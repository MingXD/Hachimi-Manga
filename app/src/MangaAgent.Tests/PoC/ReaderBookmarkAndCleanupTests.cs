using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.21：书签重新上架（面板/命令）、删除时清理库配置键、封面配置健康检查。</summary>
public class ReaderBookmarkAndCleanupTests
{
    private static string NewRoot(string tag)
    {
        var p = Path.Combine(Path.GetTempPath(), $"manga_bm_{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        return p;
    }

    private static string MakeFolderBook(string dir, int pages)
    {
        Directory.CreateDirectory(dir);
        for (int i = 1; i <= pages; i++)
            File.WriteAllBytes(Path.Combine(dir, $"{i:000}.jpg"), new byte[8]);
        return dir;
    }

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

    [AvaloniaFact]
    public async Task Bookmark_Toggle_Persists_And_Shows_In_Panel()
    {
        var root = NewRoot("bmk");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            LibraryStateService.FlushIntervalMs = 0;   // 测试：立即落盘
            ReadingStateStore.SetLibraryRoots(new[] { root });
            var dir = MakeFolderBook(Path.Combine(root, "书"), 6);
            var book = new BookInfo(dir, "书", null, null, BookKind.Folder, DateTime.UtcNow, 6);

            var svc = new LibraryStateService(MainWindowViewModel.StateDbPath);
            var vm = new ReaderViewModel(book, svc);
            try
            {
                Assert.True(await WaitUntilAsync(() => vm.PhysicalPageCount == 6));
                vm.NextPageCommand.Execute(null);
                vm.NextPageCommand.Execute(null);         // 第 3 页
                vm.ToggleBookmarkCommand.Execute(null);   // 打书签
                Assert.Single(vm.Bookmarks);
                Assert.Single(vm.BookmarkItems);
                Assert.Equal("第 3 页", vm.BookmarkItems[0].Label);
                vm.ToggleBookmarkPanelCommand.Execute(null);
                Assert.True(vm.IsBookmarkPanelOpen);
            }
            finally { await vm.DisposeAsync(); }

            // 库内 progress.json 里应有书签
            var cfg = LibraryProgressConfig.Load(root);
            Assert.Contains(cfg.StateKeys, k => k == "书");

            // 重开同一本书：书签应还在（跨进程/跨会话持久化）
            var vm2 = new ReaderViewModel(book, svc);
            try
            {
                Assert.True(await WaitUntilAsync(() => vm2.PhysicalPageCount == 6));
                Assert.True(await WaitUntilAsync(() => vm2.Bookmarks.Count == 1), "重开应恢复书签");
            }
            finally { await vm2.DisposeAsync(); }

            // 面板删除书签
            var vm3 = new ReaderViewModel(book, svc);
            try
            {
                Assert.True(await WaitUntilAsync(() => vm3.BookmarkItems.Count == 1));
                vm3.RemoveBookmarkAt(vm3.BookmarkItems[0].Page);
                Assert.Empty(vm3.Bookmarks);
            }
            finally { await vm3.DisposeAsync(); }
            svc.Dispose();
            var diskPath = Path.Combine(root, "progress.json");
            var diskJson = File.Exists(diskPath) ? File.ReadAllText(diskPath) : "(无文件)";
            var reread = LibraryProgressConfig.Load(root);
            Assert.True(reread.GetBookmarks("书").Count == 0, "磁盘仍有书签：" + diskJson + " | LastError=" + reread.LastError);
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Delete_Removes_Cover_And_Progress_Keys()
    {
        var root = NewRoot("del");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            LibraryStateService.FlushIntervalMs = 0;
            ReadingStateStore.SetLibraryRoots(new[] { root });
            var gallery = Path.Combine(root, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            var cover = Path.Combine(root, "c.png");
            File.WriteAllBytes(cover, new byte[8]);
            var book = Path.Combine(gallery, "Inner", "b.zip");
            File.WriteAllBytes(book, new byte[8]);

            // 封面 + 进度记录
            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(gallery, cover, out var err), err);
            Assert.True(cfg.Flush());
            var svc = new LibraryStateService(MainWindowViewModel.StateDbPath);
            svc.SaveState(book, 7, ReadingMode.Single, ReadingDirection2.Rtl);
            svc.Flush();

            var vm = new MainWindowViewModel();
            vm.Libraries.Add(new LibraryItemViewModel(root, new List<BookInfo>()));
            vm.DeletePaths(new[] { gallery });   // 删除到回收站

            Assert.Null(FolderCoverConfig.Load(root).GetCoverFullPath(gallery));
            Assert.DoesNotContain(LibraryProgressConfig.Load(root).StateKeys, k => k.StartsWith("Gallery"));
            svc.Dispose();
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void Cover_Inspect_Reports_And_Cleans_Invalid_Entries()
    {
        var root = NewRoot("inspect");
        try
        {
            var a = Path.Combine(root, "A");
            Directory.CreateDirectory(a);
            var imgA = Path.Combine(a, "c.png");
            File.WriteAllBytes(imgA, new byte[8]);
            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(a, imgA, out var err), err);
            Assert.True(cfg.Flush());

            // 造无效：一个指向已删文件夹、一个图片被删
            var b = Path.Combine(root, "B");
            Directory.CreateDirectory(b);
            var imgB = Path.Combine(b, "c.png");
            File.WriteAllBytes(imgB, new byte[8]);
            Assert.True(cfg.TrySet(b, imgB, out _));
            Assert.True(cfg.Flush());
            Directory.Delete(b, true);       // 文件夹没了

            var report = FolderCoverConfig.Load(root).Inspect();
            Assert.Equal(1, report.MissingFolder);
            Assert.Equal(0, report.MissingImage);

            var cleaner = FolderCoverConfig.Load(root);
            Assert.Equal(1, cleaner.RemoveInvalid());
            Assert.True(cleaner.Flush());
            Assert.Null(FolderCoverConfig.Load(root).GetCoverFullPath(b));
            Assert.NotNull(FolderCoverConfig.Load(root).GetCoverFullPath(a));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
