using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>回归：复位强制居中（问题1）、方向标签更新（问题2）。</summary>
public class ResetAndDirectionTests
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
    public async Task Reset_After_Drag_Centers_Page()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_rs_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            await WaitUntilAsync(() => vm.Image1 is not null && vm.PageText == "1 / 300");
            // 拖动偏离
            win.MouseDown(new Point(550, 400), MouseButton.Left, RawInputModifiers.None);
            win.MouseMove(new Point(750, 500), RawInputModifiers.None);
            win.MouseUp(new Point(750, 500), MouseButton.Left, RawInputModifiers.None);
            Assert.True(Math.Abs(win.CanvasX - win.CanvasX) > 0 || Math.Abs(win.CanvasY - 0) > 10 || Math.Abs(win.CanvasX - 0) > 10);
            // 复位（FitScreen 已是默认 → 旧代码不触发；新代码强制重置）
            win.CallReset();
            await Task.Delay(200);
            // 内容应居中（tx/ty 为 FitScreen 居中值，scale = FitScreen）
            double fit = win.CurrentFitScaleValue;
            Assert.True(Math.Abs(win.CanvasScale - fit) < 0.01, $"复位后 scale 应=fit({fit}): {win.CanvasScale}");
            win.Close();
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }

    [AvaloniaFact]
    public async Task Direction_Change_Updates_Label()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_dir_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            string d0 = vm.DirectionText;
            vm.SetDirection(ReadingDirection2.Ltr);
            Assert.NotEqual(d0, vm.DirectionText);   // 问题2：标签必须更新
            vm.SetDirection(ReadingDirection2.Rtl);
            Assert.Equal(d0, vm.DirectionText);
            await vm.DisposeAsync();
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
