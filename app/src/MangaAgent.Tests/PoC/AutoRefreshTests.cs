using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MangaAgent.App.Services;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.19 自动更新机制回归：
/// ①幽灵书修复（分类目录的散图不自成一本，单行本文件夹仍是一本）；
/// ②浅刷新（原地增量，不打开压缩包，节点身份保留）；
/// ③进入目录/窗口激活时按文件系统刷新；
/// ④页数后台懒补；
/// ⑤当前目录 watcher（P3）。</summary>
public class AutoRefreshTests
{
    private static string NewTempRoot(string tag)
    {
        var p = Path.Combine(Path.GetTempPath(), $"manga_{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>写一个含 n 张图片条目的最小 zip（ZipComicSource 按图片条目数计页）。</summary>
    private static void WriteZip(string path, int images = 3)
    {
        using var fs = File.Create(path);
        using var za = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create);
        for (int i = 1; i <= images; i++)
        {
            var entry = za.CreateEntry($"{i:000}.jpg");
            using var es = entry.Open();
            es.Write(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }, 0, 4);
        }
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

    // ---------- ① 幽灵书 ----------
    [AvaloniaFact]
    public async Task Ghost_Book_Only_For_SingleVolume_Folder()
    {
        var root = NewTempRoot("ghost");
        try
        {
            // 分类目录：有子文件夹 + 散图 + 压缩包（旧实现会多出一本"zhongzi"）
            var zhongzi = Path.Combine(root, "zhongzi");
            Directory.CreateDirectory(Path.Combine(zhongzi, "NTR"));
            File.WriteAllBytes(Path.Combine(zhongzi, "001.jpg"), new byte[8]);
            File.WriteAllBytes(Path.Combine(zhongzi, "002.jpg"), new byte[8]);
            WriteZip(Path.Combine(zhongzi, "loose.zip"));
            // 单行本文件夹：只有直接图片 → 仍应是一本书
            var single = Path.Combine(root, "Single");
            Directory.CreateDirectory(single);
            File.WriteAllBytes(Path.Combine(single, "p1.jpg"), new byte[8]);
            File.WriteAllBytes(Path.Combine(single, "p2.jpg"), new byte[8]);

            var tree = await FolderTreeScanner.ScanAsync(root);
            var zNode = tree.Children.Single(c => c.Name == "zhongzi");
            var sNode = tree.Children.Single(c => c.Name == "Single");

            Assert.DoesNotContain(zNode.Books, b => b.Kind == BookKind.Folder);   // 幽灵书已消失
            Assert.False(zNode.IsSingleVolumeFolder);
            Assert.Contains(sNode.Books, b => b.Kind == BookKind.Folder);         // 单行本保留
            Assert.True(sNode.IsSingleVolumeFolder);

            var vm = new MainWindowViewModel();
            vm.ShowFolderContents(tree);   // 根视图
            Assert.Contains(vm.FolderEntries, e => e.IsFolder && e.DisplayName == "zhongzi");
            Assert.Contains(vm.FolderEntries, e => !e.IsFolder && e.DisplayName == "Single");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---------- ② 浅刷新 ----------
    [AvaloniaFact]
    public async Task ShallowRefresh_Picks_Up_New_Items_And_Keeps_Identity()
    {
        var root = NewTempRoot("shallow");
        try
        {
            var sub = Path.Combine(root, "Sub");
            Directory.CreateDirectory(sub);
            WriteZip(Path.Combine(root, "a.zip"));

            var tree = await FolderTreeScanner.ScanAsync(root);
            var subNode = tree.Children.Single();
            Assert.Single(tree.Books);

            // 外部新增：一本新压缩包 + 一个新文件夹
            WriteZip(Path.Combine(root, "b.zip"));
            Directory.CreateDirectory(Path.Combine(root, "NewDir"));

            await FolderTreeScanner.RefreshShallowAsync(tree);

            Assert.Equal(2, tree.Books.Count);
            Assert.Contains(tree.Children, c => c.Name == "NewDir");
            // 身份保留（同一个 Sub 节点对象，Parent 仍指向根）
            Assert.Same(subNode, tree.Children.Single(c => c.Name == "Sub"));
            Assert.Same(tree, subNode.Parent);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---------- ③ 进入目录即刷新 ----------
    [AvaloniaFact]
    public async Task EnterFolder_Refreshes_From_Disk()
    {
        var root = NewTempRoot("enter");
        try
        {
            var sub = Path.Combine(root, "Sub");
            Directory.CreateDirectory(sub);
            WriteZip(Path.Combine(sub, "a.zip"));

            var tree = await FolderTreeScanner.ScanAsync(root);
            var vm = new MainWindowViewModel();
            vm.ShowFolderContents(tree);
            vm.EnterFolderCommand.Execute(tree.Children.Single());
            await WaitUntilAsync(() => vm.CurrentFolder?.Name == "Sub");
            Assert.Single(vm.FolderEntries);

            // 外部新增一本 → 重新进入该目录应看到
            WriteZip(Path.Combine(sub, "b.zip"));
            vm.GoUpCommand.Execute(null);
            await WaitUntilAsync(() => vm.CurrentFolder?.Name == tree.Name);
            vm.EnterFolderCommand.Execute(tree.Children.Single(c => c.Name == "Sub"));
            Assert.True(await WaitUntilAsync(() => vm.FolderEntries.Count == 2),
                $"进入目录后应看到外部新增的漫画，实际 {vm.FolderEntries.Count} 项");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---------- ③ 窗口激活（mtime 变化） ----------
    [AvaloniaFact]
    public async Task RefreshIfStale_Detects_External_Add()
    {
        var root = NewTempRoot("stale");
        try
        {
            WriteZip(Path.Combine(root, "a.zip"));
            var tree = await FolderTreeScanner.ScanAsync(root);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.ShowFolderContents(tree);
            Assert.Single(vm.FolderEntries);

            // 无变化：不应重渲染（条目对象保持同一个实例）
            var before = vm.FolderEntries[0];
            await vm.RefreshIfStaleAsync();
            Assert.Same(before, vm.FolderEntries[0]);

            // 外部新增 → 激活时浅刷新并出现
            WriteZip(Path.Combine(root, "b.zip"));
            await vm.RefreshIfStaleAsync();
            Assert.Equal(2, vm.FolderEntries.Count);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---------- ④ 页数懒补 ----------
    [AvaloniaFact]
    public async Task PageCounts_Are_Lazily_Enriched()
    {
        var root = NewTempRoot("pages");
        try
        {
            WriteZip(Path.Combine(root, "a.zip"), images: 7);
            var tree = await FolderTreeScanner.ScanAsync(root);
            Assert.Equal(0, tree.Books[0].PageCount);   // 浅扫描不打开压缩包

            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.ShowFolderContents(tree);                // 渲染即启动后台补页数
            var item = vm.FolderEntries.Single();
            Assert.Equal(0, item.ResolvedPageCount);
            Assert.Contains("…", item.Subtitle);

            Assert.True(await WaitUntilAsync(() => item.ResolvedPageCount == 7),
                $"页数应懒补为 7，实际 {item.ResolvedPageCount}");
            Assert.Contains("7 页", item.Subtitle);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---------- ⑤ 当前目录 watcher ----------
    [AvaloniaFact]
    public async Task FolderWatcher_Fires_On_Change_And_Retargets()
    {
        var root = NewTempRoot("watch");
        try
        {
            int calls = 0;
            using var watcher = new FolderWatcher { DebounceMs = 0 };
            watcher.Watch(root, () => calls++);
            Assert.Equal(root, watcher.WatchingPath);

            WriteZip(Path.Combine(root, "new.zip"));
            Assert.True(await WaitUntilAsync(() => calls > 0), "新建文件应触发 watcher 回调");

            // 切到另一个存在的目录 → 旧目录变化不再回调
            var other = Path.Combine(root, "Other");
            Directory.CreateDirectory(other);
            watcher.Watch(other, () => calls++);
            var before = calls;
            WriteZip(Path.Combine(root, "ignored.zip"));
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, calls);

            // 目标目录变化仍回调
            WriteZip(Path.Combine(other, "hit.zip"));
            Assert.True(await WaitUntilAsync(() => calls > before), "新目标目录变化应回调");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
