using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.23：双页模式下的书签高亮与切换（以"当前这一屏"为单位）。
/// 回归点：①翻页后指示刷新（此前只刷新物理页 setter，双页翻页时拿到旧逻辑页）；
/// ②屏内任一页有书签即高亮；③在含书签的屏再点 → 清掉本屏书签。</summary>
public class ReaderBookmarkDoublePageTests
{
    private static async Task<bool> WaitUntilAsync(Func<bool> cond, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            if (cond()) return true;
            await Task.Delay(50);
        }
        Dispatcher.UIThread.RunJobs();
        return cond();
    }

    private static string MakeFolderBook(string tag, int pages)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"manga_dp_{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        for (int i = 1; i <= pages; i++) File.WriteAllBytes(Path.Combine(dir, $"{i:000}.jpg"), new byte[8]);
        return dir;
    }

    private static async Task<ReaderViewModel> OpenDoublePageAsync(string dir, int pages, string db)
    {
        var book = new BookInfo(dir, "书", null, null, BookKind.Folder, DateTime.UtcNow, pages);
        var vm = new ReaderViewModel(book, new LibraryStateService(db));
        Assert.True(await WaitUntilAsync(() => vm.PhysicalPageCount == pages));
        vm.ViewMode = ViewMode.Double;
        Assert.True(await WaitUntilAsync(() => vm.ViewMode == ViewMode.Double));
        return vm;
    }

    [AvaloniaFact]
    public async Task Double_Page_Bookmark_Highlight_Follows_Screen()
    {
        var dir = MakeFolderBook("hl", 8);
        var db = Path.Combine(Path.GetTempPath(), $"manga_dp_{Guid.NewGuid():N}.db");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            LibraryStateService.FlushIntervalMs = 0;
            var vm = await OpenDoublePageAsync(dir, 8, db);
            try
            {
                // 进入第二屏（第 1 屏通常是封面单页）
                vm.NextPageCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var firstScreen = vm.CurrentVisiblePhysicalPages().ToList();
                Assert.True(firstScreen.Count >= 1);

                // 本屏加书签 → 高亮
                vm.ToggleBookmarkCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.IsCurrentPageBookmarked);
                Assert.True(vm.Bookmarks.Count >= 1);

                // 翻到下一屏 → 不应高亮（书签不在本屏）
                vm.NextPageCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var secondScreen = vm.CurrentVisiblePhysicalPages();
                Assert.False(vm.IsCurrentPageBookmarked, $"下一屏 [{string.Join(",", secondScreen.Select(p => p + 1))}] 不应高亮");

                // 翻回 → 重新高亮（验证刷新时机 bug 已修）
                vm.PrevPageCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.IsCurrentPageBookmarked, "翻回原屏应重新高亮");
            }
            finally { await vm.DisposeAsync(); }
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            try { Directory.Delete(dir, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Double_Page_Toggle_On_Bookmarked_Screen_Clears_Screen_Bookmarks()
    {
        var dir = MakeFolderBook("clr", 8);
        var db = Path.Combine(Path.GetTempPath(), $"manga_dp_{Guid.NewGuid():N}.db");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            LibraryStateService.FlushIntervalMs = 0;
            var vm = await OpenDoublePageAsync(dir, 8, db);
            try
            {
                vm.NextPageCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                vm.ToggleBookmarkCommand.Execute(null);   // 本屏加书签
                Dispatcher.UIThread.RunJobs();
                Assert.True(vm.IsCurrentPageBookmarked);
                Assert.True(vm.Bookmarks.Count >= 1);

                // 再点一次 → 清掉本屏书签，按钮复原
                vm.ToggleBookmarkCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.False(vm.IsCurrentPageBookmarked);
                Assert.Empty(vm.Bookmarks);
            }
            finally { await vm.DisposeAsync(); }
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            try { Directory.Delete(dir, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }
}
