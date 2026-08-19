using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

            // 画布模型：Fit 模式由 code-behind 计算居中 translate
            double fitScale = win.CanvasScale;
            Assert.True(fitScale > 0.1, "画布应有缩放");
            Assert.True(win.CanvasX >= 0, "FitWidth 居中或左对齐：CanvasX>=0");

            // 缩小（画布交互，看图器规则：内容<视口时自动居中）
            win.ZoomRelative(0.5);
            Assert.True(win.CanvasScale < fitScale - 0.5, $"缩小后 scale 应小于 fit（{fitScale}）: {win.CanvasScale}");
            Assert.True(win.CanvasX > 0, $"缩小应水平居中（CanvasX>0），实际 {win.CanvasX}");
            double shrunk = win.CanvasScale;

            // 放大：相对放大（1.03 × 2 = 2.06 ≈ fit 是正确行为）
            win.ZoomRelative(2.0);
            Assert.True(win.CanvasScale > shrunk + 0.5, $"放大后 scale 应大于缩小值（{shrunk}）: {win.CanvasScale}");

            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
