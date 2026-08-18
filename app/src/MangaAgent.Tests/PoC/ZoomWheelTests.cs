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
            double scale0 = vm.Scale;

            // Ctrl+滚轮向上 → 缩放（不是翻页）
            win.MouseWheel(new Point(500, 400), new Vector(0, 120), RawInputModifiers.Control);
            await WaitUntilAsync(() => vm.Scale > scale0 || vm.CurrentIndex != page0, 3000);
            Assert.True(vm.Scale > scale0, $"Ctrl+滚轮应缩放: scale {scale0}->{vm.Scale}");
            Assert.Equal(page0, vm.CurrentIndex);   // 不应翻页

            // 普通滚轮 → 翻页（不缩放）
            double s1 = vm.Scale;
            win.MouseWheel(new Point(500, 400), new Vector(0, -120), RawInputModifiers.None);
            await WaitUntilAsync(() => vm.CurrentIndex != page0, 3000);
            Assert.Equal(page0 + 1, vm.CurrentIndex);
            Assert.Equal(s1, vm.Scale);             // 不应缩放
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
