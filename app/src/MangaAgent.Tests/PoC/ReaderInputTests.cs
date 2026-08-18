using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>基础阅读操作验证：滚轮翻页、拖拽平移（Avalonia.Headless 官方输入模拟）。</summary>
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
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(50);
        Assert.True(cond(), "条件超时");
    }

    [AvaloniaFact]
    public async Task MouseWheel_Turns_Page_Not_Scrolls()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_in_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null);
            Assert.Equal(0, vm.CurrentIndex);

            // 向下滚 = 下一页
            win.MouseWheel(new Point(500, 400), new Vector(0, -120), RawInputModifiers.None);
            await WaitUntilAsync(() => vm.CurrentIndex == 1);
            Assert.Equal(1, vm.CurrentIndex);

            // 向上滚 = 上一页
            win.MouseWheel(new Point(500, 400), new Vector(0, 120), RawInputModifiers.None);
            await WaitUntilAsync(() => vm.CurrentIndex == 0);
            Assert.Equal(0, vm.CurrentIndex);
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [Fact]
    public void PanOffset_Clamps_To_Content_Range()
    {
        // 向右拖（dx<0）→ offset.X 增大；限制在 maxX
        var r1 = ReaderWindow.PanOffset(new Avalonia.Vector(0, 0), -120, 0, 500, 300);
        Assert.Equal(120, r1.X);
        Assert.Equal(0, r1.Y);
        // 超界 clamp
        var r2 = ReaderWindow.PanOffset(new Avalonia.Vector(0, 0), -999, 0, 500, 300);
        Assert.Equal(500, r2.X);
        // 向左拖（dx>0）→ offset.X 减小
        var r3 = ReaderWindow.PanOffset(new Avalonia.Vector(200, 100), 80, 0, 500, 300);
        Assert.Equal(120, r3.X);
        Assert.Equal(100, r3.Y);
        // 负 max（内容小于视口）→ 不允许平移
        var r4 = ReaderWindow.PanOffset(new Avalonia.Vector(0, 0), -120, 0, -10, -10);
        Assert.Equal(0, r4.X);
        Assert.Equal(0, r4.Y);
        // 垂直平移
        var r5 = ReaderWindow.PanOffset(new Avalonia.Vector(0, 0), 0, -80, 500, 300);
        Assert.Equal(0, r5.X);
        Assert.Equal(80, r5.Y);
    }
}
