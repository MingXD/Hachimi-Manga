using Avalonia;
using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>诊断：连续点击缩放按钮，scale 是否每次变化（问题3）。</summary>
public class ZoomButtonDiagTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    private static BookInfo StressBook => new(
        Path.Combine(Fixtures, "Stress", "stress_300p.cbz"),
        "stress_300p", "Stress", null, BookKind.Zip, DateTime.UtcNow, 300);

    private static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(30);
        Assert.True(cond(), "条件超时未满足");
    }

    [AvaloniaFact]
    public async Task ZoomIn_Button_Changes_Scale_Each_Click()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_zb_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null && vm.PageText == "1 / 300");
            double s0 = win.CanvasScale;
            win.ZoomRelative(1.25);
            await Task.Delay(200);
            double s1 = win.CanvasScale;
            win.ZoomRelative(1.25);
            await Task.Delay(200);
            double s2 = win.CanvasScale;
            win.ZoomRelative(1.25);
            await Task.Delay(200);
            double s3 = win.CanvasScale;
            Assert.True(s1 > s0 + 0.01, $"第1次缩放应生效: {s0}->{s1}");
            Assert.True(s2 > s1 + 0.01, $"第2次缩放应生效: {s1}->{s2}");
            Assert.True(s3 > s2 + 0.01, $"第3次缩放应生效: {s2}->{s3}");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
