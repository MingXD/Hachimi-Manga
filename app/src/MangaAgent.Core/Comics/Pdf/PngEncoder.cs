using System.IO.Compression;

namespace MangaAgent.Core.Comics.Pdf;

/// <summary>极简 PNG 编码器（纯 BCL，无第三方依赖）：把 BGRA8888 像素缓冲编码为 RGBA PNG。
/// 用途：PdfComicSource.OpenPageAsync 的"渲染 → PNG 流"兜底（通用图片解码调用方零改动接入）。</summary>
internal static class PngEncoder
{
    private static readonly uint[] s_crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>BGRA8888（自顶向下，alpha 通常 255）→ RGBA PNG 字节流。</summary>
    public static byte[] EncodeBgra(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            throw new ArgumentException("非法的像素缓冲尺寸");

        // 扫描线：每行前置 filter 类型字节 0（None）
        byte[] raw = new byte[height * (1 + width * 4)];
        for (int row = 0; row < height; row++)
        {
            int src = row * width * 4;
            int dst = row * (1 + width * 4);
            raw[dst] = 0;   // filter: None
            for (int i = 0; i < width * 4; i += 4)
            {
                raw[dst + 1 + i] = bgra[src + i + 2];     // R
                raw[dst + 1 + i + 1] = bgra[src + i + 1]; // G
                raw[dst + 1 + i + 2] = bgra[src + i];     // B
                raw[dst + 1 + i + 3] = bgra[src + i + 3]; // A
            }
        }

        using var zlib = new MemoryStream();
        using (var zs = new ZLibStream(zlib, CompressionLevel.Fastest, leaveOpen: true))
            zs.Write(raw, 0, raw.Length);
        byte[] idatData = zlib.ToArray();

        using var outMs = new MemoryStream();
        outMs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        void Chunk(string type, ReadOnlySpan<byte> data)
        {
            Span<byte> typeBytes = stackalloc byte[4];
            typeBytes[0] = (byte)type[0];
            typeBytes[1] = (byte)type[1];
            typeBytes[2] = (byte)type[2];
            typeBytes[3] = (byte)type[3];
            Span<byte> len = stackalloc byte[4];
            WriteBe32(len, data.Length);
            outMs.Write(len);
            outMs.Write(typeBytes);
            outMs.Write(data);
            // CRC = type + data 联合校验
            Span<byte> crcBuf = stackalloc byte[4];
            uint crc = 0xFFFFFFFFu;
            crc = UpdateCrc(crc, typeBytes);
            crc = UpdateCrc(crc, data);
            WriteBe32(crcBuf, (int)(crc ^ 0xFFFFFFFFu));
            outMs.Write(crcBuf);
        }

        Span<byte> ihdr = stackalloc byte[13];
        WriteBe32(ihdr[..4], width);
        WriteBe32(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;    // bit depth
        ihdr[9] = 6;    // color type: RGBA
        ihdr[10] = 0;   // compression
        ihdr[11] = 0;   // filter
        ihdr[12] = 0;   // interlace
        Chunk("IHDR", ihdr);
        Chunk("IDAT", idatData);
        Chunk("IEND", ReadOnlySpan<byte>.Empty);
        return outMs.ToArray();
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = s_crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    private static void WriteBe32(Span<byte> dst, int value)
    {
        dst[0] = (byte)(value >> 24);
        dst[1] = (byte)(value >> 16);
        dst[2] = (byte)(value >> 8);
        dst[3] = (byte)value;
    }
}
