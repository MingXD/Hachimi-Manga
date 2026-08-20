using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>BUG-1 回归（2026-08-20）：重新扫描后必须重建文件夹树并选中根（此前左侧空白）。</summary>
public class RescanBuildsTreeTests
{
    [AvaloniaFact]
    public async Task Rescan_Rebuilds_FolderTree()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_rescan_{Guid.NewGuid():N}");
        try
        {
            var c = Path.Combine(root, "C");
            Directory.CreateDirectory(c);
            File.WriteAllBytes(Path.Combine(c, "p1.jpg"), new byte[8]);

            var vm = new MainWindowViewModel();
            // 模拟添加库（直接注入库）
            var lib = new LibraryItemViewModel(root, await LibraryScanner.ScanAsync(root));
            vm.Libraries.Add(lib);
            vm.SelectedLibrary = lib;
            // 首次建树
            var tree = await FolderTreeScanner.ScanAsync(root);
            vm.FolderTree.Add(tree);
            vm.SelectedFolder = tree;

            // 重新扫描：树必须重建且非空（BUG-1）
            vm.RescanCommand.Execute(null);
            await Task.Delay(800);
            Assert.True(vm.FolderTree.Count > 0, "Rescan 后文件夹树不应为空");
            Assert.True(vm.FolderEntries.Count > 0, "Rescan 后内容区应显示条目");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
