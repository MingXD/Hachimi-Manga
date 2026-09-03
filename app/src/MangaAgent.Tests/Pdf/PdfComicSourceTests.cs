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

    // ---------- BUG-008：脏元数据净化（NUL/控制/格式字符 → null → 回退文件名） ----------

    [Fact]
    public async Task Dirty_Info_Title_Is_Sanitized()
    {
        if (!NativeOk) return;
        var dir = Path.Combine(Path.GetTempPath(), $"manga_pdf_{Guid.NewGuid():N}");
        // 真实样本形态（晚安布布 12 卷）：/Info Title 解析出 U+0000/空白 —— 净化后 null → 扫描回退文件名
        var spacePath = PdfTestWriter.WriteFile(dir, "space.pdf", 1, infoTitle: "  ");
        await using (var src = new PdfComicSource(spacePath))
            Assert.Null((await src.ReadMetadataAsync())?.Title);
        // 内嵌 NUL（ASCII fixture 不能编码零宽字符 → 零宽净化由实现层覆盖，此处测 NUL+空白）
        var messyPath = PdfTestWriter.WriteFile(dir, "messy.pdf", 1, infoTitle: "  \0Vol1  ");
        await using (var src2 = new PdfComicSource(messyPath))
            Assert.Equal("Vol1", (await src2.ReadMetadataAsync())?.Title);
        // 正常标题不受影响（fixture 为 ASCII 编码；中文标题净化由真实样本复验）
        var okPath = PdfTestWriter.WriteFile(dir, "ok.pdf", 1, infoTitle: "Good Vol.2");
        await using (var src3 = new PdfComicSource(okPath))
            Assert.Equal("Good Vol.2", (await src3.ReadMetadataAsync())?.Title);
        // 字面单 NUL Title 的文件 pdfium 无法解析 → 打开抛 Corrupt（坏文件跳过语义，扫描不中断）
        var nulPath = PdfTestWriter.WriteFile(dir, "nul.pdf", 1, infoTitle: "\0");
        await using (var src4 = new PdfComicSource(nulPath))
            await Assert.ThrowsAsync<PdfException>(async () => _ = src4.PageCount);
    }

    // ---------- BUG-009：渲染背景预填白（页面未绘制区 = 白而非黑） ----------

    [Fact]
    public async Task Unpainted_Margin_Renders_White_Not_Black()
    {
        if (!NativeOk) return;
        var dir = Path.Combine(Path.GetTempPath(), $"manga_pdf_{Guid.NewGuid():N}");
        // 位图不满 MediaBox、无全幅背景：四周 24pt 留白是"未绘制区"
        var path = PdfTestWriter.WriteEmbeddedImageFile(dir, "img.pdf", 1, 600, 800, marginPt: 24);
        await using var src = new PdfComicSource(path);
        Assert.False(src.CropToContent);   // 纸面视图
        var rp = await src.RenderPageAsync(0, 400);
        Assert.NotNull(rp);
        var b = rp!.Value.Bgra8888;
        int stride = rp.Value.Width * 4;
        // 边缘应为纯白不透明（FillRect 修复）；修复前是 (0,0,0,0)。
        // 纸面留白 24pt → 400px 页上 16px 内；内容从 16px 起（红/蓝）
        foreach (int col in new[] { 0, 1, 5, 10, 14 })
        {
            int o = (rp.Value.Height / 2) * stride + col * 4;
            Assert.True(b[o] == 255 && b[o + 1] == 255 && b[o + 2] == 255 && b[o + 3] == 255,
                $"col={col} 应为纯白，实际 ({b[o + 2]},{b[o + 1]},{b[o]},{b[o + 3]})");
        }
        // 内容区（图 24..576pt）：25% 处红、75% 处蓝（400px 下 x=16+0.25*368≈108 / x=16+0.75*368≈292）
        int midRow = rp.Value.Height / 2;
        int red = (int)((24 + (600 - 48) * 0.25) / 600 * 400);
        int blue = (int)((24 + (600 - 48) * 0.75) / 600 * 400);
        int oR = midRow * stride + red * 4;
        Assert.True(b[oR + 2] > 200 && b[oR] < 60, "内容左区应偏红");
        int oB = midRow * stride + blue * 4;
        Assert.True(b[oB] > 200 && b[oB + 2] < 60, "内容右区应偏蓝");
    }

    // ---------- 裁切模式（位图本身） ----------

    [Fact]
    public async Task CropToContent_Resizes_And_Crops_To_Bitmap()
    {
        if (!NativeOk) return;
        var dir = Path.Combine(Path.GetTempPath(), $"manga_pdf_{Guid.NewGuid():N}");
        var path = PdfTestWriter.WriteEmbeddedImageFile(dir, "img.pdf", 1, 600, 800, marginPt: 24);
        await using var src = new PdfComicSource(path);
        src.CropToContent = true;
        // GetPageSize = 内容区 canonical：图 552×752pt → ×300/72 → 2300×3133
        var sz = src.GetPageSize(0);
        Assert.NotNull(sz);
        Assert.Equal(2300, sz!.Value.Width);
        Assert.Equal(3133, sz.Value.Height);
        Assert.Equal(552.0 / 752.0, sz.Value.AspectRatio, 3);
        // 渲染：输出宽 ≈ 目标宽，且边缘像素 = 图内容（红左缘/蓝右缘），无纸面白
        var rp = await src.RenderPageAsync(0, 400);
        Assert.NotNull(rp);
        var b = rp!.Value.Bgra8888;
        int stride = rp.Value.Width * 4;
        Assert.True(rp.Value.Width >= 395 && rp.Value.Width <= 405, $"输出宽应≈400，实际 {rp.Value.Width}");
        int midRow = rp.Value.Height / 2;
        int oL = midRow * stride;
        Assert.True(b[oL + 2] > 200 && b[oL + 1] < 60 && b[oL] < 60 && b[oL + 3] == 255, "裁切后左缘应为红(255,0,0)");
        int oR2 = midRow * stride + (rp.Value.Width - 1) * 4;
        Assert.True(b[oR2] > 200 && b[oR2 + 2] < 60 && b[oR2 + 3] == 255, "裁切后右缘应为蓝(0,0,255)");
    }

    [Fact]
    public async Task CropToContent_No_Image_Page_Is_Not_Cropped()
    {
        if (!NativeOk) return;
        // 纯矢量页（无 IMAGE 对象）→ 裁切不生效（bbox 无效 → 整页）
        var path = WriteFixture("vector.pdf", 1, 600, 800);
        await using var src = new PdfComicSource(path);
        src.CropToContent = true;
        var sz = src.GetPageSize(0);
        Assert.NotNull(sz);
        Assert.Equal(2500, sz!.Value.Width);   // 整页 600pt@300dpi，未被裁
        Assert.Equal(3333, sz.Value.Height);
        var rp = await src.RenderPageAsync(0, 400);
        Assert.NotNull(rp);
        Assert.Equal(400, rp!.Value.Width);
    }

    [Fact]
    public async Task CropToggle_Changes_Semantics_Idempotently()
    {
        if (!NativeOk) return;
        var dir = Path.Combine(Path.GetTempPath(), $"manga_pdf_{Guid.NewGuid():N}");
        var path = PdfTestWriter.WriteEmbeddedImageFile(dir, "img.pdf", 1, 600, 800, marginPt: 24);
        await using var src = new PdfComicSource(path);
        Assert.False(src.CropToContent);
        var full = src.GetPageSize(0);
        src.CropToContent = true;
        var cropped = src.GetPageSize(0);
        Assert.True(cropped!.Value.Width < full!.Value.Width, "裁切后 canonical 宽应变小");
        src.CropToContent = true;    // 幂等
        src.CropToContent = false;   // 切回
        var back = src.GetPageSize(0);
        Assert.Equal(full.Value.Width, back!.Value.Width);
    }
}
