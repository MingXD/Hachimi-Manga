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

/// <summary>滚动模式修复回归（scroll-mode-fix A/B/C）：
/// 1) 进入滚动模式画布立即非空 —— 黑屏回归（不再需要滚轮才出现）
/// 2) 跳页（输入页码/预览栏）视图滚动定位 + 竞态不黑屏
/// 3) 惯性方向正确：滚轮向下 → 单调下滑停止，不回弹
/// 4) 复位/默认视图：当前页适应屏幕 + 居中（与单页 FitScreen 语义一致）</summary>
public class ScrollModeFixesTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    /// <summary>压力书：300 页 800×1200 PNG（比例 0.6667，全部等比例）。</summary>
    private static BookInfo StressBook => new(
        Path.Combine(Fixtures, "Stress", "stress_300p.cbz"),
        "stress_300p", "Stress", null, BookKind.Zip, DateTime.UtcNow, 300);

    private static readonly double PageRatio = 800.0 / 1200.0;

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(30);
        Assert.True(cond(), "条件超时未满足");
    }

    /// <summary>打开窗口并切到滚动模式（Single→Double→Webtoon）。</summary>
    private static async Task<ReaderWindow> OpenWebtoonAsync(ReaderViewModel vm)
    {
        var win = new ReaderWindow { DataContext = vm };
        win.Show();
        win.Measure(new Size(1100, 800));
        win.Arrange(new Rect(0, 0, 1100, 800));
        vm.ToggleModeCommand.Execute(null);   // Single -> Double
        vm.ToggleModeCommand.Execute(null);   // Double -> Webtoon
        await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
        return win;
    }

    [AvaloniaFact]
    public async Task EnterWebtoon_CanvasPopulates_WithoutScroll()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf1_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            // 黑屏回归：不滚动，等解码后画布必须有页
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);
            Assert.True(win.WebtoonCanvas!.Children.Count > 0, "进入滚动模式后画布不应为空（黑屏）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task EnterWebtoon_DefaultView_FitAndCentered()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf2_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            // 默认视图：当前页（封面）适应屏幕（完全可见）+ 水平垂直居中，scale=1
            var vp = win.ViewportSize;
            double expectedW = Math.Min(vp.Width, vp.Height * PageRatio);   // 页高 = 宽/ratio ≤ 视口高
            Assert.Equal(1.0, win.CanvasScale, 3);
            Assert.Equal((vp.Width - expectedW) / 2, win.CanvasX, 2);
            // 封面页高 = 宽/ratio = 视口高（min 取页高约束）→ ty≈0：页恰好铺满视口（居中兜底）
            Assert.True(Math.Abs(win.CanvasY) < 2, $"封面应垂直铺满视口（ty={win.CanvasY}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task JumpToPhysical_MovesView_AndNoBlackScreen()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf3_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);
            var vp = win.ViewportSize;
            double pageH = Math.Min(vp.Width, vp.Height * PageRatio) / PageRatio;

            // 输入页码跳转（对话框等价路径：命令 + 视图滚动）：画面必须移动到目标页
            vm.JumpToPhysicalCommand.Execute(100);
            win.CallScrollWebtoonToPhysical(100);
            double expectedTy = -(100 * pageH);
            Assert.True(Math.Abs(win.CanvasY - expectedTy) < 15,
                $"跳页后应定位到页 101（ty={win.CanvasY} 期望 {expectedTy}）");

            // 跳页后画布仍有页（不黑屏）
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);
            Assert.True(win.WebtoonCanvas!.Children.Count > 0, "跳页后画布不应为空");

            // 竞态：连续跳页，画布仍非空
            vm.JumpToPhysicalCommand.Execute(250);
            win.CallScrollWebtoonToPhysical(250);
            await Task.Delay(800);
            Assert.True(win.WebtoonInstancedCount >= 1, "连续跳页后画布不应为空");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task WheelScroll_GlidesDown_Monotonic_NoBounce()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf4_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            double y0 = win.CanvasY;
            win.MouseWheel(new Point(550, 400), new Vector(0, -1), RawInputModifiers.None);   // 向下滚一格
            // 惯性回归：滑动过程单调向下（ty 只减不增，无回弹），最后停住
            double prev = y0;
            double last = y0;
            int samples = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 1800)
            {
                await Task.Delay(60);
                double cur = win.CanvasY;
                Assert.True(cur <= prev + 1.0, $"惯性方向错误/回弹: {prev} -> {cur}");
                prev = cur;
                last = cur;
                samples++;
                if (Math.Abs(win.WebtoonScrollVelocity) < 1.0 && sw.ElapsedMilliseconds > 300) break;
            }
            Assert.True(samples > 3, "应有足够采样点");
            Assert.True(last < y0 - 20, $"滚轮向下应滑行一段距离（y0={y0} last={last}）");
            await Task.Delay(150);
            Assert.True(Math.Abs(win.WebtoonScrollVelocity) < 1.0, "滚动结束后速度应归零");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task ResetWebtoon_FitAndCentered()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf5_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 跳到书中部（页 151 顶对齐），再复位
            vm.JumpToPhysicalCommand.Execute(150);
            win.CallScrollWebtoonToPhysical(150);
            await Task.Delay(400);
            win.CallResetWebtoonView();

            var vp = win.ViewportSize;
            double expectedW = Math.Min(vp.Width, vp.Height * PageRatio);
            double pageH = expectedW / PageRatio;
            Assert.Equal(1.0, win.CanvasScale, 3);                       // 回默认缩放
            Assert.Equal((vp.Width - expectedW) / 2, win.CanvasX, 2);    // 水平居中
            double expectedTy = -(150 * pageH);                          // 复位目标 = 可见页 151，垂直居中
            Assert.True(Math.Abs(win.CanvasY - expectedTy) < 15,
                $"复位后当前页应居中（ty={win.CanvasY} 期望 {expectedTy}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task RapidModeSwitch_Webtoon_StillPopulates()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf6_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            for (int round = 0; round < 3; round++)
            {
                vm.ToggleModeCommand.Execute(null);   // Single -> Double
                vm.ToggleModeCommand.Execute(null);   // Double -> Webtoon
                await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
                await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);   // 进入即非空（黑屏回归）
                vm.ToggleModeCommand.Execute(null);   // Webtoon -> Single
                await WaitUntilAsync(() => vm.ViewMode == ViewMode.Single);
            }
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
