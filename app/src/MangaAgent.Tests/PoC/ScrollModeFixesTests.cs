using System.IO.Compression;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.Services;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
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

    // ---------- v3 回归 ----------

    /// <summary>构造含 1 个损坏条目的 CBZ：页 2 的 deflate 压缩数据被破坏 →
    /// ZipArchive 读取该条目时抛 InvalidDataException → 真实"解码失败"路径。
    /// 注：DecodeToWidth 对垃圾字节从不抛异常（返回占位图），必须破坏流读取才能触发失败。</summary>
    private static string CreateCorruptCbz()
    {
        var path = Path.Combine(Path.GetTempPath(), $"manga_corrupt_{Guid.NewGuid():N}.cbz");
        var png1 = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("0001.png", CompressionLevel.Optimal); using (var s = e1.Open()) s.Write(png1);
            var e2 = zip.CreateEntry("0002.png", CompressionLevel.Optimal); using (var s = e2.Open()) s.Write(png1);
            var e3 = zip.CreateEntry("0003.png", CompressionLevel.Optimal); using (var s = e3.Open()) s.Write(png1);
        }
        // 解析本地头，定位条目 2 的 deflate 数据并破坏（→ 解压抛异常）
        var bytes = File.ReadAllBytes(path);
        int off = 0;
        for (int entry = 1; entry <= 3; entry++)
        {
            int compSize = BitConverter.ToInt32(bytes, off + 18);          // 压缩大小
            int nameLen = BitConverter.ToUInt16(bytes, off + 26);          // 文件名长
            int extraLen = BitConverter.ToUInt16(bytes, off + 28);         // 扩展长
            int dataStart = off + 30 + nameLen + extraLen;
            if (entry == 2)
                for (int i = dataStart; i < dataStart + Math.Min(compSize, 8); i++) bytes[i] = 0x00;
            off = dataStart + compSize;
        }
        File.WriteAllBytes(path, bytes);
        return path;
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

    /// <summary>v3 回归：复位（宽度重算）后画布不得残留旧宽度图片（防"两页之间"错位）。</summary>
    [AvaloniaFact]
    public async Task ResetWebtoon_NoStaleWidthImages()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf12_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 先缩放到非默认宽度（模拟用户缩放后的复位），再复位
            win.MouseWheel(new Point(550, 400), new Vector(0, 1), RawInputModifiers.Control);   // Ctrl+滚轮放大
            await Task.Delay(300);
            win.CallResetWebtoonView();
            await Task.Delay(800);
            // 复位后所有画布 Image 宽度必须 == 当前基准宽（无旧宽度残留）
            foreach (var ch in win.WebtoonCanvas!.Children.OfType<Image>())
            {
                Assert.True(Math.Abs(ch.Width - win.WebtoonWidthValue) < 0.5,
                    $"复位后画布 Image 宽度应等于当前基准宽 {win.WebtoonWidthValue:F1}（实际 {ch.Width:F1}）");
            }
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v3 回归：放大后可见页自动按更高宽度重解码（防模糊）；百分比 = scale×100（fit=100%）。</summary>
    [AvaloniaFact]
    public async Task ZoomIn_RefreshesSharpness_AndPercent()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf13_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // fit = 100%（与单页一致）
            Assert.Equal(100.0, vm.ZoomPercent, 1);

            // 跳到未被预解码的页（100，滚动模式无分页预解码 → 仅按显示宽度 540 解码）
            vm.JumpToPhysicalCommand.Execute(100);
            win.CallScrollWebtoonToPhysical(100);
            await WaitUntilAsync(() => vm.CachedDecodeWidth(100) >= 400, 8000);
            int w0 = vm.CachedDecodeWidth(100);
            Assert.True(w0 < 1000, $"前置：页 100 应按显示宽度解码（实际 {w0}）");

            // Ctrl+滚轮放大 2 次 → 可见页自动按更高宽度重解码（防模糊）
            win.MouseWheel(new Point(550, 400), new Vector(0, 1), RawInputModifiers.Control);
            await Task.Delay(200);
            win.MouseWheel(new Point(550, 400), new Vector(0, 1), RawInputModifiers.Control);
            await Task.Delay(1200);
            Assert.True(win.CanvasScale > 1.0, "应已放大");
            int w1 = vm.CachedDecodeWidth(100);
            Assert.True(w1 > w0, $"放大后缓存宽度应提升（{w0} -> {w1}）");
            // 百分比跟随 scale（fit=100%）
            Assert.True(Math.Abs(vm.ZoomPercent - win.CanvasScale * 100.0) < 1.0,
                $"百分比应对应缩放（scale={win.CanvasScale:F2} percent={vm.ZoomPercent:F0}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
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

    /// <summary>v3.1 回归：滚动模式"适应宽度"= 条宽铺满视口（与单页 FitWidth 一致）。</summary>
    [AvaloniaFact]
    public async Task FitWidth_FillsViewport()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf15_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // 初始为适应屏幕（条宽窄于视口）→ 按一次适应按钮 → FitWidth：条宽 = 视口宽、tx=0、fit=100%
            Assert.True(win.WebtoonWidthValue < win.ViewportSize.Width - 10, "前置：适应屏幕条宽应窄于视口");
            vm.CycleFitCommand.Execute(null);   // FitScreen -> FitWidth
            await Task.Delay(600);
            Assert.True(Math.Abs(win.WebtoonWidthValue - win.ViewportSize.Width) < 1.0,
                $"适应宽度后条宽应 = 视口宽（{win.WebtoonWidthValue:F1} vs {win.ViewportSize.Width:F1}）");
            Assert.True(Math.Abs(win.CanvasX) < 1.0, "铺满宽度 → tx=0");
            Assert.Equal(100.0, vm.ZoomPercent, 1);
            // 画面有内容（重建完成）
            Assert.True(win.WebtoonInstancedCount >= 1 || win.WebtoonPlaceholderCount >= 1, "适应宽度后画面不应全黑");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    /// <summary>v3.2 回归：滚动模式"适应高度"= 当前页高铺满视口（与单页 FitHeight 一致）。</summary>
    [AvaloniaFact]
    public async Task FitHeight_FillsViewportHeight()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_wtf16_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            vm.CycleFitCommand.Execute(null);   // FitScreen -> FitWidth
            await Task.Delay(300);
            vm.CycleFitCommand.Execute(null);   // FitWidth -> FitHeight
            await Task.Delay(600);

            // 条宽 = 视口高 × 当前页比例（页高 = 视口高，单页 FitHeight 语义）
            double expectedW = win.ViewportSize.Height * PageRatio;
            Assert.True(Math.Abs(win.WebtoonWidthValue - expectedW) < 2.0,
                $"适应高度后条宽应 = 视口高×比例（{win.WebtoonWidthValue:F1} vs {expectedW:F1}）");
            Assert.Equal(100.0, vm.ZoomPercent, 1);
            Assert.True(win.WebtoonInstancedCount >= 1 || win.WebtoonPlaceholderCount >= 1, "适应高度后画面不应全黑");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    // ---------- v4 设置系统回归 ----------

    /// <summary>设置存取往返 + 使用介绍默认生成。</summary>
    [AvaloniaFact]
    public async Task SettingsStore_RoundTrip_AndGuideGenerated()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings { ZoomSensitivity = 1.5, WebtoonSpeed = 1.2, ScrollFriction = 8, CacheCap = 16, InvertScroll = true, PagedWheelMode = "Page" };
            s.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)] = new() { "X" };
            SettingsStore.Save(s);
            var loaded = SettingsStore.Load();
            Assert.Equal(1.5, loaded.ZoomSensitivity);
            Assert.Equal(1.2, loaded.WebtoonSpeed);
            Assert.Equal(8, loaded.ScrollFriction);
            Assert.Equal(16, loaded.CacheCap);
            Assert.True(loaded.InvertScroll);
            Assert.Equal("Page", loaded.PagedWheelMode);
            Assert.Equal(new[] { "X" }, loaded.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)]);
            Assert.True(File.Exists(SettingsStore.GuidePath), "首次运行应生成默认使用介绍");
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>滚动模式 ←→ = 整页顶对齐跳（→ 下一页顶到视口顶、← 上一页顶到视口顶）。</summary>
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

    /// <summary>快捷键重绑生效：把滚动"下一页整页跳"绑到 X → X 触发、Right 不再触发。</summary>
    [AvaloniaFact]
    public async Task HotkeyRebind_ChangesAction()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings();
            s.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)] = new() { "X" };
            SettingsStore.Save(s);

            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf18_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // Right 已解绑 → 不应整页跳
            win.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await Task.Delay(200);
            Assert.True(Math.Abs(win.CanvasY) < 15, $"解绑后 → 不应跳页（ty={win.CanvasY:F0}）");

            // X → 整页跳
            win.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.None);
            await Task.Delay(200);
            var vp = win.ViewportSize;
            double pageH = Math.Min(vp.Width, vp.Height * PageRatio) / PageRatio;
            Assert.True(Math.Abs(win.CanvasY - (-pageH)) < 15,
                $"重绑 X 应整页跳（ty={win.CanvasY:F0} 期望 {-pageH:F0}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>分页滚轮行为=翻页：分页模式滚轮翻页而非缩放。</summary>
    [AvaloniaFact]
    public async Task PagedWheelMode_Page_Pages()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings { PagedWheelMode = "Page" };
            SettingsStore.Save(s);

            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf19_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));

            int p0 = vm.CurrentPhysical;
            win.MouseWheel(new Point(550, 400), new Vector(0, -120), RawInputModifiers.None);   // 向下滚 → 下一页
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p0 + 1, $"分页滚轮=翻页：向下应下一页（{p0 + 1} -> {vm.CurrentPhysical + 1}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v2 回归：分页 LTR/RTL 分组独立 —— 默认 RTL：←=下一页；切 LTR 后：←=上一页。</summary>
    [AvaloniaFact]
    public async Task Paged_DirectionGroups_Separate()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf20_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            vm.SetDirection(ReadingDirection2.Rtl);
            await Task.Delay(200);

            // RTL：← = 下一页
            int p0 = vm.CurrentPhysical;
            win.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p0 + 1, $"RTL ← 应下一页（{p0 + 1} -> {vm.CurrentPhysical + 1}）");

            // 切 LTR：← = 上一页
            vm.SetDirection(ReadingDirection2.Ltr);
            await Task.Delay(200);
            int p1 = vm.CurrentPhysical;
            win.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            await Task.Delay(300);
            Assert.True(vm.CurrentPhysical == p1 - 1, $"LTR ← 应上一页（{p1 + 1} -> {vm.CurrentPhysical + 1}）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            SettingsStore.BaseDir = old;
            try { Directory.Delete(baseDir, true); } catch { }
        }
    }

    /// <summary>v2 回归：组合键（Ctrl+Shift+P）绑定生效；解绑的普通键不再触发。</summary>
    [AvaloniaFact]
    public async Task ComboKey_Binding_Works()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), $"manga_set_{Guid.NewGuid():N}");
        var old = SettingsStore.BaseDir;
        SettingsStore.BaseDir = baseDir;
        try
        {
            var s = new AppSettings();
            s.Hotkeys["Webtoon"][nameof(ReaderAction.WebtoonNextPage)] = new() { "Ctrl+Shift+P" };
            SettingsStore.Save(s);

            var db = Path.Combine(Path.GetTempPath(), $"manga_wtf21_{Guid.NewGuid():N}.db");
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = await OpenWebtoonAsync(vm);
            await WaitUntilAsync(() => win.WebtoonInstancedCount >= 1, 8000);

            // Right 已解绑 → 不跳
            win.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            await Task.Delay(200);
            Assert.True(Math.Abs(win.CanvasY) < 15, $"解绑后 → 不应跳页（ty={win.CanvasY:F0}）");

            // Ctrl+Shift+P → 整页跳
            win.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.Control | RawInputModifiers.Shift);
            await Task.Delay(200);
            var vp = win.ViewportSize;
            double pageH = Math.Min(vp.Width, vp.Height * PageRatio) / PageRatio;
            Assert.True(Math.Abs(win.CanvasY - (-pageH)) < 15,
                $"组合键应整页跳（ty={win.CanvasY:F0} 期望 {-pageH:F0}）");

            // 单独 P（无修饰）不应触发
            double yAfter = win.CanvasY;
            win.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);
            await Task.Delay(200);
            Assert.True(Math.Abs(win.CanvasY - yAfter) < 15, $"单独 P 不应触发组合键绑定（ty={win.CanvasY:F0}）");
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
