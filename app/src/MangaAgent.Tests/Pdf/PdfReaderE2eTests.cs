using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MangaAgent.App.Imaging;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Comics.Pdf;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.Pdf;

/// <summary>PDF 端到端（Headless）：打开 PDF → 解码/翻页/双页/滚动/缩略图走 RenderPageAsync 光栅路径。
/// 依赖 pdfium.dll（已复制到测试输出）；缺失时用例直接返回。dll 缺失不影响其余基线。</summary>
public class PdfReaderE2eTests
{
    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 15000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(50);
        Assert.True(cond(), "条件超时未满足");
    }

    private static (BookInfo Book, string Db) MakeBook(int pages = 6)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"manga_pdf_e2e_{Guid.NewGuid():N}");
        var path = PdfTestWriter.WriteFile(dir, "book.pdf", pages);
        var db = Path.Combine(Path.GetTempPath(), $"manga_pdf_e2e_db_{Guid.NewGuid():N}.db");
        return (new BookInfo(path, "pdf-book", null, null, BookKind.Pdf, DateTime.UtcNow, pages), db);
    }

    [AvaloniaFact]
    public async Task BgraBitmap_Keeps_Requested_Size()
    {
        // P0 风险消解：WriteableBitmap 直通在 Headless 平台尺寸是否保真
        var bgra = new byte[400 * 500 * 4];
        Array.Fill<byte>(bgra, 255);
        using var bmp = BgraBitmap.Create(400, 500, bgra);
        Assert.Equal(400, bmp.PixelSize.Width);
        Assert.Equal(500, bmp.PixelSize.Height);
    }

    [AvaloniaFact]
    public async Task Opens_Pdf_Navigates_Pages_And_Modes()
    {
        if (!PdfiumLibrary.IsAvailable) return;
        var (book, db) = MakeBook(6);
        try
        {
            var vm = new ReaderViewModel(book, db);
            // 1) 单页：第 1 页解码（RenderPageAsync 光栅 → WriteableBitmap）
            await WaitUntilAsync(() => vm.Image1 is not null && vm.PageText == "1 / 6");
            Assert.Equal("1 / 6", vm.PageText);
            Assert.NotNull(vm.Image1);
            Assert.True(vm.Image1!.PixelSize.Width > 100, "PDF 页应按显示宽渲染（headless 下不应是 1x1 伪影）");

            // 2) 翻页（预取相邻页解码也应走 PDF 渲染）
            vm.NextPageCommand.Execute(null);
            await WaitUntilAsync(() => vm.Image1 is not null && vm.PageText == "2 / 6");
            Assert.Equal("2 / 6", vm.PageText);

            // 3) 双页模式：逻辑第 1 页 = 封面单页后成对
            vm.ToggleModeCommand.Execute(null);
            Assert.Equal(ViewMode.Double, vm.ViewMode);
            await WaitUntilAsync(() => vm.IsDoublePageShown && vm.Image1 is not null);
            Assert.NotNull(vm.Image2);

            // 4) 滚动模式（虚拟化按宽解码 = GetPageBitmapAsync → RenderPageAsync）
            vm.ToggleModeCommand.Execute(null);
            Assert.Equal(ViewMode.Webtoon, vm.ViewMode);
            var wb = await vm.GetPageBitmapAsync(5, 600);
            Assert.NotNull(wb);
            Assert.True(wb!.PixelSize.Width > 100);

            // 5) 预览栏缩略图
            var thumb = await vm.GetThumbnailAsync(0);
            Assert.NotNull(thumb);

            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Pdf_Sizes_Feed_Layout_And_Webtoon_Ratios()
    {
        if (!PdfiumLibrary.IsAvailable) return;
        var (book, db) = MakeBook(4);
        try
        {
            var vm = new ReaderViewModel(book, db);
            await WaitUntilAsync(() => vm.SizesLoaded, 20000);
            Assert.True(vm.SizesLoaded);
            // canonical 300dpi：600×800pt → 2500×3333 → 比例 0.75
            double ratio = vm.RatioOfPage(0);
            Assert.Equal(0.75, ratio, 3);
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }
}
