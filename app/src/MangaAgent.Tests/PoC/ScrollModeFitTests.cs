using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using static MangaAgent.Tests.PoC.ReaderTestFixtures;

namespace MangaAgent.Tests.PoC;

/// <summary>滚动模式适应模式/缩放回归（P1-3 拆自 ScrollModeFixesTests）：
/// 适应宽度/高度语义与单页一致、放大按需重解码防模糊、复位无旧宽度残留。</summary>
public class ScrollModeFitTests
{
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
}
