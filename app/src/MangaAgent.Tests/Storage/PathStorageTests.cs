using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.Storage;

/// <summary>v4.13 路径存储：库根相对路径（库整体移动后重新添加库即可匹配）+ 旧绝对记录迁移。</summary>
public class PathStorageTests
{
    private static string MakeDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"manga_path_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void Relative_Storage_RoundTrip_And_Survives_Library_Move()
    {
        var oldRoot = MakeDir();
        var newRoot = MakeDir();
        var db = Path.Combine(Path.GetTempPath(), $"manga_path_db_{Guid.NewGuid():N}.db");
        try
        {
            var bookOld = Path.Combine(oldRoot, "作者A", "书1.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(bookOld)!);
            File.WriteAllText(bookOld, "x");

            ReadingStateStore.SetLibraryRoots(new[] { oldRoot });
            using (var store = new ReadingStateStore(db))
            {
                store.SaveState(bookOld, 12, ReadingMode.Double, ReadingDirection2.Rtl);
                // 同库根下按绝对路径可读回
                Assert.NotNull(store.LoadState(bookOld));
            }
            // 证明存的是「相对路径」：清空库根后按原绝对路径查不到
            ReadingStateStore.ResetLibraryRootsForTests();
            using (var store = new ReadingStateStore(db))
                Assert.Null(store.LoadState(bookOld));

            // 模拟整个库移动到新根：文件跟着移动 + 重新添加库
            var bookNew = Path.Combine(newRoot, "作者A", "书1.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(bookNew)!);
            File.Move(bookOld, bookNew);
            ReadingStateStore.SetLibraryRoots(new[] { newRoot });
            using (var store = new ReadingStateStore(db))
            {
                var state = store.LoadState(bookNew);
                Assert.NotNull(state);
                Assert.Equal(12, state!.Value.LogicalPage);
            }
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(oldRoot, true); } catch { }
            try { Directory.Delete(newRoot, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Longest_Library_Root_Wins_For_Relative_Path()
    {
        var root = MakeDir();
        try
        {
            var sub = Path.Combine(root, "work");
            Directory.CreateDirectory(sub);
            ReadingStateStore.SetLibraryRoots(new[] { root, sub });
            // 子库根更具体 → 相对路径以子库根为基准
            var abs = Path.Combine(sub, "a", "cover.png");
            Assert.Equal(Path.Combine("a", "cover.png"), ReadingStateStore.ToStoragePath(abs));
            // 不在任何库根下 → 保持绝对
            var outside = Path.Combine(MakeDir(), "x.png");
            Assert.Equal(outside, ReadingStateStore.ToStoragePath(outside));
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void FromStoragePath_Prefers_Existing_Candidate()
    {
        var have = MakeDir();
        var missing = MakeDir();   // 不创建
        try
        {
            File.WriteAllText(Path.Combine(have, "a.png"), "x");
            // 不存在根在前、存在根在后 → 仍应解析到存在的
            ReadingStateStore.SetLibraryRoots(new[] { missing, have });
            Assert.Equal(Path.Combine(have, "a.png"), ReadingStateStore.FromStoragePath("a.png"));
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(have, true); } catch { }
        }
    }

    [Fact]
    public void Migrate_Old_Absolute_Records_To_Relative()
    {
        var root = MakeDir();
        var db = Path.Combine(Path.GetTempPath(), $"manga_path_mig_{Guid.NewGuid():N}.db");
        try
        {
            var book = Path.Combine(root, "书2.pdf");
            Directory.CreateDirectory(root);
            File.WriteAllText(book, "x");
            // 旧版本：无库根 → 存的是绝对路径
            ReadingStateStore.ResetLibraryRootsForTests();
            using (var store = new ReadingStateStore(db))
            {
                store.SaveState(book, 7, ReadingMode.Single, ReadingDirection2.Rtl);
                store.AddBookmark(book, 7);
            }
            // 新版本：设置库根 + 迁移
            ReadingStateStore.SetLibraryRoots(new[] { root });
            using (var store = new ReadingStateStore(db))
            {
                store.MigratePathsToRelative();
                Assert.NotNull(store.LoadState(book));
                Assert.Equal(new[] { 7 }, store.LoadBookmarks(book));
            }
            // 迁移后清空库根 → 绝对路径查不到（已是相对存储）
            ReadingStateStore.ResetLibraryRootsForTests();
            using (var store = new ReadingStateStore(db))
                Assert.Null(store.LoadState(book));
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void FolderCover_Stored_Relative_And_Survives_Move()
    {
        var oldRoot = MakeDir();
        var newRoot = MakeDir();
        var db = Path.Combine(Path.GetTempPath(), $"manga_path_cov_{Guid.NewGuid():N}.db");
        try
        {
            var oldFolder = Path.Combine(oldRoot, "作者B");
            Directory.CreateDirectory(oldFolder);
            File.WriteAllBytes(Path.Combine(oldFolder, "c.png"),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="));
            ReadingStateStore.SetLibraryRoots(new[] { oldRoot });
            using (var store = new ReadingStateStore(db))
            {
                Assert.True(store.SetFolderCover(oldFolder, "c.png"));
                Assert.Equal("c.png", store.LoadFolderCover(oldFolder));
            }
            // 库整体移动（文件夹随库移动）
            var newFolder = Path.Combine(newRoot, "作者B");
            Directory.CreateDirectory(newFolder);
            File.Move(Path.Combine(oldFolder, "c.png"), Path.Combine(newFolder, "c.png"));
            ReadingStateStore.SetLibraryRoots(new[] { newRoot });
            using (var store = new ReadingStateStore(db))
                Assert.Equal("c.png", store.LoadFolderCover(newFolder));
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(oldRoot, true); } catch { }
            try { Directory.Delete(newRoot, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }
}
