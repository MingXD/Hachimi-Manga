using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>M1 E2E：真实阅读器窗口（Headless）—— 翻页/双页/主题/续读/书签。</summary>
public class ReaderE2eTests
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

    [AvaloniaFact]
    public async Task Reader_Navigates_DoublePage_Theme_Resume_Bookmarks()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_e2e_{Guid.NewGuid():N}.db");
        try
        {
            // 1) 打开：封面单页 + 299 页成对 → 150 逻辑页（2E 异步解码后需等待加载完成）
            var vm = new ReaderViewModel(StressBook, db);
            await WaitUntilAsync(() => vm.Image1 is not null && vm.PageText == "1 / 300");
            Assert.Equal("1 / 300", vm.PageText); // BUG-A v1.9：单页显示物理页号
            Assert.False(vm.IsDoublePageShown);

            // 2) 翻页 + 状态保存（单页逐物理页）
            vm.NextPageCommand.Execute(null);
            await WaitUntilAsync(() => vm.Image1 is not null && vm.PageText == "2 / 300");
            Assert.Equal("2 / 300", vm.PageText);

            // 3) 双页模式：逻辑页 index1 = 物理页(1,2) 双页（2E 异步解码后等待）
            vm.ToggleModeCommand.Execute(null);
            Assert.True(vm.ViewMode == ViewMode.Double);
            await WaitUntilAsync(() => vm.IsDoublePageShown && vm.Image1 is not null && vm.Image2 is not null);
            Assert.True(vm.IsDoublePageShown);
            Assert.NotNull(vm.Image1);
            Assert.NotNull(vm.Image2);

            // 4) 主题切换
            Assert.True(vm.IsDark);
            vm.ToggleThemeCommand.Execute(null);
            Assert.False(vm.IsDark);

            // 5) 续读：新开 VM 恢复物理页（上一逻辑页首物理=1）+ 双页模式
            var vm2 = new ReaderViewModel(StressBook, db);
            Assert.True(vm2.ViewMode == ViewMode.Double);
            await WaitUntilAsync(() => vm2.CurrentIndex == 1, 3000);

            // 6) 书签
            vm2.ToggleBookmarkCommand.Execute(null);
            Assert.Contains(1, vm2.Bookmarks);
            vm2.ToggleBookmarkCommand.Execute(null); // 取消
            Assert.DoesNotContain(1, vm2.Bookmarks);
            vm2.ToggleBookmarkCommand.Execute(null); // 再加回
            Assert.Contains(1, vm2.Bookmarks);

            // 7) 窗口级渲染冒烟（真实 ReaderWindow）
            var win = new ReaderWindow { DataContext = vm2 };
            win.Show();
            win.Measure(new Size(1100, 800));
            win.Arrange(new Rect(0, 0, 1100, 800));
            var bmp = new RenderTargetBitmap(new PixelSize(1100, 800));
            bmp.Render(win);
            Assert.True(bmp.PixelSize.Width > 0);
            win.Close();

            // 8) 逐页翻越 LRU 缓存上限（6 页）——缓存淘汰路径（逐步等待，避免竞态）
            var db3 = Path.Combine(Path.GetTempPath(), $"mangaagent_e2e3_{Guid.NewGuid():N}.db");
            var vm3 = new ReaderViewModel(StressBook, db3);
            for (int i = 0; i < 12; i++)
            {
                vm3.NextPageCommand.Execute(null);
                Assert.Equal(i + 1, vm3.CurrentPhysical);   // BUG-A：单页逐物理页
                await WaitUntilAsync(() => vm3.Image1 is not null);
                Assert.False(vm3.HasStatus, $"翻页到 {i + 1} 不应出现解码错误");
            }
            Assert.Equal(12, vm3.CurrentPhysical);
            vm3.PrevPageCommand.Execute(null);
            vm3.PrevPageCommand.Execute(null);
            Assert.Equal(10, vm3.CurrentPhysical);
            await vm3.DisposeAsync();
            try { File.Delete(db3); } catch { }

            await vm.DisposeAsync();
            await vm2.DisposeAsync();
        }
        finally
        {
            try { File.Delete(db); } catch { }
        }
    }
}
