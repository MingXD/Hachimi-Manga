using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>预览栏覆盖层回归：①不再占 DockPanel 布局（不推挤图片）②背景绑定主题色（浅/深半透明）③页码在卡片右侧不溢出 + 当前页高亮 ④动态张数（固定卡片96×130，随视口高度增减，极小窗口缩到1张）。</summary>
public class PreviewStripOverlayTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    private static BookInfo StressBook => new(
        Path.Combine(Fixtures, "Stress", "stress_300p.cbz"),
        "stress_300p", "Stress", null, BookKind.Zip, DateTime.UtcNow, 300);

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 8000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(50);
        Assert.True(cond(), "条件超时未满足");
    }

    /// <summary>唤出预览栏并返回 ThumbStrip（ShowPreviewStrip 私有，反射调用）。</summary>
    private static ItemsControl ShowStrip(ReaderWindow win)
    {
        var show = typeof(ReaderWindow).GetMethod("ShowPreviewStrip",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(show);
        show.Invoke(win, null);
        var strip = win.FindControl<ItemsControl>("ThumbStrip");
        Assert.NotNull(strip);
        return strip;
    }

    /// <summary>断言：张数与公式一致、当前页恰一项 IsCurrent、内容纵向不溢出。布局后调用。</summary>
    private static void AssertStripConsistent(ReaderWindow win, ItemsControl thumbStrip, ReaderViewModel vm)
    {
        var viewport = win.FindControl<Border>("Viewport");
        Assert.NotNull(viewport);
        int expected = Math.Clamp((int)((viewport.Bounds.Height - 58) / 136.0), 1, 11);
        Assert.InRange(expected, 1, 11);
        Assert.Equal(expected, thumbStrip.Items.Count);

        var items = thumbStrip.Items.OfType<ReaderWindow.ThumbStripItem>().ToList();
        // 卡片高填满剩余高度：cardH = clamp((H-64-6(n-1))/n, 80, 150)（64 = 按钮26×2 + RowSpacing6×2）
        double expectedCardH = Math.Clamp((viewport.Bounds.Height - 64 - 6 * (expected - 1)) / expected, 80, 150);
        Assert.True(Math.Abs(items[0].CardHeight - expectedCardH) < 0.5,
            $"卡片高不符: 实际={items[0].CardHeight} 期望={expectedCardH}");
        Assert.All(items, x => Assert.InRange(x.CardHeight, 80, 150));
        var curPhys = vm.CurrentPhysical;
        Assert.Equal(1, items.Count(x => x.IsCurrent));
        Assert.Contains(items, x => x.Physical == curPhys && x.IsCurrent);

        var scroller = thumbStrip.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller is not null)
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 1,
                $"预览栏内容纵向溢出: extent={scroller.Extent.Height} viewport={scroller.Viewport.Height}");
    }

    [AvaloniaFact]
    public async Task PreviewStrip_IsOverlay_NotDockChild_And_ThemeBg()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_preview_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));

            var strip = win.FindControl<Border>("PreviewStrip");
            Assert.NotNull(strip);
            var viewport = win.FindControl<Border>("Viewport");
            Assert.NotNull(viewport);

            // 1) 覆盖层：PreviewStrip 是 Viewport 的可视后代（叠加在其上，而非 DockPanel 兄弟）
            Assert.Contains(viewport, strip.GetVisualAncestors());

            // 2) 不占 DockPanel 布局：不是根 DockPanel 的直接子级
            Assert.IsType<DockPanel>(win.Content);
            var dock = (DockPanel)win.Content;
            Assert.DoesNotContain(strip, dock.Children);

            // 3) 显示/隐藏不改变 Viewport 尺寸（覆盖层不参与布局）
            var w0 = viewport.Bounds.Width;
            strip.IsVisible = true;
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            var w1 = viewport.Bounds.Width;
            Assert.Equal(w0, w1);
            strip.IsVisible = false;

            // 4) 主题色：VM 属性随 IsDark 切换（深→浅，颜色不同但均半透明）
            Assert.True(vm.IsDark);
            var darkBg = vm.PreviewStripBg;
            var darkItem = vm.PreviewStripItemBg;
            var darkLabel = vm.PreviewStripLabel;
            var darkHighlight = vm.PreviewStripHighlight;
            vm.ToggleThemeCommand.Execute(null);
            Assert.False(vm.IsDark);
            Assert.NotEqual(darkBg, vm.PreviewStripBg);
            Assert.NotEqual(darkItem, vm.PreviewStripItemBg);
            Assert.NotEqual(darkLabel, vm.PreviewStripLabel);
            Assert.NotEqual(darkHighlight, vm.PreviewStripHighlight);   // 高亮色随主题切换
            // 半透明：ARGB 前两位 alpha != FF
            Assert.NotEqual("FF", vm.PreviewStripBg.Substring(1, 2));

            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task PreviewStrip_DynamicCount_CurrentHighlight_NoVerticalOverflow()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_preview2_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            await Task.Delay(200);
            var thumbStrip = ShowStrip(win);
            await Task.Delay(200);   // 等缩略图异步填充

            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            AssertStripConsistent(win, thumbStrip, vm);

            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task PreviewStrip_TinyWindow_ShrinksToOne()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_preview3_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 340));
            win.Arrange(new Rect(0, 0, 1100, 340));
            await Task.Delay(200);
            var thumbStrip = ShowStrip(win);
            await Task.Delay(200);

            win.Measure(new Size(1100, 340));
            win.Arrange(new Rect(0, 0, 1100, 340));
            AssertStripConsistent(win, thumbStrip, vm);   // 期望 n=1（视口高 ~290 → (290-58)/136=1.7 → 1）

            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }
}
