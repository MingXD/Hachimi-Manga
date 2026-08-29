using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>快速翻页回归（2026-08-20）：跳页命令 + 进度条联动。</summary>
public class QuickJumpTests
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
    public async Task JumpToPage_FromFirst_ToLater()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_jump_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            // 封面单页 → 共 151 逻辑页（0..150）；跳到第 100 逻辑页（0-based 99）
            vm.JumpToPageCommand.Execute(99);
            await WaitUntilAsync(() => vm.CurrentIndex == 99 && vm.PageText == "198 / 300");
            // BUG-A v1.9：单页模式显示物理页号（逻辑99 → 物理198）
            Assert.Equal("198 / 300", vm.PageText);

            // 越界 clamp
            vm.JumpToPageCommand.Execute(10000);
            await WaitUntilAsync(() => vm.CurrentIndex == vm.TotalLogical - 1);
            vm.JumpToPageCommand.Execute(-5);
            await WaitUntilAsync(() => vm.CurrentIndex == 0);

            // Webtoon 物理页跳转
            vm.ToggleModeCommand.Execute(null);   // Single -> Double
            vm.ToggleModeCommand.Execute(null);   // Double -> Webtoon
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
            vm.JumpToPhysicalCommand.Execute(250);   // 跳到物理页 251
            await Task.Delay(400);
            Assert.True(vm.CurrentIndex > 100, $"物理页 251 应对应逻辑页 >100（实际 {vm.CurrentIndex}）");

            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v3 #8 回归：一键首页/末页（单页=物理页边界，双页=逻辑页边界，滚动=物理页边界）。</summary>
    [AvaloniaFact]
    public async Task GoHome_GoEnd_Navigate_To_Boundaries()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_home_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);

            // 单页模式：翻到中间 → GoHome 回物理页 0 → GoEnd 到最后一物理页
            vm.NextPageCommand.Execute(null);
            vm.NextPageCommand.Execute(null);
            vm.GoHomeCommand.Execute(null);
            await Task.Delay(200);
            Assert.Equal(0, vm.CurrentPhysical);
            vm.GoEndCommand.Execute(null);
            await Task.Delay(200);
            Assert.Equal(vm.PhysicalPageCount - 1, vm.CurrentPhysical);

            // 双页模式：GoHome → 逻辑页 0（封面单页）；GoEnd → 最后逻辑页
            vm.ToggleModeCommand.Execute(null);   // Single -> Double
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Double);
            vm.GoHomeCommand.Execute(null);
            await Task.Delay(200);
            Assert.Equal(0, vm.CurrentIndex);
            vm.GoEndCommand.Execute(null);
            await Task.Delay(200);
            Assert.Equal(vm.TotalLogical - 1, vm.CurrentIndex);

            // 滚动模式：GoHome → 物理页 0
            vm.ToggleModeCommand.Execute(null);   // Double -> Webtoon
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
            vm.GoHomeCommand.Execute(null);
            await Task.Delay(200);
            Assert.Equal(0, vm.CurrentPhysical);

            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
