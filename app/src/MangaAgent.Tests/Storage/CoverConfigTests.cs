using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.Storage;

/// <summary>v4.20：库内封面配置 covers.json 回归。
/// 覆盖：设置/读回/清除、相对库根语义、跨子目录共用封面、越界与非法格式拒绝、坏 JSON 备份、
/// 原子写 .bak、外部手改后按 mtime 重读、库根自身键 "."、移动目录改键、从 reading.db 迁移。</summary>
public class CoverConfigTests
{
    private static string NewRoot(string tag)
    {
        var p = Path.Combine(Path.GetTempPath(), $"manga_covercfg_{tag}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(p);
        return p;
    }

    private static string MakeImage(string dir, string name, int size = 8)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    [Fact]
    public void Set_Read_Clear_RoundTrip_Writes_Json()
    {
        var root = NewRoot("set");
        try
        {
            var folder = Path.Combine(root, "Book", "Sub");
            Directory.CreateDirectory(folder);
            var cover = MakeImage(folder, "cover.png");

            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(folder, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);

            var json = File.ReadAllText(cfg.FilePath);
            Assert.Contains("\"version\"", json);
            Assert.Contains("Book/Sub", json);
            Assert.Contains("cover.png", json);

            // 读回：新实例（模拟下次启动）
            var cfg2 = FolderCoverConfig.Load(root);
            Assert.Equal(cover, cfg2.GetCoverFullPath(folder));
            Assert.True(cfg2.HasCover(folder));

            // 清除
            Assert.True(cfg2.TryClear(folder));
            Assert.True(cfg2.Flush(), cfg2.LastError);
            Assert.Null(FolderCoverConfig.Load(root).GetCoverFullPath(folder));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Cover_Can_Live_Anywhere_In_Library_And_Be_Shared()
    {
        var root = NewRoot("shared");
        try
        {
            var shared = Path.Combine(root, "_shared");
            var cover = MakeImage(shared, "common.jpg");
            var a = Path.Combine(root, "A"); Directory.CreateDirectory(a);
            var b = Path.Combine(root, "B", "Nested"); Directory.CreateDirectory(b);

            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(a, cover, out var e1), e1);
            Assert.True(cfg.TrySet(b, cover, out var e2), e2);
            Assert.True(cfg.Flush(), cfg.LastError);

            var json = File.ReadAllText(cfg.FilePath);
            Assert.Contains("_shared/common.jpg", json.Replace('\\', '/'));   // 相对库根、可跨目录
            var back = FolderCoverConfig.Load(root);
            Assert.Equal(cover, back.GetCoverFullPath(a));
            Assert.Equal(cover, back.GetCoverFullPath(b));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Rejects_Outside_Library_Non_Image_And_Missing()
    {
        var root = NewRoot("reject");
        var outside = NewRoot("outside");
        try
        {
            var folder = Path.Combine(root, "Book"); Directory.CreateDirectory(folder);
            var cfg = FolderCoverConfig.Load(root);

            var outsideCover = MakeImage(outside, "x.png");   // 库外的合法图片
            Assert.False(cfg.TrySet(folder, outsideCover, out var e1));
            Assert.Contains("库", e1);

            var txt = Path.Combine(folder, "note.txt"); File.WriteAllText(txt, "x");
            Assert.False(cfg.TrySet(folder, txt, out var e2));
            Assert.Contains("图片格式", e2);

            Assert.False(cfg.TrySet(folder, Path.Combine(folder, "missing.png"), out var e3));
            Assert.Contains("不存在", e3);

            // 相对路径越界
            Assert.False(cfg.TrySetByRel("Book", "../../evil.png", out var e4));
            Assert.Contains("越界", e4);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
            try { Directory.Delete(outside, true); } catch { }
        }
    }

    [Fact]
    public void Root_Itself_Uses_Dot_Key()
    {
        var root = NewRoot("dot");
        try
        {
            var cover = MakeImage(Path.Combine(root, "img"), "c.png");
            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(root, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);
            Assert.Contains("\".\"", File.ReadAllText(cfg.FilePath));
            Assert.Equal(cover, FolderCoverConfig.Load(root).GetCoverFullPath(root));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Broken_Json_Is_Backed_Up_Not_Overwritten()
    {
        var root = NewRoot("broken");
        try
        {
            var folder = Path.Combine(root, "Book"); Directory.CreateDirectory(folder);
            var path = Path.Combine(root, FolderCoverConfig.FileName);
            File.WriteAllText(path, "{ 这不是 JSON,,, ");

            var cfg = FolderCoverConfig.Load(root);
            Assert.NotNull(cfg.LastError);
            Assert.False(File.Exists(path), "坏文件应被改名备份");
            var backups = Directory.GetFiles(root, FolderCoverConfig.FileName + ".broken");
            Assert.Single(backups);

            // 备份保留原内容，之后可重建
            var cover = MakeImage(folder, "c.jpg");
            Assert.True(cfg.TrySet(folder, cover, out var err), err);
            Assert.True(cfg.Flush(), cfg.LastError);
            Assert.Equal(cover, FolderCoverConfig.Load(root).GetCoverFullPath(folder));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Flush_Keeps_Bak_And_Reloads_After_External_Edit()
    {
        var root = NewRoot("edit");
        try
        {
            var f1 = Path.Combine(root, "A"); Directory.CreateDirectory(f1);
            var f2 = Path.Combine(root, "B"); Directory.CreateDirectory(f2);
            var c1 = MakeImage(f1, "1.png");
            var c2 = MakeImage(f2, "2.png");

            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(f1, c1, out _));
            Assert.True(cfg.Flush());
            Assert.True(cfg.TrySet(f2, c2, out _));
            Assert.True(cfg.Flush());
            Assert.False(File.Exists(cfg.FilePath + ".bak"), "v4.22 起正常写入不产生 .bak（保持库根干净）");

            // 外部手改（模拟用户编辑）：追加 C 的封面 → 按 mtime 重读生效
            var edited = File.ReadAllText(cfg.FilePath);
            var f3 = Path.Combine(root, "C"); Directory.CreateDirectory(f3);
            var c3 = MakeImage(f3, "3.png");
            edited = edited.Replace("\"covers\": {", "\"covers\": {\n    \"C\": \"C/3.png\",");
            File.WriteAllText(cfg.FilePath, edited);
            File.SetLastWriteTimeUtc(cfg.FilePath, DateTime.UtcNow.AddSeconds(5));

            Assert.Equal(c3, cfg.GetCoverFullPath(f3));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void MoveKey_Rewrites_Prefix()
    {
        var root = NewRoot("move");
        try
        {
            var book = Path.Combine(root, "A", "Book"); Directory.CreateDirectory(book);
            var cover = MakeImage(book, "cover.png");
            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(book, cover, out _));
            Assert.True(cfg.Flush());

            // 模拟目录搬到 B/Book
            var dst = Path.Combine(root, "B", "Book");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            Directory.Move(book, dst);

            Assert.True(cfg.MoveKey(book, dst));
            Assert.True(cfg.Flush(), cfg.LastError);
            Assert.Equal(Path.Combine(dst, "cover.png"), FolderCoverConfig.Load(root).GetCoverFullPath(dst));
            Assert.DoesNotContain("A/Book", File.ReadAllText(cfg.FilePath));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    // ---------- v4.20：历史 FolderCovers 表 → 备份 JSON 后删除 ----------

    [Fact]
    public void Legacy_FolderCovers_Table_Is_Backed_Up_Then_Dropped()
    {
        var backupDir = NewRoot("legacybak");
        var db = Path.Combine(Path.GetTempPath(), $"manga_legacydb_{Guid.NewGuid():N}.db");
        try
        {
            // 手工建出旧表并塞一条记录（模拟 v4.19 及以前的数据）
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE FolderCovers (FolderPath TEXT PRIMARY KEY, RelPath TEXT NOT NULL, UpdatedAt TEXT NOT NULL);
                    INSERT INTO FolderCovers VALUES ('25.08/作者A', 'cover.png', '2026-01-01T00:00:00Z');
                    """;
                cmd.ExecuteNonQuery();
            }

            var backup = ReadingStateStore.BackupLegacyFolderCovers(db, backupDir);
            Assert.NotNull(backup);
            Assert.True(File.Exists(backup));
            // JSON 默认转义非 ASCII，用解析方式校验内容
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(backup!));
            var covers = node?["covers"] as System.Text.Json.Nodes.JsonArray;
            Assert.NotNull(covers);
            Assert.Single(covers!);
            Assert.Equal("25.08/作者A", covers![0]?["Folder"]?.GetValue<string>());

            // 幂等：表已删 → 再次调用返回 null
            Assert.Null(ReadingStateStore.BackupLegacyFolderCovers(db, backupDir));
            // db 仍可正常打开（阅读进度/书签不受影响）
            using var store = new ReadingStateStore(db);
            Assert.Null(store.LoadState(@"C:\x\a.cbz"));
        }
        finally
        {
            try { Directory.Delete(backupDir, true); } catch { }
            try { File.Delete(db); } catch { }
        }
    }
    [Fact]
    public void Flush_Fails_On_ReadOnly_Library_File()
    {
        var root = NewRoot("readonly");
        try
        {
            var folder = Path.Combine(root, "Book"); Directory.CreateDirectory(folder);
            var cover = MakeImage(folder, "cover.png");
            var path = Path.Combine(root, FolderCoverConfig.FileName);
            File.WriteAllText(path, "{\"version\":1,\"covers\":{}}");
            File.SetAttributes(path, FileAttributes.ReadOnly);

            var cfg = FolderCoverConfig.Load(root);
            Assert.True(cfg.TrySet(folder, cover, out var err), err);
            Assert.False(cfg.Flush(), "只读配置文件应写入失败（上层据此回退 db）");
            Assert.NotNull(cfg.LastError);
        }
        finally
        {
            var path = Path.Combine(root, FolderCoverConfig.FileName);
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
