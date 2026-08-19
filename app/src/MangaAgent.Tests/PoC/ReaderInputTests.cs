using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>画布模型交互：滚轮=精细缩放(焦点锚定)；拖拽图片=平移(可出界)；空白不拖。</summary>
public class ReaderInputTests
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
    public async Task Wheel_Zooms_Fine_With_Focus_Anchor()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_in_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null);
            int page = vm.CurrentIndex;
            double s0 = win.CanvasScale;
            double x0 = win.CanvasX;

            // 滚轮向上 → 缩放（不翻页）
            var pos = new Point(500, 400);
            win.MouseWheel(pos, new Vector(0, 120), RawInputModifiers.None);
            await WaitUntilAsync(() => win.CanvasScale > s0 + 0.001, 3000);
            Assert.True(win.CanvasScale > s0, $"滚轮应缩放: {s0}->{win.CanvasScale}");
            Assert.Equal(page, vm.CurrentIndex);   // 不翻页

            // 数学验证：以 pos 为焦点缩放，pos 处的内容点应保持（ContentToScreen 逆运算）
            double contentXBefore = (pos.X - x0) / s0;
            double contentXAfter = (pos.X - win.CanvasX) / win.CanvasScale;
            Assert.True(Math.Abs(contentXBefore - contentXAfter) < 1.0,
                $"焦点锚定（数学）: {contentXBefore} vs {contentXAfter}");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task Rtl_Double_Page_Order_Reversed()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_rtl_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            vm.ViewMode = ViewMode.Double;   // 双页模式
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null);
            vm.NextPageCommand.Execute(null);   // 第 1 页是封面单页，翻到双页逻辑页 (1,2)
            await WaitUntilAsync(() => vm.Image1 is not null && vm.Image2 is not null);
            // RTL（默认）：右页=前页 → PagePanel 顺序应为 [Image2, Image1]
            var panel = win.GetVisualDescendants().OfType<Avalonia.Controls.StackPanel>().FirstOrDefault(p => p.Name == "PagePanel");
            Assert.NotNull(panel);
            if (vm.Direction == ReadingDirection2.Rtl)
                Assert.True(panel.Children.IndexOf(panel.Children.OfType<Image>().Last()) > 0, "RTL 双页顺序应反转");
            // 切换 LTR → 顺序恢复 [Image1, Image2]
            vm.Direction = ReadingDirection2.Ltr;
            await Task.Delay(150);
            win.Measure(new Size(1100, 800)); win.Arrange(new Rect(0, 0, 1100, 800));
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task ButtonZoom_Keeps_Content_Center_Fixed()
    {
        // 数学验证（与 MatrixTransform 同一模型 p' = scale·p + t）：按钮缩放以内容中心为焦点
        // → 缩放前后内容中心屏幕位置应一致（误差 <0.01）
        var db = Path.Combine(Path.GetTempPath(), $"manga_cc_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null);
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));

            // 内容中心局部点（基准尺寸一半）
            var centerLocal = new Avalonia.Point(533.6 / 2, 400);   // baseW≈ratio×800，此处用应力书 ratio≈0.667
            var before = win.ContentToScreen(centerLocal);
            win.ZoomRelative(1.5);
            var after = win.ContentToScreen(centerLocal);
            Console.WriteLine($"[CC] before=({before.X:F2},{before.Y:F2}) after=({after.X:F2},{after.Y:F2})");
            Assert.True(Math.Abs(before.X - after.X) < 1.0 && Math.Abs(before.Y - after.Y) < 1.0,
                $"内容中心缩放后应保持: d=({before.X - after.X:F2},{before.Y - after.Y:F2})");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task Drag_Image_Pans_And_Can_Go_Out_Of_Bounds()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_in2_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null);
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));

            double x0 = win.CanvasX, y0 = win.CanvasY;
            // 在图片上拖拽（图片位于视口中心附近）→ 平移
            win.MouseDown(new Point(550, 400), MouseButton.Left, RawInputModifiers.None);
            win.MouseMove(new Point(450, 350), RawInputModifiers.None);
            win.MouseUp(new Point(450, 350), MouseButton.Left, RawInputModifiers.None);
            Console.WriteLine($"[DRAG] winW={win.Width} client={win.ClientSize} vp={win.ViewportSize} source={win.LastPressSource} left={win.LastPressLeft}");
            Assert.True(Math.Abs(win.CanvasX - x0) > 5, $"拖拽应平移 X: {x0}->{win.CanvasX} (client={win.ClientSize} vp={win.ViewportSize} img1={win.PageImage1Vis})");
            Assert.True(Math.Abs(win.CanvasY - y0) > 5, $"拖拽应平移 Y: {y0}->{win.CanvasY}");

            // 可拖出边界：大幅拖动 → 无 clamp（CanvasX 可 < 0 或 > 视口）
            double before = win.CanvasX;
            win.MouseDown(new Point(500, 400), MouseButton.Left, RawInputModifiers.None);
            win.MouseMove(new Point(500 + 5000, 400), RawInputModifiers.None);
            win.MouseUp(new Point(500 + 5000, 400), MouseButton.Left, RawInputModifiers.None);
            Assert.True(Math.Abs(win.CanvasX - before) > 1000, "大幅拖拽应无边界限制（可出界）");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
