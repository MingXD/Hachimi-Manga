using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>Webtoon 虚拟化回归（2026-08-20 重构）：
/// 1) Canvas 只实例化可见页 ±缓冲（内存可控，不全量 300 页）
/// 2) 滚动后虚拟化范围跟随移动（增量增删）
/// 3) 滚动/缩放不抛异常</summary>
public class WebtoonVirtualizationTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    private static BookInfo StressBook => new(
        Path.Combine(Fixtures, "Stress", "stress_300p.cbz"),
        "stress_300p", "Stress", null, BookKind.Zip, DateTime.UtcNow, 300);

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(30);
        Assert.True(cond(), "条件超时未满足");
    }

    [AvaloniaFact]
    public async Task Webtoon_Virtualizes_And_Scrolls()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_wt_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);

            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));

            // 进入 Webtoon
            vm.ToggleModeCommand.Execute(null);   // Single -> Double
            vm.ToggleModeCommand.Execute(null);   // Double -> Webtoon
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
            await Task.Delay(1200);   // 等待虚拟化异步解码

            // 虚拟化：已实例化页数应远小于总页数（300）
            var canvas = win.WebtoonCanvas;
            Assert.NotNull(canvas);
            int inst = win.WebtoonInstancedCount;
            Assert.True(inst < 20, $"虚拟化应只实例化可见±缓冲页（实际 {inst} 页）");
            Assert.True(inst >= 1, "至少应有 1 页实例化");

            // 滚动：调用滚轮事件推进多页，虚拟化范围应移动且不崩溃
            // （scroll-mode-fix v2：滚轮走累积器，需真实档位 Delta -120）
            var point = new Point(550, 400);
            double ty0 = win.CanvasY;
            for (int i = 0; i < 12; i++)
            {
                win.MouseWheel(point, new Vector(0, -120), Avalonia.Input.RawInputModifiers.None);
                await Task.Delay(60);
            }
            await Task.Delay(800);
            Assert.True(win.CanvasY != ty0, $"滚轮应滚动 _ty: {ty0}->{win.CanvasY} scale={win.CanvasScale}");
            // 滚动后仍有实例化页（范围移动，不空）
            Assert.True(canvas.Children.Count > 0, "滚动后 Canvas 不应为空");
            // 状态文本应为物理页号（>1 表示已滚离首页）
            Assert.NotEqual("1 / 300", vm.PageText);

            // Ctrl+滚轮缩放不崩溃
            win.MouseWheel(point, new Vector(0, 1), Avalonia.Input.RawInputModifiers.Control);
            await Task.Delay(300);
            Assert.True(canvas.Children.Count > 0);

            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }
}
