using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>Ctrl+滚轮 = 缩放（稳定判定：事件修饰键 + 键盘状态兜底）。</summary>
public class ZoomWheelTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    private static BookInfo StressBook => new(
        Path.Combine(Fixtures, "Stress", "stress_300p.cbz"),
        "stress_300p", "Stress", null, BookKind.Zip, DateTime.UtcNow, 300);

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 8000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(50);
        Assert.True(cond(), "条件超时");
    }

    [AvaloniaFact]
    public async Task CtrlWheel_Zooms_Not_Pages()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_zw_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null);
            int page0 = vm.CurrentIndex;
            double scale0 = win.CanvasScale;   // 画布绝对值（默认 FitScreen ~0.92）

            // 滚轮向上 → 缩放（不是翻页）——看图器：滚轮即缩放
            win.MouseWheel(new Point(500, 400), new Vector(0, 120), RawInputModifiers.None);
            await WaitUntilAsync(() => win.CanvasScale > scale0 + 0.001 || vm.CurrentIndex != page0, 3000);
            Assert.True(win.CanvasScale > scale0, $"滚轮应缩放: scale {scale0}->{win.CanvasScale}");
            Assert.Equal(page0, vm.CurrentIndex);   // 不应翻页

            // 滚轮向下 → 缩小
            double s1 = win.CanvasScale;
            win.MouseWheel(new Point(500, 400), new Vector(0, -120), RawInputModifiers.None);
            await WaitUntilAsync(() => win.CanvasScale < s1 - 0.001, 3000);
            Assert.True(win.CanvasScale < s1, "滚轮向下应缩小");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
