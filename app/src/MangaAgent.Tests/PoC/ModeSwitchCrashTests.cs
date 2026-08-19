using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>BUG-004 回归：模式切换（双页→Webtoon→单页→…）快速连续 + 翻页制造缓存压力，
/// 不应因位图被缓存淘汰 Dispose 而抛 ObjectDisposedException（Image.MeasureOverride → Bitmap.get_Size）。</summary>
public class ModeSwitchCrashTests
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
    public async Task Rapid_ModeSwitch_DoesNot_Throw_ObjectDisposed()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_bug004_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);

            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));

            // 翻几页制造缓存压力，然后快速连续切换模式 3 轮（布局触发布局 → 位图 Size 访问）
            for (int pg = 0; pg < 10; pg++)
            {
                vm.NextPageCommand.Execute(null);
                await Task.Delay(80);
            }
            for (int round = 0; round < 3; round++)
            {
                vm.ToggleModeCommand.Execute(null);
                await Task.Delay(120);
                vm.ToggleModeCommand.Execute(null);
                await Task.Delay(120);
            }
            // 稳定在 Webtoon 等待渲染
            vm.ToggleModeCommand.Execute(null);
            await Task.Delay(500);

            // 再翻页（Webtoon 下 CurrentIndex 仍有效）
            for (int pg = 0; pg < 3; pg++) vm.NextPageCommand.Execute(null);
            await Task.Delay(300);

            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }
}
