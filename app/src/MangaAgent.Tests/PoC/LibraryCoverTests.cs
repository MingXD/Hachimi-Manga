using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.20：目录浏览器从库内 covers.json 注入文件夹封面（唯一存储；不再有 reading.db 回退）。</summary>
public class LibraryCoverTests
{
    private static void WriteImage(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[8]);
    }

    [AvaloniaFact]
    public async Task Folder_Contents_Inject_Cover_From_Library_File()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_libcover_{Guid.NewGuid():N}");
        try
        {
            // 库：Gallery（有子文件夹 → 文件夹条目）、_shared/folder.png（库内共用的封面图）
            var gallery = Path.Combine(root, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            var cover = Path.Combine(root, "_shared", "folder.png");
            WriteImage(cover);

            ReadingStateStore.SetLibraryRoots(new[] { root });
            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(gallery, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);

            var tree = await FolderTreeScanner.ScanAsync(root);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.Libraries.Add(new LibraryItemViewModel(root, new List<BookInfo>()));
            vm.ShowFolderContents(tree);

            var entry = vm.FolderEntries.Single(e => e.IsFolder && e.DisplayName == "Gallery");
            Assert.Equal(cover, entry.FolderCoverPath);
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    /// <summary>嵌套库根（bz 与 bz\25.08 同时是库根）：读"最具体"库根没有时，回退到父库根的文件。</summary>
    [AvaloniaFact]
    public async Task Nested_Library_Falls_Back_To_Outer_Covers_File()
    {
        var bz = Path.Combine(Path.GetTempPath(), $"manga_nested_{Guid.NewGuid():N}");
        try
        {
            var inner = Path.Combine(bz, "25.08");
            var gallery = Path.Combine(inner, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            var cover = Path.Combine(bz, "_shared", "c.png");
            WriteImage(cover);

            ReadingStateStore.SetLibraryRoots(new[] { inner, bz });   // 内外都算库根
            // 键只写在**外层**库根（bz）的 covers.json 里
            var outer = FolderCoverConfig.Load(bz);
            Assert.True(outer.TrySet(gallery, cover, out var err), err);
            Assert.True(outer.Flush(), outer.LastError);
            // 内层库根文件此时不存在该键
            Assert.Null(FolderCoverConfig.Load(inner).GetCoverFullPath(gallery));

            var tree = await FolderTreeScanner.ScanAsync(bz);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.Libraries.Add(new LibraryItemViewModel(inner, new List<BookInfo>()));
            vm.Libraries.Add(new LibraryItemViewModel(bz, new List<BookInfo>()));
            vm.ShowFolderContents(tree.Children.Single(c => c.Name == "25.08"));

            var entry = vm.FolderEntries.Single(e => e.IsFolder && e.DisplayName == "Gallery");
            Assert.Equal(cover, entry.FolderCoverPath);   // 回退到 bz\covers.json 生效
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(bz, true); } catch { }
        }
    }

    /// <summary>清除封面要清掉所有候选库根里的同键（否则父库根残留会让封面"清不掉"）。</summary>
    [AvaloniaFact]
    public async Task Clear_Removes_Key_From_All_Candidate_Roots()
    {
        var bz = Path.Combine(Path.GetTempPath(), $"manga_clear_{Guid.NewGuid():N}");
        try
        {
            var inner = Path.Combine(bz, "25.08");
            var gallery = Path.Combine(inner, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            var outerCover = Path.Combine(bz, "_shared", "c.png");
            WriteImage(outerCover);
            var innerCover = Path.Combine(inner, "_shared", "c.png");   // 内层库根也要有库内封面图
            WriteImage(innerCover);

            ReadingStateStore.SetLibraryRoots(new[] { inner, bz });
            var outerCfg = FolderCoverConfig.Load(bz);
            Assert.True(outerCfg.TrySet(gallery, outerCover, out _));
            Assert.True(outerCfg.Flush());
            var innerCfg = FolderCoverConfig.Load(inner);
            Assert.True(innerCfg.TrySet(gallery, innerCover, out _));
            Assert.True(innerCfg.Flush());

            var tree = await FolderTreeScanner.ScanAsync(bz);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.Libraries.Add(new LibraryItemViewModel(inner, new List<BookInfo>()));
            vm.Libraries.Add(new LibraryItemViewModel(bz, new List<BookInfo>()));
            vm.ShowFolderContents(tree.Children.Single(c => c.Name == "25.08"));
            var entry = vm.FolderEntries.Single(e => e.IsFolder && e.DisplayName == "Gallery");
            Assert.Equal(innerCover, entry.FolderCoverPath);   // 最具体库根优先

            vm.ClearFolderCover(entry);

            Assert.Null(entry.FolderCoverPath);
            Assert.Null(FolderCoverConfig.Load(inner).GetCoverFullPath(gallery));   // 内层库根也清掉
            Assert.Null(FolderCoverConfig.Load(bz).GetCoverFullPath(gallery));      // 外层库根也清掉
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(bz, true); } catch { }
        }
    }
}
