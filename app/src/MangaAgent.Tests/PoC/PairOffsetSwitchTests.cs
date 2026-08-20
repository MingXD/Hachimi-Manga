using Avalonia;
using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>BUG-3 回归（2026-08-20）：配对起点切换 / 模式切换后画面必须更新（此前竞态防护误杀）。</summary>
public class PairOffsetSwitchTests
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
    public async Task PairOffset_Switch_Updates_CurrentPage()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_po_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            // 进入双页模式
            vm.ToggleModeCommand.Execute(null);   // Single -> Double
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Double);
            // 翻到第 2 逻辑页（封面单页后是双页逻辑页）
            vm.NextPageCommand.Execute(null);
            await WaitUntilAsync(() => vm.IsDoublePageShown && vm.Image2 is not null);
            var before = (vm.Image1, vm.Image2);

            // 切换配对起点：RebuildLayout 重建 _logical → 画面必须更新（BUG-3）
            vm.TogglePairOffsetCommand.Execute(null);
            await Task.Delay(600);
            // 若画面被竞态误杀丢弃，Image1/2 保持旧值；修复后应重新解码
            Assert.False(vm.Image1 is null, "配对切换后 Image1 不应为 null");
            // 配对起点变化会改变逻辑页结构 → 当前逻辑页对应的物理页变化 → Image1 内容应不同
            // （用位图引用是否变化判断：修复后是重新解码的新位图对象）
            Assert.True(!ReferenceEquals(vm.Image1, before.Item1) || !ReferenceEquals(vm.Image2, before.Item2),
                "配对切换后画面位图应更新（BUG-3 回归）");

            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
