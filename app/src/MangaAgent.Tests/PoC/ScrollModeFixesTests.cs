using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.Services;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using static MangaAgent.Tests.PoC.ReaderTestFixtures;

namespace MangaAgent.Tests.PoC;

/// <summary>滚动模式修复回归（scroll-mode-fix A/B/C）—— 核心滚动/视图行为：
/// 1) 进入滚动模式画布立即非空 —— 黑屏回归（不再需要滚轮才出现）
/// 2) 跳页（输入页码/预览栏）视图滚动定位 + 竞态不黑屏
/// 3) 惯性方向正确：滚轮向下 → 单调下滑停止，不回弹
/// 4) 复位/默认视图：当前页适应屏幕 + 居中（与单页 FitScreen 语义一致）
/// 5) 键盘：↑↓ 滑动、←→ 整页顶对齐跳；坏页占位。
/// （P1-3 拆自原 893 行文件；Fit/缩放类测试移入 ScrollModeFitTests，设置类移入 SettingsSystemTests）</summary>
public class ScrollModeFixesTests
{
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
            win.MouseWheel(new Point(550, 400), new Vector(0, -120), RawInputModifiers.None);   // 向下滚一档
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

    /// <summary>v2 回归：再次进入滚动模式必须恢复默认视图（适应屏幕+居中+scale=1），
    /// 不得携带分页残留的缩放/平移（问题1：贴着左边+放大）。</summary>
    [AvaloniaFact]
    public async Task ReEnterWebtoon_Refits_DefaultView()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf7_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            vm.ToggleModeCommand.Execute(null);   // -> Double
            vm.ToggleModeCommand.Execute(null);   // -> Webtoon
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);
            // 退出滚动 → 分页放大 → 再进入滚动
            vm.SetViewMode(ViewMode.Single);
            await Task.Delay(300);
            win.ZoomRelative(2.5);
            Assert.True(win.CanvasScale > 2.0, "分页应已放大（前置条件）");
            vm.SetViewMode(ViewMode.Webtoon);
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
            // 再进入必须重设：scale=1、水平居中、页 0 铺满视口高
            var vp = win.ViewportSize;
            double expectedW = Math.Min(vp.Width, vp.Height * PageRatio);
            Assert.Equal(1.0, win.CanvasScale, 3);
            Assert.Equal((vp.Width - expectedW) / 2, win.CanvasX, 2);
            Assert.True(Math.Abs(win.CanvasY) < 2, $"再次进入后页 0 应铺满视口（ty={win.CanvasY}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v2/v3 回归：滚轮抖动免疫（±1 只产生微小脉冲不反弹）；
    /// 弱 Delta（-30）也必须有滚动效果（v3 比例脉冲根治"滚轮无作用"）；一档 -120 正常滚动。</summary>
    [AvaloniaFact]
    public async Task WheelDeltaJitter_DoesNotBounce()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf8_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 抖动：±1 交替（触摸板惯性尾流）→ 比例脉冲微小且正负相抵，不应有明显移动/反弹
            double y0 = win.CanvasY;
            win.MouseWheel(new Point(550, 400), new Vector(0, -1), RawInputModifiers.None);
            win.MouseWheel(new Point(550, 400), new Vector(0, 1), RawInputModifiers.None);
            win.MouseWheel(new Point(550, 400), new Vector(0, -1), RawInputModifiers.None);
            win.MouseWheel(new Point(550, 400), new Vector(0, 1), RawInputModifiers.None);
            await Task.Delay(400);
            Assert.True(Math.Abs(win.CanvasY - y0) < 8.0, $"±1 抖动不应明显移动（ty {y0} -> {win.CanvasY}）");

            // 弱 Delta（-30，弱 Delta 设备常见）→ 必须滚动（v3.1：无死区无下限，比例生效）
            win.MouseWheel(new Point(550, 400), new Vector(0, -30), RawInputModifiers.None);
            await Task.Delay(800);
            double yWeak = win.CanvasY;
            Assert.True(yWeak < y0 - 20, $"弱 Delta 也应产生滚动（ty {y0} -> {yWeak}）");

            // 一档真实向下 → 正常滚动并滑行停住
            win.MouseWheel(new Point(550, 400), new Vector(0, -120), RawInputModifiers.None);
            await Task.Delay(1000);
            double yAfterDown = win.CanvasY;
            Assert.True(yAfterDown < yWeak - 50, $"一档向下应滚动（ty {yWeak} -> {yAfterDown}）");

            // 滑行结束后的小幅反向抖动（惯性尾流）→ 不应把画面往回拉
            win.MouseWheel(new Point(550, 400), new Vector(0, 1), RawInputModifiers.None);
            win.MouseWheel(new Point(550, 400), new Vector(0, -1), RawInputModifiers.None);
            await Task.Delay(400);
            Assert.True(win.CanvasY < yAfterDown + 8.0,
                $"滑行后反向抖动不应把画面拉回（ty {yAfterDown} -> {win.CanvasY}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v2 回归：预览栏/输入页码跳页即时定位，且画面立即有图或占位（不黑屏）。</summary>
    [AvaloniaFact]
    public async Task PreviewJump_Instant_NoBlack()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf9_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            // 不等初始解码完成立即跳页（最坏竞态：初始解码仍在途）
            vm.JumpToPhysicalCommand.Execute(150);
            win.CallScrollWebtoonToPhysical(150);
            // 立即定位（同步）
            var vp = win.ViewportSize;
            double pageH = Math.Min(vp.Width, vp.Height * PageRatio) / PageRatio;
            Assert.True(Math.Abs(win.CanvasY - (-150 * pageH)) < 15, "跳页应立即定位到目标页");
            // 画面不黑：有实例化图 或 有占位块
            Assert.True(win.WebtoonInstancedCount >= 1 || win.WebtoonPlaceholderCount >= 1,
                $"跳页后画面不应全黑（inst={win.WebtoonInstancedCount} placeholder={win.WebtoonPlaceholderCount}）");
            await Task.Delay(1500);
            Assert.True(win.WebtoonInstancedCount >= 1, "解码完成后应有实例化图");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v2 回归：滚动模式视口任意处（含两侧空白）可拖拽（消除拖拽死区）。</summary>
    [AvaloniaFact]
    public async Task DragOnMargin_Moves()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf10_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 左侧空白（条居中于 280..820，x=100 是视口背景空白区）
            double y0 = win.CanvasY;
            win.MouseDown(new Point(100, 400), MouseButton.Left, RawInputModifiers.None);
            for (int i = 1; i <= 8; i++)
            {
                win.MouseMove(new Point(100, 400 - i * 40), RawInputModifiers.None);
                await Task.Delay(16);
            }
            win.MouseUp(new Point(100, 80), MouseButton.Left, RawInputModifiers.None);
            await Task.Delay(100);
            Assert.True(win.CanvasY < y0 - 100, $"空白区拖拽应生效（ty {y0} -> {win.CanvasY}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v3 实证：解码失败页必须显示占位块（而非黑块空图），且不崩溃、重试有界。</summary>
    [AvaloniaFact]
    public async Task CorruptPage_ShowsPlaceholder_NotBlack()
    {
        var cbz = CreateCorruptCbz();
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf11_{Guid.NewGuid():N}.db");
        try
        {
            var book = new BookInfo(cbz, "corrupt", "corrupt", null, BookKind.Zip, DateTime.UtcNow, 3);
            var vm = new ReaderViewModel(book, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            vm.ToggleModeCommand.Execute(null);   // -> Double
            vm.ToggleModeCommand.Execute(null);   // -> Webtoon
            await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
            await Task.Delay(1500);
            // 坏页（物理 2）应显示占位（不黑）；好页应有实例
            Assert.True(win.WebtoonPlaceholderCount >= 1, $"坏页应保留占位块（placeholder={win.WebtoonPlaceholderCount}）");
            Assert.True(win.WebtoonInstancedCount >= 1, "好页应有实例化图");
            // 重试周期结束（3×800ms）后：仍无崩溃、坏页仍占位
            await Task.Delay(3500);
            Assert.True(win.WebtoonPlaceholderCount >= 1, "重试结束后坏页仍应显示占位");
            Assert.True(win.WebtoonInstancedCount >= 1);
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(cbz); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>v3 回归：键盘 ↑↓ 在滚动模式滚动、分页模式翻页。</summary>
    [AvaloniaFact]
    public async Task Keyboard_UpDown_ScrollsOrPages()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf14_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 滚动模式：↓ 向下滚
            double y0 = win.CanvasY;
            win.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            await Task.Delay(900);
            Assert.True(win.CanvasY < y0 - 100, $"↓ 应向下滚动（ty {y0} -> {win.CanvasY}）");

            // 滚动模式：↑ 向上滚
            double y1 = win.CanvasY;
            win.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
            await Task.Delay(900);
            Assert.True(win.CanvasY > y1 + 100, $"↑ 应向上滚动（ty {y1} -> {win.CanvasY}）");

            // 分页模式（v2 默认键表：分页组不含 ↑↓）→ ↓ 无绑定不应翻页；←→ 方向感知翻页
            vm.SetViewMode(ViewMode.Single);
            vm.SetDirection(ReadingDirection2.Ltr);
            await Task.Delay(300);
            int p0 = vm.CurrentPhysical;
            win.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p0, $"分页默认无 ↓ 绑定，不应翻页（{vm.CurrentPhysical + 1}）");
            // LTR 默认：→ = 下一页
            win.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p0 + 1, $"分页 LTR → 应下一页（{p0 + 1} -> {vm.CurrentPhysical + 1}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>滚动模式 ←→ = 整页顶对齐跳（→ 下一页顶到视口顶、← 上一页顶到视口顶）；↑↓ 仍滑动。</summary>
    [AvaloniaFact]
    public async Task Webtoon_ArrowKeys_PageJumpTopAligned()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf17_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 初始在页 0（页顶对齐）→ 按 → 下一页整页跳：页 1 顶对齐视口顶
            win.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await Task.Delay(100);
            var vp = win.ViewportSize;
            double pageH = Math.Min(vp.Width, vp.Height * PageRatio) / PageRatio;
            Assert.True(Math.Abs(win.CanvasY - (-pageH)) < 15,
                $"→ 应跳到页 2 顶对齐（ty={win.CanvasY:F0} 期望 {-pageH:F0}）");
            Assert.Equal("2 / 300", vm.PageText);

            // 按 ← 上一页整页跳：回到页 0 顶
            win.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            await Task.Delay(100);
            Assert.True(Math.Abs(win.CanvasY) < 15, $"← 应回到页 1 顶对齐（ty={win.CanvasY:F0}）");

            // ↑↓ 仍是滑动（动量），不是整页跳
            double y0 = win.CanvasY;
            win.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            await Task.Delay(800);
            Assert.True(win.CanvasY < y0 - 50 && win.CanvasY > y0 - vp.Height * 2,
                $"↓ 应平滑滑动而非整页跳（ty {y0:F0} -> {win.CanvasY:F0}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }
}
