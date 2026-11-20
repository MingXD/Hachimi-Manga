using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.Storage;

/// <summary>v4.21：阅读进度/书签搬进库内 progress.json。
/// 覆盖：库文件读写、节流写入、嵌套库根回退、库外路径回退 db、db 一次性迁移、删除清键。</summary>
public class LibraryProgressConfigTests
{
    private static string NewRoot(string tag)
    {
        var p = Path.Combine(Path.GetTempPath(), $"manga_prog_{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        return p;
    }

    private static string MakeBook(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[8]);
        return path;
    }

    [Fact]
    public void Set_Get_Flush_RoundTrip()
    {
        var root = NewRoot("rt");
        try
        {
            var book = Path.Combine(root, "作者A", "书1.zip");
            MakeBook(Path.GetDirectoryName(book)!, "书1.zip");

            var cfg = LibraryProgressConfig.Load(root);
            Assert.True(cfg.TryKeyFor(book, out var key));
            Assert.Equal("作者A/书1.zip", key);
            Assert.True(cfg.TrySetState(key, 37, ReadingMode.Double, ReadingDirection2.Rtl));
            Assert.True(cfg.Flush(), cfg.LastError);

            // 新实例（模拟重启）
            var cfg2 = LibraryProgressConfig.Load(root);
            var st = cfg2.GetState(key);
            Assert.NotNull(st);
            Assert.Equal(37, st!.Value.LogicalPage);
            Assert.Equal(ReadingMode.Double, st.Value.Mode);
            Assert.Equal(ReadingDirection2.Rtl, st.Value.Direction);

            // 文件内容
            var json = File.ReadAllText(cfg2.FilePath);
            Assert.Contains("\"books\"", json);
            Assert.Contains("\"page\": 37", json);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Bookmarks_Add_Remove_And_ClearBook()
    {
        var root = NewRoot("bm");
        try
        {
            var book = Path.Combine(root, "a", "b.zip");
            MakeBook(Path.GetDirectoryName(book)!, "b.zip");
            var cfg = LibraryProgressConfig.Load(root);
            cfg.TryKeyFor(book, out var key);

            cfg.AddBookmark(key, 5, "chapter start");
            cfg.AddBookmark(key, 20, null);
            cfg.AddBookmark(key, 5, "dup");        // 同页覆盖
            cfg.TrySetState(key, 8, ReadingMode.Single, ReadingDirection2.Rtl);
            Assert.True(cfg.Flush());

            var back = LibraryProgressConfig.Load(root).GetBookmarks(key);
            Assert.Equal(new[] { 5, 20 }, back.Select(b => b.Page));
            Assert.Equal("dup", back[0].Note);

            var edit = LibraryProgressConfig.Load(root);
            Assert.True(edit.RemoveBookmark(key, 20));
            Assert.True(edit.Flush());
            Assert.Single(LibraryProgressConfig.Load(root).GetBookmarks(key));

            // ClearBook：状态 + 书签一起清（也支持文件夹前缀）
            var cleared = LibraryProgressConfig.Load(root);
            Assert.True(cleared.ClearBook("a"));
            Assert.True(cleared.Flush());
            var after = LibraryProgressConfig.Load(root);
            Assert.Null(after.GetState(key));
            Assert.Empty(after.GetBookmarks(key));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Broken_Json_Is_Backed_Up()
    {
        var root = NewRoot("broken");
        try
        {
            var path = Path.Combine(root, LibraryProgressConfig.FileName);
            File.WriteAllText(path, "{ not json ,,,");
            var cfg = LibraryProgressConfig.Load(root);
            Assert.NotNull(cfg.LastError);
            Assert.False(File.Exists(path));
            Assert.Single(Directory.GetFiles(root, LibraryProgressConfig.FileName + ".broken-*"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Service_Writes_Library_File_And_Throttles_Flush()
    {
        var root = NewRoot("svc");
        var db = Path.Combine(Path.GetTempPath(), $"manga_progdb_{Guid.NewGuid():N}.db");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            var book = Path.Combine(root, "book.zip");
            MakeBook(root, "book.zip");
            ReadingStateStore.SetLibraryRoots(new[] { root });

            LibraryStateService.FlushIntervalMs = 0;   // 测试：每次写入立即落盘
            using var svc = new LibraryStateService(db);
            svc.SaveState(book, 12, ReadingMode.Double, ReadingDirection2.Ltr);
            svc.AddBookmark(book, 12, "标记");

            var cfg = LibraryProgressConfig.Load(root);
            cfg.TryKeyFor(book, out var key);
            Assert.Equal(12, cfg.GetState(key)!.Value.LogicalPage);
            Assert.Single(cfg.GetBookmarks(key));

            // 重启后：service 从库文件读回
            using var svc2 = new LibraryStateService(db);
            var st = svc2.LoadState(book);
            Assert.NotNull(st);
            Assert.Equal(12, st!.Value.LogicalPage);
            Assert.Equal(ReadingMode.Double, st.Value.Mode);
            Assert.Single(svc2.LoadBookmarkEntries(book));
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Nested_Root_Falls_Back_To_Outer_Progress_File()
    {
        var bz = NewRoot("nested");
        var db = Path.Combine(Path.GetTempPath(), $"manga_progdb_{Guid.NewGuid():N}.db");
        try
        {
            var inner = Path.Combine(bz, "25.08");
            var book = Path.Combine(inner, "作者", "书.zip");
            MakeBook(Path.GetDirectoryName(book)!, "书.zip");

            ReadingStateStore.SetLibraryRoots(new[] { inner, bz });
            // 记录只写在外层库根
            var outer = LibraryProgressConfig.Load(bz);
            Assert.True(outer.TryKeyFor(book, out var key));
            Assert.Equal("25.08/作者/书.zip", key);
            outer.TrySetState(key, 99, ReadingMode.Single, ReadingDirection2.Rtl);
            Assert.True(outer.Flush());

            LibraryStateService.FlushIntervalMs = 0;
            using var svc = new LibraryStateService(db);
            Assert.Equal(99, svc.LoadState(book)!.Value.LogicalPage);   // 回退到外层库根文件
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(bz, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Path_Outside_Library_Falls_Back_To_Db()
    {
        var root = NewRoot("outside");
        var outside1 = NewRoot("outsideBook");
        var db = Path.Combine(Path.GetTempPath(), $"manga_progdb_{Guid.NewGuid():N}.db");
        var oldInterval = LibraryStateService.FlushIntervalMs;
        try
        {
            var book = Path.Combine(outside1, "x.zip");
            MakeBook(outside1, "x.zip");
            ReadingStateStore.SetLibraryRoots(new[] { root });

            LibraryStateService.FlushIntervalMs = 0;
            using (var svc = new LibraryStateService(db))
                svc.SaveState(book, 5, ReadingMode.Single, ReadingDirection2.Rtl);

            using (var dbStore = new ReadingStateStore(db))
                Assert.Equal(5, dbStore.LoadState(book)!.Value.LogicalPage);
            Assert.Null(LibraryProgressConfig.Load(root).GetState("x.zip"));
        }
        finally
        {
            LibraryStateService.FlushIntervalMs = oldInterval;
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outside1, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Migrate_From_Db_Moves_States_And_Bookmarks_Into_Library_File()
    {
        var root = NewRoot("migrate");
        var db = Path.Combine(Path.GetTempPath(), $"manga_progdb_{Guid.NewGuid():N}.db");
        try
        {
            var folder = Path.Combine(root, "作者");
            var book = Path.Combine(folder, "书.zip");
            MakeBook(folder, "书.zip");

            ReadingStateStore.SetLibraryRoots(new[] { root });
            using (var s = new ReadingStateStore(db))
            {
                s.SaveState(book, 21, ReadingMode.Webtoon, ReadingDirection2.Ltr);
                s.AddBookmark(book, 21, "迁移书签");
            }

            var (migrated, kept) = LibraryStateService.MigrateFromDatabase(db);
            Assert.Equal(2, migrated);      // 1 条状态 + 1 条书签
            Assert.Equal(0, kept);

            var cfg = LibraryProgressConfig.Load(root);
            cfg.TryKeyFor(book, out var key);
            Assert.Equal(21, cfg.GetState(key)!.Value.LogicalPage);
            Assert.Equal(ReadingMode.Webtoon, cfg.GetState(key)!.Value.Mode);
            Assert.Single(cfg.GetBookmarks(key));

            using var dbStore = new ReadingStateStore(db);
            Assert.Empty(dbStore.LoadAllStates());
            Assert.Empty(dbStore.LoadAllBookmarks());
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }
}
