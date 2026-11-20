using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.20：目录浏览器从库内 covers.json 注入文件夹封面（不依赖 reading.db）。</summary>
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
            WriteImage(Path.Combine(root, "_shared", "folder.png"));
            var cover = Path.Combine(root, "_shared", "folder.png");

            ReadingStateStore.SetLibraryRoots(new[] { root });
            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(gallery, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);

            var tree = await FolderTreeScanner.ScanAsync(root);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
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

    [AvaloniaFact]
    public async Task Db_Record_Still_Used_For_Path_Outside_Library()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_libcover2_{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"manga_outside_{Guid.NewGuid():N}");
        try
        {
            // 库外目录（不属于任何库）→ 只能落到 reading.db 回退
            var gallery = Path.Combine(outside, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            WriteImage(Path.Combine(gallery, "cover.png"));

            ReadingStateStore.SetLibraryRoots(new[] { root });
            using (var db = new ReadingStateStore(MainWindowViewModel.StateDbPath))
                Assert.True(db.SetFolderCover(gallery, "cover.png"));

            var tree = await FolderTreeScanner.ScanAsync(outside);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.ShowFolderContents(tree);

            var entry = vm.FolderEntries.Single(e => e.IsFolder && e.DisplayName == "Gallery");
            Assert.Equal(Path.Combine(gallery, "cover.png"), entry.FolderCoverPath);
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outside, true); } catch { }
        }
    }
}
