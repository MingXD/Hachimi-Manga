using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>BUG-004/物理页导航回归：快速模式切换 + 翻页不抛 ObjectDisposedException。
/// 注：headless teardown 会在窗口未销毁时渲染最后一帧，故窗口测试在切换回分页模式后立即关闭。</summary>
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
    public async Task Rapid_ModeSwitch_VM_No_DisposedCrash()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_bug004_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            // VM 层快速切换 + 翻页（无窗口，纯逻辑压力）
            for (int i = 0; i < 10; i++)
            {
                vm.NextPageCommand.Execute(null);
                await Task.Delay(30);
            }
            for (int round = 0; round < 3; round++)
            {
                vm.ToggleModeCommand.Execute(null);   // Single→Double→Webtoon 循环
                await Task.Delay(60);
                vm.NextPageCommand.Execute(null);
                await Task.Delay(60);
            }
            vm.SetViewMode(ViewMode.Single);
            await Task.Delay(200);
            Assert.True(vm.Image1 is not null, "切换后应有当前页");
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task Rapid_ModeSwitch_Window_No_DisposedCrash()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_bug004w_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            // 快速切换 + 翻页（窗口级：触发布局 → 位图 Size 访问）
            for (int round = 0; round < 2; round++)
            {
                vm.ToggleModeCommand.Execute(null);
                await Task.Delay(80);
                vm.ToggleModeCommand.Execute(null);
                await Task.Delay(80);
            }
            vm.SetViewMode(ViewMode.Single);
            await Task.Delay(200);
            win.Close();
            await Task.Delay(200);
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
