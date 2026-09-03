using MangaAgent.Core.Comics;
using MangaAgent.Core.Comics.Pdf;
using Xunit;

namespace MangaAgent.Tests.Pdf;

/// <summary>PdfComicSource 单测（依赖 pdfium.dll —— Directory.Build.targets 已复制到测试输出；
/// dll 缺失时用例直接返回，沿用 5000p 压力书"环境不具备则跳过"先例）。</summary>
public class PdfComicSourceTests
{
    private static bool NativeOk => PdfiumLibrary.IsAvailable;

    private static string WriteFixture(string name, int pages, int w = 600, int h = 800)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"manga_pdf_{Guid.NewGuid():N}");
        return PdfTestWriter.WriteFile(dir, name, pages, w, h);
    }

    [Fact]
    public async Task PageCount_And_Sizes_Are_Read()
    {
        if (!NativeOk) return;
        var path = WriteFixture("3p.pdf", 3, 600, 800);
        await using var src = new PdfComicSource(path);
        Assert.Equal(3, src.PageCount);
        // canonical 300dpi：600pt → 2500px；800pt → 3333px
        var sz = src.GetPageSize(0);
        Assert.NotNull(sz);
        Assert.Equal(2500, sz!.Value.Width);
        Assert.Equal(3333, sz.Value.Height);
        // 比例正确（驱动双页布局）
        Assert.Equal(600.0 / 800.0, sz.Value.AspectRatio, 3);
    }

    [Fact]
    public async Task RenderPageAsync_Produces_Requested_Width_With_Content()
    {
        if (!NativeOk) return;
        var path = WriteFixture("3p.pdf", 3, 600, 800);
        await using var src = new PdfComicSource(path);
        var rp = await src.RenderPageAsync(0, 1000);
        Assert.NotNull(rp);
        Assert.Equal(1000, rp!.Value.Width);
        Assert.Equal(1333, rp.Value.Height);          // 1000 * 800/600
        Assert.Equal(rp.Value.Width * rp.Value.Height * 4, rp.Value.Bgra8888.Length);
        // 页 0 顶部色块 = 红 (255,26,26)：采样块区域（PNG 顶行 y≈(H-20..H-60) 映射到小 y）
        bool foundRed = false;
        var b = rp.Value.Bgra8888;
        int stride = rp.Value.Width * 4;
        for (int row = 8; row < 60 && !foundRed; row++)
            for (int col = 40; col < rp.Value.Width - 40; col += 7)
            {
                int o = row * stride + col * 4;
                if (b[o + 2] > 200 && b[o + 1] < 90 && b[o] < 90 && b[o + 3] == 255)
                {
                    foundRed = true;
                    break;
                }
            }
        Assert.True(foundRed, "页 0 顶部红色块未渲染（颜色通道或坐标错误）");
    }

    [Fact]
    public async Task RenderWidth_Rescales_Height_Proportionally()
    {
        if (!NativeOk) return;
        var path = WriteFixture("landscape.pdf", 1, 800, 400);   // 横向页
        await using var src = new PdfComicSource(path);
        var rp = await src.RenderPageAsync(0, 1200);
        Assert.NotNull(rp);
        Assert.Equal(1200, rp!.Value.Width);
        Assert.Equal(600, rp.Value.Height);   // 1200 * 400/800
    }

    [Fact]
    public async Task OpenPageAsync_Returns_Decodable_Png_Stream()
    {
        if (!NativeOk) return;
        var path = WriteFixture("1p.pdf", 1);
        await using var src = new PdfComicSource(path);
        await using var stream = await src.OpenPageAsync(0);
        var head = new byte[8];
        await stream.ReadAsync(head);
        // PNG 签名 89 50 4E 47 0D 0A 1A 0A
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, head);
    }

    [Fact]
    public async Task Corrupt_File_Throws_PdfException_Corrupt()
    {
        if (!NativeOk) return;
        var dir = Path.Combine(Path.GetTempPath(), $"manga_pdf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "garbage.pdf");
        File.WriteAllBytes(path, new byte[] { 0x1, 0x2, 0x3, 0x4, 0x5 });
        await using var src = new PdfComicSource(path);
        var ex = await Assert.ThrowsAsync<PdfException>(async () => _ = src.PageCount);
        Assert.Equal(PdfErrorKind.Corrupt, ex.Kind);
    }

    [Fact]
    public async Task Chinese_Path_Opens_And_Renders()
    {
        if (!NativeOk) return;
        var dir = Path.Combine(Path.GetTempPath(), $"漫画测试_{Guid.NewGuid():N}");
        var path = PdfTestWriter.WriteFile(dir, "第一册.pdf", 1);
        await using var src = new PdfComicSource(path);
        Assert.Equal(1, src.PageCount);
        var rp = await src.RenderPageAsync(0, 400);
        Assert.NotNull(rp);
        Assert.Equal(400, rp!.Value.Width);
    }

    [Fact]
    public async Task Dispose_Is_Idempotent_And_Blocks_Reopen()
    {
        if (!NativeOk) return;
        var path = WriteFixture("1p.pdf", 1);
        var src = new PdfComicSource(path);
        _ = src.PageCount;
        await src.DisposeAsync();
        await src.DisposeAsync();   // 幂等不抛
        Assert.Throws<ObjectDisposedException>(() => _ = src.PageCount);
    }

    [Fact]
    public async Task ReadMetadata_Returns_Info_Title_Or_Null()
    {
        if (!NativeOk) return;
        var path = WriteFixture("1p.pdf", 1);
        await using var src = new PdfComicSource(path);
        var meta = await src.ReadMetadataAsync();
        // 测试 writer 未写 /Info → 应为 null（不抛）
        Assert.Null(meta?.Title);
    }
}
