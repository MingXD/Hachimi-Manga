using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>诊断：进入漫画后第一次按 ← 不翻页（焦点在 ◀ 按钮被白框框起）——验证焦点/键盘导航机制。</summary>
public class ArrowKeyFocusDiagTests
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
    public async Task ArrowLeft_FirstKey_NoPageTurn_FocusDiagnostic()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_key_{Guid.NewGuid():N}.db");
        try
        {
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null);
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            await Task.Delay(100);

            int p0 = vm.CurrentPhysical;
            var f0 = win.FocusManager?.GetFocusedElement()?.GetType().Name;
            win.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            await Task.Delay(80);
            int p1 = vm.CurrentPhysical;
            var f1 = win.FocusManager?.GetFocusedElement()?.GetType().Name;
            win.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
            await Task.Delay(80);
            int p2 = vm.CurrentPhysical;
            var f2 = win.FocusManager?.GetFocusedElement()?.GetType().Name;

            Assert.True(p1 != p0 || p2 != p0,
                $"两次←都没翻页: p0={p0} p1={p1} p2={p2}; focus: {f0} -> {f1} -> {f2}");
            Assert.True(p2 == p0 + 2,
                $"预期翻2页: p0={p0} p1={p1} p2={p2}; focus: {f0} -> {f1} -> {f2}");
            win.Close();
            await vm.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }
};
