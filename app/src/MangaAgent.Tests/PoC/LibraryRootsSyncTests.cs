using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.20 bug 回归：库根列表与库列表不同步时（如"清空库后第一次添加库"），
/// 渲染前必须先同步库根，否则新加的库一个封面都读不到。</summary>
public class LibraryRootsSyncTests
{
    [AvaloniaFact]
    public async Task Render_Syncs_LibraryRoots_Before_Reading_Covers()
    {
        var lib = Path.Combine(Path.GetTempPath(), $"manga_rootsync_{Guid.NewGuid():N}");
        var staleRoot = Path.Combine(Path.GetTempPath(), $"manga_stale_{Guid.NewGuid():N}");
        try
        {
            var gallery = Path.Combine(lib, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            var cover = Path.Combine(lib, "_shared", "c.png");
            Directory.CreateDirectory(Path.GetDirectoryName(cover)!);
            File.WriteAllBytes(cover, new byte[8]);

            // 封面记录写在新库里
            var cfg = FolderCoverConfig.Load(lib);
            Assert.True(cfg.TrySet(gallery, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);

            // 关键：内存库根是**旧值**（模拟清空库后第一次添加：ClearLibraries 之前没重置 / 添加顺序不对）
            Directory.CreateDirectory(staleRoot);
            ReadingStateStore.SetLibraryRoots(new[] { staleRoot });

            var tree = await FolderTreeScanner.ScanAsync(lib);
            var vm = new MainWindowViewModel();
            vm.Libraries.Add(new LibraryItemViewModel(lib, new List<BookInfo>()));

            // 渲染时必须自动把库根同步成 [lib]
            vm.ShowFolderContents(tree);

            Assert.Equal(new[] { lib }, ReadingStateStore.LibraryRoots, StringComparer.OrdinalIgnoreCase);
            var entry = vm.FolderEntries.Single(e => e.IsFolder && e.DisplayName == "Gallery");
            Assert.Equal(cover, entry.FolderCoverPath);
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(lib, true); } catch { }
            try { Directory.Delete(staleRoot, true); } catch { }
        }
    }

    /// <summary>清空库后库根列表应被重置（否则下次添加库会沿用旧库根）。</summary>
    [AvaloniaFact]
    public async Task Clearing_Libraries_Resets_Roots_For_Next_Add()
    {
        var lib = Path.Combine(Path.GetTempPath(), $"manga_rootsync2_{Guid.NewGuid():N}");
        try
        {
            var gallery = Path.Combine(lib, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            var cover = Path.Combine(lib, "c.png");
            File.WriteAllBytes(cover, new byte[8]);
            var cfg = FolderCoverConfig.Load(lib);
            Assert.True(cfg.TrySet(gallery, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);

            var vm = new MainWindowViewModel();
            // 直接模拟"清空库"后的状态：没有任何库，库根也被清空
            ReadingStateStore.SetLibraryRoots(Array.Empty<string>());
            vm.Libraries.Clear();

            // 重新添加库（只动库列表，不经过文件选择器）
            vm.Libraries.Add(new LibraryItemViewModel(lib, new List<BookInfo>()));
            var tree = await FolderTreeScanner.ScanAsync(lib);
            vm.ShowFolderContents(tree);

            Assert.Equal(new[] { lib }, ReadingStateStore.LibraryRoots, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(cover, vm.FolderEntries.Single(e => e.IsFolder && e.DisplayName == "Gallery").FolderCoverPath);
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(lib, true); } catch { }
        }
    }
}
