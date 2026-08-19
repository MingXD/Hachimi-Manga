using Avalonia.Headless.XUnit;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>目录浏览器混合展示回归（2026-08-20）：
/// A 下 B/C/D —— B 有子文件夹（非漫画）、C/D 是漫画（文件夹下直接图片）。
/// 右侧应混合展示：B 为文件夹样式、C/D 为漫画样式；双击 B 进入下一层。</summary>
public class FolderBrowserTests
{
    [AvaloniaFact]
    public async Task Mixed_Entries_Show_Folders_And_Books()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_browser_{Guid.NewGuid():N}");
        try
        {
            // A/B/Sub1/Sub2 (B 只有子文件夹，非漫画)
            var b1 = Path.Combine(root, "B", "Sub1");
            var b2 = Path.Combine(root, "B", "Sub2");
            Directory.CreateDirectory(b1);
            Directory.CreateDirectory(b2);
            // A/C (漫画：直接含图片)
            var c = Path.Combine(root, "C");
            Directory.CreateDirectory(c);
            File.WriteAllBytes(Path.Combine(c, "p1.jpg"), new byte[8]);
            File.WriteAllBytes(Path.Combine(c, "p2.jpg"), new byte[8]);
            // A/D (漫画)
            var d = Path.Combine(root, "D");
            Directory.CreateDirectory(d);
            File.WriteAllBytes(Path.Combine(d, "p1.png"), new byte[8]);

            // 扫描树
            var tree = await FolderTreeScanner.ScanAsync(root);
            // 根：3 个子文件夹（B/C/D），无直接书
            Assert.Equal(3, tree.Children.Count);
            Assert.Empty(tree.Books);

            var b = tree.Children[0];
            Assert.Equal("B", b.Name);
            Assert.Equal(2, b.Children.Count);   // Sub1/Sub2
            Assert.Empty(b.Books);               // 非漫画
            Assert.NotNull(b.Parent);            // Parent 已接线

            var cNode = tree.Children[1];
            Assert.Equal("C", cNode.Name);
            Assert.Single(cNode.Books);          // 1 本文件夹型漫画
            Assert.Equal(BookKind.Folder, cNode.Books[0].Kind);

            // 导航：根 → 展示混合条目（3 文件夹 + 0 书）；进入 B → 2 子文件夹
            var vm = new MainWindowViewModel();
            vm.ShowFolderContents(tree);
            Assert.Equal(3, vm.FolderEntries.Count);
            Assert.All(vm.FolderEntries, e => Assert.True(e.IsFolder));
            Assert.Single(vm.Breadcrumbs);   // 根节点自身 = 1 层

            vm.EnterFolderCommand.Execute(b);
            Assert.Equal(2, vm.FolderEntries.Count);
            Assert.All(vm.FolderEntries, e => Assert.True(e.IsFolder));
            Assert.Equal(2, vm.Breadcrumbs.Count);   // 根 + B

            // 返回上级
            vm.GoUpCommand.Execute(null);
            Assert.Equal(3, vm.FolderEntries.Count);
            Assert.Single(vm.Breadcrumbs);

            // 进入 C：1 本漫画（封面样式）
            vm.EnterFolderCommand.Execute(cNode);
            Assert.Single(vm.FolderEntries);
            Assert.False(vm.FolderEntries[0].IsFolder);
            Assert.Equal("C", vm.FolderEntries[0].DisplayName);

        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
