using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using static MangaAgent.Tests.PoC.ReaderTestFixtures;

namespace MangaAgent.Tests.PoC;

/// <summary>P2-3 压力回归（headless）：
/// 长时连续滚动 + 快速切模式 + 缩放循环 + 关闭清理 ——
/// 断言全程无 ObjectDisposed/黑屏、缩放有界、Dispose 后缓存与 UI 引用计数归零。</summary>
public class StressRegressionTests
{
    [AvaloniaFact]
    public async Task Stress_LongScroll_RapidSwitch_ZoomLoop_NoLeak()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_stress_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            await WaitUntilAsync(() => vm.SizesLoaded, 15000);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            vm.ToggleModeCommand.Execute(null);   // -> Double
            vm.ToggleModeCommand.Execute(null);   // -> Webtoon
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 阶段 1：长时连续滚动（60 档）→ 滑行自然停止，画布不空、速度归零
            var point = new Point(550, 400);
            for (int i = 0; i < 60; i++)
            {
                win.MouseWheel(point, new Vector(0, -120), RawInputModifiers.None);
                await Task.Delay(16);
            }
            await WaitUntilAsync(() => Math.Abs(win.WebtoonScrollVelocity) < 1.0, 15000);
            Assert.True(win.WebtoonCanvas!.Children.Count > 0, "长滚后画布不应为空");
            Assert.True(win.WebtoonInstancedCount >= 1, "长滚后应有实例化页");

            // 阶段 2：快速切模式 8 轮（Single→Double→Webtoon→Single），每轮进滚动即非空（黑屏回归）
            vm.SetViewMode(ViewMode.Single);
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Single);
            for (int round = 0; round < 8; round++)
            {
                vm.ToggleModeCommand.Execute(null);   // -> Double
                vm.ToggleModeCommand.Execute(null);   // -> Webtoon
                await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
                await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);
                vm.ToggleModeCommand.Execute(null);   // -> Single
                await WaitUntilAsync(() => vm.ViewMode == ViewMode.Single);
                await WaitUntilAsync(() => vm.Image1 is not null, 8000);
            }

            // 阶段 3：缩放循环（Ctrl+滚轮放大/缩小 ×12）→ 画布不黑、scale 有界（0.2~8.0）
            vm.SetViewMode(ViewMode.Webtoon);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);
            for (int i = 0; i < 12; i++)
            {
                win.MouseWheel(point, new Vector(0, 1), RawInputModifiers.Control);
                await Task.Delay(30);
                win.MouseWheel(point, new Vector(0, -1), RawInputModifiers.Control);
                await Task.Delay(30);
            }
            await Task.Delay(800);
            Assert.True(win.WebtoonCanvas!.Children.Count > 0, "缩放循环后画布不应为空");
            Assert.True(win.CanvasScale >= 0.2 && win.CanvasScale <= 8.0,
                $"缩放应有界（scale={win.CanvasScale:F2}）");

            // 阶段 4：关闭 → 在途解码静默后引用计数归零；DisposeAsync 幂等（双调用）
            win.Close();
            await Task.Delay(1200);   // 等在途解码/预取结束（解码有 _disposed 守卫，双保险）
            await vm.DisposeAsync();
            await vm.DisposeAsync();
            Assert.Equal(0, vm.CacheCount);
            Assert.Equal(0, vm.UiRefCount);
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>P2-3：打开后立即关闭（后台尺寸补全仍在途）→ 不崩溃、无泄漏、计数归零。</summary>
    [AvaloniaFact]
    public async Task Stress_OpenThenImmediateClose_NoCrash()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_stress2_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            // 不等任何解码完成立即关闭（最坏竞态：填充/解码在途时释放）
            win.Close();
            await Task.Delay(1500);
            await vm.DisposeAsync();
            Assert.Equal(0, vm.CacheCount);
            Assert.Equal(0, vm.UiRefCount);
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
