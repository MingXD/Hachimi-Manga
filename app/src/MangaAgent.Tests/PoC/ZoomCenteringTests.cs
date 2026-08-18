using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>缩放居中布局验证：缩小内容居中完整显示；放大左对齐可滚动（Grid 居中容器）。</summary>
public class ZoomCenteringTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    private static BookInfo StressBook => new(
        Path.Combine(Fixtures, "Stress", "stress_300p.cbz"),
        "stress_300p", "Stress", null, BookKind.Zip, DateTime.UtcNow, 300);

    [AvaloniaFact]
    public async Task Zoom_Centers_When_Smaller_Aligns_Left_When_Larger()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_zoom_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (vm.Image1 is null && sw.ElapsedMilliseconds < 8000) await Task.Delay(50);
            Assert.NotNull(vm.Image1);

            var panel = win.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(p => p.Name == "PagePanel");
            Assert.NotNull(panel);

            // 缩小：内容 < 视口 → 水平居中（X > 0）
            vm.FitMode = FitMode.Custom;
            vm.Scale = 0.5;
            await Task.Delay(150);
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            var pos1 = panel.TranslatePoint(new Avalonia.Point(0, 0), win);
            Assert.NotNull(pos1);
            Console.WriteLine($"[ZOOM] shrink X={pos1!.Value.X:F0}");
            Assert.True(pos1.Value.X > 100, $"缩小应水平居中（X>100），实际 X={pos1.Value.X:F0}");

            // 放大：内容 > 视口 → 左对齐（X≈0，可滚动）
            vm.Scale = 2.0;
            await Task.Delay(150);
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            var pos2 = panel.TranslatePoint(new Avalonia.Point(0, 0), win);
            Assert.NotNull(pos2);
            Console.WriteLine($"[ZOOM] zoom X={pos2!.Value.X:F0}");
            Assert.True(pos2.Value.X <= 15, $"放大应左对齐（X<=15），实际 X={pos2.Value.X:F0}");

            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
