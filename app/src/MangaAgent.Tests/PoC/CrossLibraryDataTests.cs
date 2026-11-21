using MangaAgent.App.ViewModels;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Avalonia.Headless.XUnit;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>v4.22：跨库复制/移动时带上封面与进度、书签清空、备份文件清理。</summary>
public class CrossLibraryDataTests
{
    private static string NewRoot(string tag)
    {
        var p = Path.Combine(Path.GetTempPath(), $"manga_xlib_{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        return p;
    }

    private static void WriteImage(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[8]);
    }

    /// <summary>进度/书签：库 A 的书 → 复制到库 B（数据跟着走，源保留）。</summary>
    [Fact]
    public void Copy_Moves_Progress_And_Bookmarks_To_Target_Library()
    {
        var libA = NewRoot("A");
        var libB = NewRoot("B");
        var db = Path.Combine(Path.GetTempPath(), $"manga_xdb_{Guid.NewGuid():N}.db");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            var book = Path.Combine(libA, "作者", "书.zip");
            WriteImage(book);
            ReadingStateStore.SetLibraryRoots(new[] { libA, libB });
            LibraryStateService.FlushIntervalMs = 0;

            using var svc = new LibraryStateService(db);
            svc.SaveState(book, 33, ReadingMode.Double, ReadingDirection2.Rtl);
            svc.AddBookmark(book, 33, "标记");
            svc.Flush();

            // 目标位置（模拟复制后的路径）
            var dst = Path.Combine(libB, "作者", "书.zip");
            WriteImage(dst);
            var (states, marks) = svc.CopyOrMoveBookData(book, dst, isCopy: true);

            Assert.Equal(1, states);
            Assert.Equal(1, marks);
            // 源保留
            Assert.Equal(33, svc.LoadState(book)!.Value.LogicalPage);
            // 目标库文件里有记录
            var cfgB = LibraryProgressConfig.Load(libB);
            Assert.True(cfgB.TryKeyFor(dst, out var keyB));
            Assert.Equal(33, cfgB.GetState(keyB)!.Value.LogicalPage);
            Assert.Single(cfgB.GetBookmarks(keyB));
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(libA, true); } catch { }
            try { Directory.Delete(libB, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>移动：目标已有数据 → 本次覆盖；源记录清除。</summary>
    [Fact]
    public void Move_Overwrites_Target_And_Clears_Source()
    {
        var libA = NewRoot("A2");
        var libB = NewRoot("B2");
        var db = Path.Combine(Path.GetTempPath(), $"manga_xdb_{Guid.NewGuid():N}.db");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            var src = Path.Combine(libA, "书.zip");
            var dst = Path.Combine(libB, "书.zip");
            WriteImage(src);
            WriteImage(dst);
            ReadingStateStore.SetLibraryRoots(new[] { libA, libB });
            LibraryStateService.FlushIntervalMs = 0;

            using var svc = new LibraryStateService(db);
            svc.SaveState(src, 7, ReadingMode.Single, ReadingDirection2.Ltr);
            svc.AddBookmark(src, 7, "源");
            // 目标已有旧数据
            svc.SaveState(dst, 99, ReadingMode.Webtoon, ReadingDirection2.Rtl);
            svc.Flush();

            var (states, marks) = svc.CopyOrMoveBookData(src, dst, isCopy: false);
            Assert.Equal(1, states);
            Assert.Equal(1, marks);

            // 目标被覆盖为源的数据
            var cfgB = LibraryProgressConfig.Load(libB);
            cfgB.TryKeyFor(dst, out var keyB);
            Assert.Equal(7, cfgB.GetState(keyB)!.Value.LogicalPage);
            Assert.Equal(ReadingMode.Single, cfgB.GetState(keyB)!.Value.Mode);
            // 源已清除
            Assert.Null(svc.LoadState(src));
            Assert.Empty(svc.LoadBookmarkEntries(src));
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(libA, true); } catch { }
            try { Directory.Delete(libB, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>文件夹整体移动：其下所有书的进度/书签一起迁（前缀）。</summary>
    [Fact]
    public void Move_Folder_Migrates_All_Books_Under_It()
    {
        var libA = NewRoot("A3");
        var db = Path.Combine(Path.GetTempPath(), $"manga_xdb_{Guid.NewGuid():N}.db");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            var folder = Path.Combine(libA, "系列");
            Directory.CreateDirectory(folder);
            var b1 = Path.Combine(folder, "1.zip");
            var b2 = Path.Combine(folder, "2.zip");
            WriteImage(b1);
            WriteImage(b2);
            ReadingStateStore.SetLibraryRoots(new[] { libA });
            LibraryStateService.FlushIntervalMs = 0;

            using var svc = new LibraryStateService(db);
            svc.SaveState(b1, 11, ReadingMode.Single, ReadingDirection2.Rtl);
            svc.SaveState(b2, 22, ReadingMode.Double, ReadingDirection2.Rtl);
            svc.AddBookmark(b2, 22, null);
            svc.Flush();

            var dstFolder = Path.Combine(libA, "新系列");
            Directory.CreateDirectory(dstFolder);
            // 用真实文件移动模拟：先把书移过去（File.Move），再迁移数据
            File.Move(b1, Path.Combine(dstFolder, "1.zip"));
            File.Move(b2, Path.Combine(dstFolder, "2.zip"));

            var (states, marks) = svc.CopyOrMoveBookData(folder, dstFolder, isCopy: false);
            Assert.Equal(2, states);
            Assert.Equal(1, marks);

            Assert.Equal(11, svc.LoadState(Path.Combine(dstFolder, "1.zip"))!.Value.LogicalPage);
            Assert.Equal(22, svc.LoadState(Path.Combine(dstFolder, "2.zip"))!.Value.LogicalPage);
            Assert.Single(svc.LoadBookmarkEntries(Path.Combine(dstFolder, "2.zip")));
            Assert.Null(svc.LoadState(Path.Combine(folder, "1.zip")));
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(libA, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    /// <summary>备份文件清理：删 .bak/.bak-*/.broken/.tmp 与 legacy 备份，不动正式文件。</summary>
    [Fact]
    public void Cleaner_Removes_Backup_Noise_Only()
    {
        var lib = NewRoot("clean");
        var appData = NewRoot("appdata");
        try
        {
            var covers = Path.Combine(lib, "covers.json");
            var progress = Path.Combine(lib, "progress.json");
            File.WriteAllText(covers, "{}");
            File.WriteAllText(progress, "{}");
            File.WriteAllText(Path.Combine(lib, "covers.json.bak"), "old");
            File.WriteAllText(Path.Combine(lib, "covers.json.bak-20260101-000000"), "old2");
            File.WriteAllText(Path.Combine(lib, "progress.json.broken"), "bad");
            File.WriteAllText(Path.Combine(lib, "progress.json.tmp"), "tmp");
            File.WriteAllText(Path.Combine(lib, "book.zip"), "book");   // 非备份：不能碰
            File.WriteAllText(Path.Combine(appData, "legacy-covers-backup-1.json"), "{}");

            var (removed, _) = LibraryFileCleaner.CleanBackups(new[] { lib }, appData);
            Assert.Equal(5, removed);
            Assert.True(File.Exists(covers));
            Assert.True(File.Exists(progress));
            Assert.True(File.Exists(Path.Combine(lib, "book.zip")));
            Assert.False(File.Exists(Path.Combine(lib, "covers.json.bak")));
            Assert.False(File.Exists(Path.Combine(appData, "legacy-covers-backup-1.json")));
        }
        finally
        {
            try { Directory.Delete(lib, true); } catch { }
            try { Directory.Delete(appData, true); } catch { }
        }
    }
}
