using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.20：资料库"重新关联"（库被改名/移动后，把库条目指到新路径，无需清空重建）。
/// 覆盖：失效库加载时保留并标记、重新关联后路径/文件/库根同步、封面随目录自动恢复。</summary>
public class LibraryRelinkTests
{
    private static void WriteImage(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[8]);
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
    public async Task Missing_Library_Is_Kept_Invalid_And_Can_Be_Relinked()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_relink_{Guid.NewGuid():N}");
        var newRoot = root + "-moved";
        try
        {
            // 造库：Gallery 文件夹条目 + 自定义封面
            var gallery = Path.Combine(root, "Gallery");
            Directory.CreateDirectory(Path.Combine(gallery, "Inner"));
            var cover = Path.Combine(root, "_shared", "c.png");
            WriteImage(cover);
            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(gallery, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);

            // 模拟"库里记的是旧路径，磁盘上已改名"
            Directory.Move(root, newRoot);
            await File.WriteAllLinesAsync(MainWindowViewModel.LibrariesFile, new[] { root });

            var vm = new MainWindowViewModel();
            Assert.True(await WaitUntilAsync(() => vm.Libraries.Count == 1), "启动应加载出 1 个库");

            var lib = vm.Libraries[0];
            Assert.False(lib.IsValid);                                   // 失效但保留在列表里
            Assert.Contains("路径失效", lib.DisplayName);
            Assert.True(vm.SelectedLibrary is null || vm.SelectedLibrary.IsValid, "失效库不应被自动选中");

            // 重新关联到新路径
            Assert.True(await vm.RelinkLibraryAsync(lib, newRoot));
            Assert.Equal(newRoot, lib.Path);
            Assert.True(lib.IsValid);

            var lines = await File.ReadAllLinesAsync(MainWindowViewModel.LibrariesFile);
            Assert.Contains(newRoot, lines);
            Assert.DoesNotContain(root, lines);
            Assert.Equal(new[] { newRoot }, ReadingStateStore.LibraryRoots, StringComparer.OrdinalIgnoreCase);

            // 封面随目录自动恢复
            Assert.True(await WaitUntilAsync(() => vm.FolderEntries.Any(e => e.IsFolder && e.DisplayName == "Gallery")));
            var entry = vm.FolderEntries.Single(e => e.IsFolder && e.DisplayName == "Gallery");
            Assert.Equal(Path.Combine(newRoot, "_shared", "c.png"), entry.FolderCoverPath);
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { File.Delete(MainWindowViewModel.LibrariesFile); } catch { }
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(newRoot, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Relink_Rejects_Missing_Or_Duplicate_Target()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_relink2_{Guid.NewGuid():N}");
        var other = Path.Combine(Path.GetTempPath(), $"manga_relink2b_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(other);

            var vm = new MainWindowViewModel();
            var lib = new LibraryItemViewModel(root, new List<BookInfo>());
            var dup = new LibraryItemViewModel(other, new List<BookInfo>());
            vm.Libraries.Add(lib);
            vm.Libraries.Add(dup);

            // 目标不存在 → 拒绝
            Assert.False(await vm.RelinkLibraryAsync(lib, Path.Combine(root, "不存在")));
            Assert.Contains("不存在", vm.StatusText);

            // 目标是另一个已存在的库 → 拒绝
            Assert.False(await vm.RelinkLibraryAsync(lib, other));
            Assert.Contains("已在资料库列表", vm.StatusText);
            Assert.Equal(root, lib.Path);
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { File.Delete(MainWindowViewModel.LibrariesFile); } catch { }
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(other, true); } catch { }
        }
    }
}
