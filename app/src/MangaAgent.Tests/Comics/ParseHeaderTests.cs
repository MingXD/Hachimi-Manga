using MangaAgent.Core.Comics;
using Xunit;

namespace MangaAgent.Tests.Comics;

/// <summary>v3 根因验证：ImageInfo.ParseHeader 逐字节扫描 JPEG 找 SOF（0xFFC0-0xCF）——
/// APP1/EXIF 段数据里常见内嵌缩略图（本身含 FFD8/FFC0），会被误判为"图片尺寸段"→ 解析出缩略图尺寸而非真实尺寸。
/// 后果：Auto 清晰度档信任错误 origW → 主动降采样到缩略图宽度 → 翻页模糊且不升级。</summary>
public class ParseHeaderTests
{
    /// <summary>构造"APP1 内含假 0xFFC0（EXIF 缩略图）+ 真实 SOF0(3000×3000)"的 JPEG 头，
    /// 断言 ParseHeader 应返回真实尺寸。若返回缩略图尺寸 → 证明误匹配 bug。</summary>
    [Fact]
    public void JPEG_With_ExifThumbnail_Parses_Real_Size_Not_Thumbnail()
    {
        var bytes = new List<byte>();
        // SOI
        bytes.Add(0xFF); bytes.Add(0xD8);
        // APP1 段：FFE1 + len + 数据（EXIF 字符串 + 内嵌 JPEG 缩略图的假 0xFFC0 标记 + 假尺寸 16×16）
        var exifData = new byte[]
        {
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,          // "Exif\0\0"
            0xFF, 0xD8,                                   // 缩略图 SOI
            0xFF, 0xC0,                                   // ← 缩略图 SOF（会被误匹配！）
            0x00, 0x0B,                                   // 假段长
            0x08,                                         // 假精度
            0x00, 0x10, 0x00, 0x10,                       // 假 16×16
            0x03, 0x00, 0x00, 0x00,                       // 假组件
        };
        bytes.Add(0xFF); bytes.Add(0xE1);
        ushort app1Len = (ushort)(2 + exifData.Length);
        bytes.Add((byte)(app1Len >> 8)); bytes.Add((byte)(app1Len & 0xFF));
        bytes.AddRange(exifData);
        // 真实 SOF0：3000×3000
        bytes.Add(0xFF); bytes.Add(0xC0);
        bytes.Add(0x00); bytes.Add(0x11);   // 段长 17
        bytes.Add(0x08);                    // 精度
        bytes.Add(0x0B); bytes.Add(0xB8);   // 高 3000
        bytes.Add(0x0B); bytes.Add(0xB8);   // 宽 3000
        bytes.Add(0x03);                    // 组件数
        bytes.AddRange(new byte[9]);        // 组件数据
        bytes.Add(0xFF); bytes.Add(0xD9);   // EOI

        var size = ImageInfo.ParseHeader(bytes.ToArray());
        Assert.NotNull(size);
        // 若命中缩略图假 SOF → 返回 16×16；正确实现应返回 3000×3000
        Assert.Equal(3000, size.Value.Width);
        Assert.Equal(3000, size.Value.Height);
    }

    /// <summary>v3 扩展：GIF 尺寸（头第 6~9 字节，小端）。</summary>
    [Fact]
    public void GIF_Header_Parses_Size()
    {
        var head = new byte[] {
            (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            0xE0, 0x07,   // 宽 2016
            0x90, 0x01,   // 高 400
        };
        var size = ImageInfo.ParseHeader(head);
        Assert.NotNull(size);
        Assert.Equal(2016, size.Value.Width);
        Assert.Equal(400, size.Value.Height);
    }

    /// <summary>v3 扩展：BMP 尺寸（BITMAPINFOHEADER：DIB 头偏移 14，宽高在偏移 18/22，小端；高度可负）。</summary>
    [Fact]
    public void BMP_Header_Parses_Size()
    {
        var head = new byte[54];
        head[0] = (byte)'B'; head[1] = (byte)'M';
        head[14] = 40;                 // BITMAPINFOHEADER 大小
        head[18] = 0x40; head[19] = 0x1F;   // 宽 8000
        head[22] = 0xE0; head[23] = 0x2E;   // 高 12000（正）
        var size = ImageInfo.ParseHeader(head);
        Assert.NotNull(size);
        Assert.Equal(8000, size.Value.Width);
        Assert.Equal(12000, size.Value.Height);

        // 高度为负（自顶向下位图）
        head[22] = 0x20; head[23] = 0xD1;   // 0xFFFFD120 = -12000
        head[24] = 0xFF; head[25] = 0xFF;
        var size2 = ImageInfo.ParseHeader(head);
        Assert.NotNull(size2);
        Assert.Equal(12000, size2.Value.Height);
    }

    /// <summary>v3 扩展：WebP 无损（VP8L：0x2F + 14bit 宽高）。</summary>
    [Fact]
    public void WebP_Lossless_Parses_Size()
    {
        var head = new byte[32];
        head[0] = (byte)'R'; head[1] = (byte)'I'; head[2] = (byte)'F'; head[3] = (byte)'F';
        head[8] = (byte)'W'; head[9] = (byte)'E'; head[10] = (byte)'B'; head[11] = (byte)'P';
        // VP8L 块
        head[12] = (byte)'V'; head[13] = (byte)'P'; head[14] = (byte)'8'; head[15] = (byte)'L';
        head[16] = 10; head[17] = 0; head[18] = 0; head[19] = 0;   // 块大小
        head[20] = 0x2F;                       // VP8L signature
        // w = (b1 | (b2&0x3F)<<8) + 1 = 1000+1 = 1001
        head[21] = 0xE8; head[22] = 0x03;
        // h: ((b2>>6) | b3<<2 | (b4&0x0F)<<10) + 1 → b2=0x03: b2>>6=0; b3=0x00<<2=0; b4=0x01<<10=1024 → 1024+1=1025
        head[23] = 0x00; head[24] = 0x01;
        var size = ImageInfo.ParseHeader(head);
        Assert.NotNull(size);
        Assert.Equal(1001, size.Value.Width);
        Assert.Equal(1025, size.Value.Height);
    }
}
