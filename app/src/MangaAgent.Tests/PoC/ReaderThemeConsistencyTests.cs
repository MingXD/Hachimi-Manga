using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.23：阅读器白天/夜间主题一致性回归。
/// 覆盖：书签按钮选中态（Classes.on + 专属配色）、面板按钮统一 panelFlat（不再用默认模板变白）、
/// 预览栏/工具栏色板随主题切换（白天 #0057B8 等）。</summary>
public class ReaderThemeConsistencyTests
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
        var dir = Path.Combine(Path.GetTempPath(), $"manga_theme_{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        for (int i = 1; i <= pages; i++) File.WriteAllBytes(Path.Combine(dir, $"{i:000}.jpg"), new byte[8]);
        return dir;
    }

    [AvaloniaFact]
    public async Task Bookmark_Button_Highlight_And_Panel_Flat_Buttons()
    {
        var dir = MakeFolderBook("bm", 6);
        var db = Path.Combine(Path.GetTempPath(), $"manga_theme_{Guid.NewGuid():N}.db");
        try
        {
            var book = new BookInfo(dir, "书", null, null, BookKind.Folder, DateTime.UtcNow, 6);
            var vm = new ReaderViewModel(book, new LibraryStateService(db));
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            try
            {
                Assert.True(await WaitUntilAsync(() => vm.PhysicalPageCount == 6));

                var btn = win.FindControl<Button>("BookmarkToggleButton");
                Assert.NotNull(btn);
                Assert.False(btn!.Classes.Contains("on"));           // 未加书签
                Assert.Equal("书签", vm.BookmarkButtonText);

                vm.ToggleBookmarkCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Contains("on", btn.Classes);                  // 选中态类生效（不再只是文案）
                Assert.Equal("★ 书签", vm.BookmarkButtonText);
                Assert.True(vm.IsCurrentPageBookmarked);

                // 取消 → 状态复原
                vm.ToggleBookmarkCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain("on", btn.Classes);
                Assert.Equal("书签", vm.BookmarkButtonText);
            }
            finally
            {
                win.Close();
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task Panel_Buttons_Use_PanelFlat_Style()
    {
        var dir = MakeFolderBook("panel", 4);
        var db = Path.Combine(Path.GetTempPath(), $"manga_theme_{Guid.NewGuid():N}.db");
        try
        {
            var book = new BookInfo(dir, "书", null, null, BookKind.Folder, DateTime.UtcNow, 4);
            var vm = new ReaderViewModel(book, new LibraryStateService(db));
            var win = new ReaderWindow { DataContext = vm };
            win.Show();
            try
            {
                Assert.True(await WaitUntilAsync(() => vm.PhysicalPageCount == 4));
                vm.ToggleBookmarkCommand.Execute(null);
                vm.ToggleBookmarkPanelCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();

                var panel = win.FindControl<Border>("BookmarkPanel");
                Assert.NotNull(panel);
                var buttons = panel!.GetVisualDescendants().OfType<Button>().ToList();
                Assert.NotEmpty(buttons);
                // 面板里所有按钮都必须走 panelFlat（否则白天 hover 用默认模板会变白）
                Assert.All(buttons, b => Assert.Contains("panelFlat", b.Classes));
            }
            finally { win.Close(); }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [AvaloniaFact]
    public void Theme_Palette_Switches_Between_Dark_And_Light()
    {
        var dir = MakeFolderBook("palette", 2);
        try
        {
            var book = new BookInfo(dir, "书", null, null, BookKind.Folder, DateTime.UtcNow, 2);
            var vm = new ReaderViewModel(book, "_unused.db");
            try
            {
                // 夜间
                Assert.True(vm.IsDark);
                Assert.Equal("#FFD700", vm.PreviewStripHighlight);
                Assert.Equal("#FFD700", vm.PreviewStripHighlightText);
                Assert.Equal("#33FFFFFF", vm.PreviewStripPanelHoverBg);
                Assert.Equal("#555", vm.PreviewStripCardBorder);
                Assert.Equal("#666666", vm.ToolbarSeparator);
                Assert.Equal("#4A3B0F", vm.BookmarkActiveBg);

                // 白天
                vm.IsDark = false;
                Assert.Equal("#0057B8", vm.PreviewStripHighlight);
                Assert.Equal("#004C99", vm.PreviewStripHighlightText);
                Assert.Equal("#22000000", vm.PreviewStripPanelHoverBg);
                Assert.Equal("#A9A6A0", vm.PreviewStripCardBorder);
                Assert.Equal("#C8C4BC", vm.ToolbarSeparator);
                Assert.Equal("#DCEBFF", vm.BookmarkActiveBg);
                Assert.Equal("#0078D4", vm.BookmarkActiveBorder);
                Assert.Equal("#005A9E", vm.BookmarkActiveForeground);
            }
            finally { vm.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
