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

            // 导航：根 → B=文件夹、C/D=漫画（BUG-D v1.9：纯图片叶子 → 漫画条目）
            var vm = new MainWindowViewModel();
            vm.ShowFolderContents(tree);
            Assert.Equal(3, vm.FolderEntries.Count);
            Assert.True(vm.FolderEntries[0].IsFolder, "B 应显示为文件夹");
            Assert.False(vm.FolderEntries[1].IsFolder, "C 应显示为漫画（纯图片叶子）");
            Assert.False(vm.FolderEntries[2].IsFolder, "D 应显示为漫画");
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

    [AvaloniaFact]
    public async Task Search_FullLibrary_FoldersAndBooks_Sort_Default()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_search_{Guid.NewGuid():N}");
        try
        {
            // 根/B(有子文件夹,非漫画) 根/C(纯图片 jpg) 根/D(纯图片 gif×2 —— 扩展名修复)
            Directory.CreateDirectory(Path.Combine(root, "B", "Inner"));   // 名不含 b/c/d，避免搜索误命中
            var c = Path.Combine(root, "C"); Directory.CreateDirectory(c);
            File.WriteAllBytes(Path.Combine(c, "p1.jpg"), new byte[8]);
            var d = Path.Combine(root, "D"); Directory.CreateDirectory(d);
            File.WriteAllBytes(Path.Combine(d, "p1.gif"), new byte[8]);
            File.WriteAllBytes(Path.Combine(d, "p2.gif"), new byte[8]);

            var tree = await FolderTreeScanner.ScanAsync(root);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);

            // 全库搜索：命中 D（纯图片漫画，含 gif）
            vm.SearchText = "D";
            Assert.Single(vm.FolderEntries);
            Assert.False(vm.FolderEntries[0].IsFolder);
            Assert.Equal("D", vm.FolderEntries[0].DisplayName);
            Assert.Equal(2, vm.FolderEntries[0].Book!.PageCount);   // gif 识别 + 页数统计

            // 命中 B（文件夹）
            vm.SearchText = "B";
            Assert.Single(vm.FolderEntries);
            Assert.True(vm.FolderEntries[0].IsFolder);
            Assert.Equal("B", vm.FolderEntries[0].DisplayName);

            // 清空 → 恢复目录浏览 + 默认排序（文件夹在前，漫画按名称）
            vm.SearchText = "";
            Assert.Equal(3, vm.FolderEntries.Count);
            Assert.True(vm.FolderEntries[0].IsFolder, "排序：文件夹在前");
            Assert.All(vm.FolderEntries.Skip(1), e => Assert.False(e.IsFolder, "漫画在后"));
            Assert.Equal(new[] { "C", "D" }, vm.FolderEntries.Skip(1).Select(e => e.DisplayName).OrderBy(x => x));

            // 详情模式：行式无图
            vm.GalleryMode = GalleryMode.Detail;
            Assert.True(vm.IsDetailView);   // 详情模式切换内容区（详情 ListBox vs 卡片 ListBox）
            Assert.All(vm.FolderEntries, e => Assert.True(e.IsDetail));
            Assert.All(vm.FolderEntries, e => Assert.Equal(0.0, e.CoverWidth));
            Assert.All(vm.FolderEntries, e => Assert.Equal(560.0, e.CardWidth));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Folder_With_Subfolder_And_Images_Is_Folder_With_Stats()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_mixed_{Guid.NewGuid():N}");
        try
        {
            // 根/B：内有子文件夹 C（非漫画）+ 直接图片 → B 是文件夹，统计 1 个子文件夹 + 1 本漫画（直接图片）
            Directory.CreateDirectory(Path.Combine(root, "B", "C"));
            File.WriteAllBytes(Path.Combine(root, "B", "p1.jpg"), new byte[8]);
            File.WriteAllBytes(Path.Combine(root, "B", "p2.jpg"), new byte[8]);

            var tree = await FolderTreeScanner.ScanAsync(root);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.ShowFolderContents(tree);

            var bEntry = Assert.Single(vm.FolderEntries);
            Assert.True(bEntry.IsFolder, "有子文件夹 → 文件夹，不是漫画");
            Assert.Contains("1 个子文件夹", bEntry.Subtitle);
            // B 的直接图片漫画页数正确（2 页）
            var folderBook = bEntry.Folder!.Books.First(b => b.Kind == BookKind.Folder);
            Assert.Equal(2, folderBook.PageCount);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void NewFolder_Creates_Directory_And_Reports_Errors()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_newfolder_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "existing"));
            var vm = new MainWindowViewModel();

            // 成功创建
            var msg = vm.NewFolderCore(root, "NewDir");
            Assert.Equal("已创建：NewDir", msg);
            Assert.True(Directory.Exists(Path.Combine(root, "NewDir")));

            // 同名已存在
            var dup = vm.NewFolderCore(root, "existing");
            Assert.Contains("同名条目已存在", dup);

            // 非法文件名（Windows 保留字符）
            var bad = vm.NewFolderCore(root, "bad<name");
            Assert.Contains("创建失败", bad);
            Assert.False(Directory.Exists(Path.Combine(root, "bad<name")));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Stats_MN_Direct_X_Recursive_And_Format_Branches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"manga_stats_{Guid.NewGuid():N}");
        try
        {
            // Alpha/：Pure1、Pure2（纯图片漫画）+ SubDir/Pure3（递归 1 本）
            Directory.CreateDirectory(Path.Combine(root, "Alpha", "Pure1"));
            File.WriteAllBytes(Path.Combine(root, "Alpha", "Pure1", "a.jpg"), new byte[8]);
            Directory.CreateDirectory(Path.Combine(root, "Alpha", "Pure2"));
            File.WriteAllBytes(Path.Combine(root, "Alpha", "Pure2", "a.jpg"), new byte[8]);
            Directory.CreateDirectory(Path.Combine(root, "Alpha", "SubDir", "Pure3"));
            File.WriteAllBytes(Path.Combine(root, "Alpha", "SubDir", "Pure3", "a.jpg"), new byte[8]);
            // Empty/：只有空子文件夹（M=0 分支）
            Directory.CreateDirectory(Path.Combine(root, "Empty", "SubOnly"));

            var tree = await FolderTreeScanner.ScanAsync(root);
            var vm = new MainWindowViewModel();
            vm.FolderTree.Add(tree);
            vm.ShowFolderContents(tree);

            // Alpha：M=2（Pure1/Pure2）N=1（SubDir）→ 两行统计
            var alpha = vm.FolderEntries.First(e => e.DisplayName == "Alpha");
            Assert.True(alpha.IsFolder);
            Assert.Equal("2 本漫画 · 1 个子文件夹", alpha.Subtitle);
            Assert.Equal("目录下共 3 本漫画", alpha.StatsLine2);   // X 递归 = 2 + SubDir 内 1

            // Empty：M=0 → 只显示子文件夹数，StatsLine2 为空
            var empty = vm.FolderEntries.First(e => e.DisplayName == "Empty");
            Assert.True(empty.IsFolder);
            Assert.Equal("1 个子文件夹", empty.Subtitle);
            Assert.Equal("", empty.StatsLine2);

            // 进入 Alpha：Pure1/Pure2 = 纯图片漫画（副标题 N 页，无后缀）；SubDir = 文件夹（M=1 N=0 → 只显示漫画数）
            vm.EnterFolderCommand.Execute(alpha.Folder);
            var pure1 = vm.FolderEntries.First(e => e.DisplayName == "Pure1");
            Assert.False(pure1.IsFolder);
            Assert.Equal("1 页", pure1.Subtitle);   // 纯图片无 "· 文件夹" 后缀
            Assert.Equal("", pure1.StatsLine2);
            var subDir = vm.FolderEntries.First(e => e.DisplayName == "SubDir");
            Assert.True(subDir.IsFolder);
            Assert.Equal("1 本漫画", subDir.Subtitle);   // N=0 → 只显示漫画数
            Assert.Equal("目录下共 1 本漫画", subDir.StatsLine2);
            // 详情模式单行副标题
            Assert.Equal("1 页", pure1.SubtitleOneLine);
            Assert.Equal("1 本漫画 · 共 1 本漫画", subDir.SubtitleOneLine);   // M=1, N=0, X=1

            // 三模式封面统一 0.7 比例、等比 ×1.5（卡片/封面尺寸）
            vm.GalleryMode = GalleryMode.Large;
            Assert.All(vm.FolderEntries, e => Assert.Equal(240.0, e.CardWidth));
            Assert.All(vm.FolderEntries, e => Assert.Equal(216.0, e.CoverWidth));
            Assert.All(vm.FolderEntries, e => Assert.Equal(309.0, e.CoverHeight));
            Assert.True(Math.Abs(vm.FolderEntries[0].CoverWidth / vm.FolderEntries[0].CoverHeight - 0.7) < 0.01);
            vm.GalleryMode = GalleryMode.Medium;
            Assert.All(vm.FolderEntries, e => Assert.Equal(168.0, e.CardWidth));
            Assert.All(vm.FolderEntries, e => Assert.Equal(144.0, e.CoverWidth));
            Assert.All(vm.FolderEntries, e => Assert.Equal(206.0, e.CoverHeight));
            vm.GalleryMode = GalleryMode.Small;
            Assert.All(vm.FolderEntries, e => Assert.Equal(120.0, e.CardWidth));
            Assert.All(vm.FolderEntries, e => Assert.Equal(96.0, e.CoverWidth));
            Assert.All(vm.FolderEntries, e => Assert.Equal(137.0, e.CoverHeight));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
