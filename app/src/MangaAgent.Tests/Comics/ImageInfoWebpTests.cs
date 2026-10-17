using MangaAgent.Core.Comics;
using Xunit;

namespace MangaAgent.Tests.Comics;

/// <summary>WebP 尺寸解析回归（v4.3 修复 VP8 有损分支：原实现错位读 start code + 误用 VP8L 位公式）。</summary>
public class ImageInfoWebpTests
{
    private static readonly string WebpFixture = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "webp",
            "sample_vp8_1280x1899.webp"));

    private static byte[] Riff(string chunk, byte[] payload)
    {
        var s = new MemoryStream();
        void W(params byte[] b) => s.Write(b, 0, b.Length);
        var r = System.Text.Encoding.ASCII.GetBytes("RIFF");
        W(r);
        var total = 4 + 8 + payload.Length + (payload.Length & 1);
        W((byte)(total & 0xFF), (byte)((total >> 8) & 0xFF), (byte)((total >> 16) & 0xFF), (byte)((total >> 24) & 0xFF));
        W(System.Text.Encoding.ASCII.GetBytes("WEBP"));
        W(System.Text.Encoding.ASCII.GetBytes(chunk));
        W((byte)(payload.Length & 0xFF), (byte)((payload.Length >> 8) & 0xFF),
          (byte)((payload.Length >> 16) & 0xFF), (byte)((payload.Length >> 24) & 0xFF));
        W(payload);
        if ((payload.Length & 1) != 0) W(0);
        return s.ToArray();
    }

    [Fact]
    public void Vp8_Lossy_Reads_Width_Height_From_Frame_Header()
    {
        // 真实字段值（用户库样本 1280×1899）：frame tag 3B + start code(9D 01 2A) + w=1280(00 05) + h=1899(6B 07)
        var payload = new byte[] { 0x30, 0x08, 0x09, 0x9D, 0x01, 0x2A, 0x00, 0x05, 0x6B, 0x07 };
        var size = ImageInfo.ParseHeader(Riff("VP8 ", payload));
        Assert.NotNull(size);
        Assert.Equal(1280, size!.Value.Width);
        Assert.Equal(1899, size.Value.Height);
    }

    [Fact]
    public void Vp8L_Lossless_Reads_Width_Height()
    {
        // 0x2F 签名 + (w-1)=1279=0x4FF、(h-1)=1898=0x76A 的 14bit 位打包：
        // w: b1=0xFF，b2 低6位=0x04；h: b2 高2位=0b10（b2=0x84），b3=0xDA，b4 低4位=0x01
        var payload = new byte[] { 0x2F, 0xFF, 0x84, 0xDA, 0x01 };
        var size = ImageInfo.ParseHeader(Riff("VP8L", payload));
        Assert.NotNull(size);
        Assert.Equal(1280, size!.Value.Width);
        Assert.Equal(1899, size.Value.Height);
    }

    [Fact]
    public void Vp8X_Canvas_Reads_Width_Height()
    {
        // flags(1) + reserved(3) + 24bit(w-1) + 24bit(h-1)
        var payload = new byte[] { 0x00, 0x00, 0x00, 0x00, 0xFF, 0x04, 0x00, 0x6A, 0x07, 0x00 };
        var size = ImageInfo.ParseHeader(Riff("VP8X", payload));
        Assert.NotNull(size);
        Assert.Equal(1280, size!.Value.Width);
        Assert.Equal(1899, size.Value.Height);
    }

    [Fact]
    public void Real_Vp8_Sample_Parses_Correctly()
    {
        if (!File.Exists(WebpFixture)) return;   // 夹具缺失：跳过（不中断基线）
        using var fs = File.OpenRead(WebpFixture);
        var head = new byte[65536];
        int read = fs.Read(head, 0, head.Length);
        var size = ImageInfo.ParseHeader(head.AsSpan(0, read));
        Assert.NotNull(size);
        Assert.Equal(1280, size!.Value.Width);
        Assert.Equal(1899, size.Value.Height);
        // 页面比例应为竖版（此前 bug 解析成 2.45 横条 → 被当横向页单拼/显示极小）
        Assert.True(size.Value.AspectRatio < 1.0, $"webp 比例应 < 1（竖版），实际 {size.Value.AspectRatio:F3}");
    }
}
