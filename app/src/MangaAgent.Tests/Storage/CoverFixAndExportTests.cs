using Microsoft.Data.Sqlite;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.Storage;

/// <summary>v4.14：库根自身封面（"." 冲突）修复 + 导出/导入（库改名后按相对路径匹配）。</summary>
public class CoverFixAndExportTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private static string MakeDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"manga_fix_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Log(string s) =>
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "manga_fix_log.txt"), s + Environment.NewLine);

    [Fact]
    public void Library_Root_Itself_Covers_Do_Not_Collide()
    {
        // 两个同级库根（用户的"秘密結社/毎日健康生活"场景）
        var rootA = MakeDir();
        var rootB = MakeDir();
        var db = Path.Combine(Path.GetTempPath(), $"manga_fix_db_{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllBytes(Path.Combine(rootA, "a.png"), TinyPng);
            File.WriteAllBytes(Path.Combine(rootB, "b.png"), TinyPng);
            ReadingStateStore.SetLibraryRoots(new[] { rootA, rootB });
            using (var store = new ReadingStateStore(db))
            {
                Assert.True(store.SetFolderCover(rootA, "a.png"));
                Assert.True(store.SetFolderCover(rootB, "b.png"));
                // 各自读回自己的（旧实现："." 冲突会互相覆盖/丢失）
                Assert.Equal("a.png", store.LoadFolderCover(rootA));
                Assert.Equal("b.png", store.LoadFolderCover(rootB));
                var batch = store.LoadFolderCovers(new[] { rootA, rootB });
                Assert.Equal(2, batch.Count);
                Assert.Equal("a.png", batch[rootA]);
                Assert.Equal("b.png", batch[rootB]);
            }
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(rootA, true); } catch { }
            try { Directory.Delete(rootB, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Migration_Cleans_Dot_Records()
    {
        var db = Path.Combine(Path.GetTempPath(), $"manga_fix_dot_{Guid.NewGuid():N}.db");
        try
        {
            using (var _ = new ReadingStateStore(db)) { }   // 建表
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db }.ToString()))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO FolderCovers (FolderPath, RelPath, UpdatedAt) VALUES ('.', 'x.png', 't')";
                cmd.ExecuteNonQuery();
            }
            using (var store = new ReadingStateStore(db))
            {
                store.MigratePathsToRelative();
                Assert.Null(store.LoadFolderCover("."));   // "." 记录被清理（无法还原归属）
            }
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { File.Delete(db); } catch { }
        }
    }

    [Fact]
    public void Export_Import_Restores_After_Library_Rename()
    {
        var oldRoot = MakeDir();
        var newRoot = oldRoot + "_renamed";
        var db = Path.Combine(Path.GetTempPath(), $"manga_fix_exp_{Guid.NewGuid():N}.db");
        var json = Path.Combine(Path.GetTempPath(), $"manga_fix_{Guid.NewGuid():N}.json");
        try
        {
            var author = Path.Combine(oldRoot, "作者A");
            Directory.CreateDirectory(author);
            File.WriteAllBytes(Path.Combine(author, "c.png"), TinyPng);
            var book = Path.Combine(author, "书1.pdf");
            File.WriteAllText(book, "x");

            ReadingStateStore.SetLibraryRoots(new[] { oldRoot });
            using (var store = new ReadingStateStore(db))
            {
                Assert.True(store.SetFolderCover(author, "c.png"));
                store.SaveState(book, 5, ReadingMode.Double, ReadingDirection2.Rtl);
                store.ExportToJson(json);
            }
            Assert.True(File.Exists(json));
            // 模拟"库根文件夹改名"（用户在文件管理器改名）→ 重新添加库
            Directory.Move(oldRoot, newRoot);
            ReadingStateStore.SetLibraryRoots(new[] { newRoot });
            using (var store = new ReadingStateStore(db))
            {
                var (applied, skipped, unmatched) = store.ImportFromJson(json, overwrite: true);
                Assert.True(applied >= 2, $"应导入封面+进度，实际 applied={applied} skipped={skipped} unmatched={unmatched}");
                Assert.Equal(0, unmatched);
                var newAuthor = Path.Combine(newRoot, "作者A");
                Assert.Equal("c.png", store.LoadFolderCover(newAuthor));
                var newBook = Path.Combine(newAuthor, "书1.pdf");
                Assert.NotNull(store.LoadState(newBook));
            }
            // 未匹配：加入一条不存在的记录
            File.WriteAllText(json, File.ReadAllText(json).Replace("\"covers\"", "\"covers\""));
            // overwrite=false 时已存在记录被跳过
            using (var store = new ReadingStateStore(db))
            {
                var (applied2, skipped2, _) = store.ImportFromJson(json, overwrite: false);
                Assert.Equal(0, applied2);
                Assert.True(skipped2 >= 2, $"已存在应跳过，实际 skipped={skipped2}");
            }
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(newRoot, true); } catch { }
            try { Directory.Delete(oldRoot, true); } catch { }
            try { File.Delete(db); } catch { }
            try { File.Delete(json); } catch { }
        }
    }

    [Fact]
    public void Import_Reports_Unmatched_Records()
    {
        var root = MakeDir();
        var db = Path.Combine(Path.GetTempPath(), $"manga_fix_unm_{Guid.NewGuid():N}.db");
        var json = Path.Combine(Path.GetTempPath(), $"manga_fix_{Guid.NewGuid():N}.json");
        try
        {
            ReadingStateStore.SetLibraryRoots(new[] { root });
            File.WriteAllText(json, """
                {
                  "version": 1,
                  "covers": [ { "folder": "不存在的作者", "image": "x.png" } ],
                  "progress": [ { "book": "不存在\\书.pdf", "page": 3 } ],
                  "bookmarks": []
                }
                """);
            using var store = new ReadingStateStore(db);
            var (applied, _, unmatched) = store.ImportFromJson(json, overwrite: true);
            Assert.Equal(0, applied);
            Assert.Equal(2, unmatched);
        }
        finally
        {
            ReadingStateStore.ResetLibraryRootsForTests();
            try { Directory.Delete(root, true); } catch { }
            try { File.Delete(db); } catch { }
            try { File.Delete(json); } catch { }
        }
    }
}
