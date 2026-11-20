using Microsoft.Data.Sqlite;

namespace MangaAgent.Core.Storage;

public enum ReadingMode { Single, Double, Webtoon }
public enum ReadingDirection2 { Ltr, Rtl }

/// <summary>阅读状态（续读/模式/方向）+ 书签持久化（SQLite）。
/// v4.13：路径统一以「库根相对路径」存储（见 LibraryRoots）——整个资源库移动后重新添加库即可匹配。
/// v4.20：文件夹自定义封面已迁出到库内 <c>covers.json</c>（<see cref="FolderCoverConfig"/>），
/// 本类不再处理封面；历史 <c>FolderCovers</c> 表由 <see cref="BackupLegacyFolderCovers"/> 备份后删除。</summary>
public sealed class ReadingStateStore : ILibraryStateStore, IDisposable
{
    private readonly SqliteConnection _conn;

    /// <summary>v4.13：库根列表（主窗口加载库后调用 SetLibraryRoots 设置）。
    /// 存储路径 = 相对最长匹配库根的相对路径；库根变化（库移动/重加库）后仍能按相对路径找回。
    /// 空列表时退化为绝对路径存储（兼容旧数据与无库场景）。</summary>
    public static IReadOnlyList<string> LibraryRoots { get; private set; } = Array.Empty<string>();

    /// <summary>设置库根（去尾分隔符、去重；不检查存在性——网络盘暂时不可达也应保留）。</summary>
    public static void SetLibraryRoots(IEnumerable<string> roots)
    {
        LibraryRoots = roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r =>
            {
                try { return Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch { return r.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>测试用：清空库根（恢复绝对路径存储）。</summary>
    public static void ResetLibraryRootsForTests() => LibraryRoots = Array.Empty<string>();

    /// <summary>绝对路径 → 存储路径：最长匹配库根 → 相对路径；无匹配 → 绝对路径原样。</summary>
    public static string ToStoragePath(string absPath)
    {
        if (string.IsNullOrEmpty(absPath)) return absPath;
        try
        {
            var full = Path.GetFullPath(absPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? best = null;
            foreach (var root in LibraryRoots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                if (full.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    if (best is null || root.Length > best.Length) best = root;
                }
            }
            if (best is null) return absPath;
            var bestFull = Path.GetFullPath(best).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(bestFull, StringComparison.OrdinalIgnoreCase)) return absPath;   // 库根自身 → 绝对键（防 "." 冲突）
            return Path.GetRelativePath(bestFull, full);
        }
        catch
        {
            return absPath;
        }
    }

    /// <summary>存储路径 → 绝对路径：绝对 → 原样；相对 → 遍历库根（具体优先）拼接，优先取存在者，兜底首个候选。</summary>
    public static string FromStoragePath(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        try { if (Path.IsPathRooted(stored)) return stored; }
        catch { return stored; }
        var roots = LibraryRoots;
        if (roots.Count == 0) return stored;
        string? fallback = null;
        foreach (var root in roots.OrderByDescending(r => r.Length))
        {
            string cand;
            try { cand = Path.Combine(root, stored); } catch { continue; }
            fallback ??= cand;
            if (File.Exists(cand) || Directory.Exists(cand)) return cand;
        }
        return fallback ?? stored;
    }

    public ReadingStateStore(string path)
    {
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS ReadingState (
                Path TEXT PRIMARY KEY,
                LogicalPage INTEGER NOT NULL DEFAULT 0,
                Mode TEXT NOT NULL DEFAULT 'Single',
                Direction TEXT NOT NULL DEFAULT 'Rtl',
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Bookmarks (
                Path TEXT NOT NULL,
                LogicalPage INTEGER NOT NULL,
                Note TEXT,
                CreatedAt TEXT NOT NULL,
                PRIMARY KEY (Path, LogicalPage)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void SaveState(string bookPath, int logicalPage, ReadingMode mode, ReadingDirection2 direction)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ReadingState (Path, LogicalPage, Mode, Direction, UpdatedAt)
            VALUES ($p, $l, $m, $d, $t)
            ON CONFLICT(Path) DO UPDATE SET
                LogicalPage = excluded.LogicalPage,
                Mode = excluded.Mode,
                Direction = excluded.Direction,
                UpdatedAt = excluded.UpdatedAt
            """;
        cmd.Parameters.AddWithValue("$p", ToStoragePath(bookPath));
        cmd.Parameters.AddWithValue("$l", logicalPage);
        cmd.Parameters.AddWithValue("$m", mode.ToString());
        cmd.Parameters.AddWithValue("$d", direction.ToString());
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public (int LogicalPage, ReadingMode Mode, ReadingDirection2 Direction)? LoadState(string bookPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT LogicalPage, Mode, Direction FROM ReadingState WHERE Path = $p";
        cmd.Parameters.AddWithValue("$p", ToStoragePath(bookPath));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (
            r.GetInt32(0),
            Enum.TryParse<ReadingMode>(r.GetString(1), out var m) ? m : ReadingMode.Single,
            Enum.TryParse<ReadingDirection2>(r.GetString(2), out var d) ? d : ReadingDirection2.Rtl);
    }

    public void AddBookmark(string bookPath, int logicalPage, string? note = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO Bookmarks (Path, LogicalPage, Note, CreatedAt)
            VALUES ($p, $l, $n, $t)
            """;
        cmd.Parameters.AddWithValue("$p", ToStoragePath(bookPath));
        cmd.Parameters.AddWithValue("$l", logicalPage);
        cmd.Parameters.AddWithValue("$n", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void RemoveBookmark(string bookPath, int logicalPage)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Bookmarks WHERE Path = $p AND LogicalPage = $l";
        cmd.Parameters.AddWithValue("$p", ToStoragePath(bookPath));
        cmd.Parameters.AddWithValue("$l", logicalPage);
        cmd.ExecuteNonQuery();
    }

    public List<int> LoadBookmarks(string bookPath)
    {
        var list = new List<int>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT LogicalPage FROM Bookmarks WHERE Path = $p ORDER BY LogicalPage";
        cmd.Parameters.AddWithValue("$p", ToStoragePath(bookPath));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetInt32(0));
        return list;
    }

    /// <summary>v4.21：带备注的书签列表（库文件未命中时的回退读取）。</summary>
    public IReadOnlyList<BookmarkEntryInfo> LoadBookmarkEntries(string bookPath)
    {
        var list = new List<BookmarkEntryInfo>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT LogicalPage, Note, CreatedAt FROM Bookmarks WHERE Path = $p ORDER BY LogicalPage";
        cmd.Parameters.AddWithValue("$p", ToStoragePath(bookPath));
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new BookmarkEntryInfo(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>v4.21：本存储是即时提交的 SQLite，<see cref="ILibraryStateStore.Flush"/> 无需额外动作。</summary>
    public void Flush() { }

    // ---------- v4.21：一次性迁移用（db → 各库 progress.json） ----------

    /// <summary>读出全部阅读状态（存储键 + 页码/模式/方向）。</summary>
    public List<(string Path, int Page, string Mode, string Direction)> LoadAllStates()
    {
        var list = new List<(string, int, string, string)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Path, LogicalPage, Mode, Direction FROM ReadingState";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    /// <summary>读出全部书签（存储键 + 页码 + 备注）。</summary>
    public List<(string Path, int Page, string? Note)> LoadAllBookmarks()
    {
        var list = new List<(string, int, string?)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Path, LogicalPage, Note FROM Bookmarks";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return list;
    }

    /// <summary>按「存储键」删除一条阅读状态（迁移成功后调用）。</summary>
    public bool DeleteStateByStorageKey(string pathKey, int page)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM ReadingState WHERE Path = $p";
        cmd.Parameters.AddWithValue("$p", pathKey);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>按「存储键 + 页码」删除一条书签（迁移成功后调用）。</summary>
    public bool DeleteBookmarkByStorageKey(string pathKey, int page)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Bookmarks WHERE Path = $p AND LogicalPage = $l";
        cmd.Parameters.AddWithValue("$p", pathKey);
        cmd.Parameters.AddWithValue("$l", page);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>v4.13：把三张表中已存的绝对路径记录迁移为库根相对路径（幂等；已相对的不动）。
    /// v4.14：库根本身条目现在存绝对（不再 "."）；历史遗留的 "." 记录无法还原归属 → 清理（用户需重设那几个库根文件夹的封面）。
    /// 主窗口加载库后调用一次（旧版本数据升级用）。</summary>
    public void MigratePathsToRelative()
    {
        MigrateTableToRelative("ReadingState", "Path");
        try { MigrateTableToRelative("Bookmarks", "Path"); } catch { /* 主键冲突可忽略 */ }
        // v4.14：清理无意义的 "." 记录（旧实现把"路径==库根"存成 "."，多条目互相覆盖后无法还原）
        DeleteExact("ReadingState", "Path", ".");
        DeleteExact("Bookmarks", "Path", ".");
    }

    private void DeleteExact(string table, string column, string value)
    {
        try
        {
            using var del = _conn.CreateCommand();
            del.CommandText = $"DELETE FROM {table} WHERE {column} = $v";
            del.Parameters.AddWithValue("$v", value);
            del.ExecuteNonQuery();
        }
        catch { /* 清理失败不影响其余 */ }
    }

    private void MigrateTableToRelative(string table, string column)
    {
        var rows = new List<string>();
        using (var sel = _conn.CreateCommand())
        {
            sel.CommandText = $"SELECT {column} FROM {table}";
            using var r = sel.ExecuteReader();
            while (r.Read()) rows.Add(r.GetString(0));
        }
        foreach (var old in rows)
        {
            if (!Path.IsPathRooted(old)) continue;   // 已相对 → 跳过
            var rel = ToStoragePath(old);
            if (rel == old) continue;                // 无库根匹配 → 保持绝对
            try
            {
                using var upd = _conn.CreateCommand();
                upd.CommandText = $"UPDATE OR IGNORE {table} SET {column} = $new WHERE {column} = $old";
                upd.Parameters.AddWithValue("$new", rel);
                upd.Parameters.AddWithValue("$old", old);
                upd.ExecuteNonQuery();
            }
            catch { /* 单条迁移失败不影响其余 */ }
        }
    }

    /// <summary>v4.20 收尾：把历史 <c>FolderCovers</c> 表（旧版封面记录）导出成一份 JSON 备份后删表。
    /// 封面已改为"库内 covers.json"，此表不再使用；保留备份只为避免历史记录无声消失。
    /// 幂等：表不存在/无记录时返回 null。</summary>
    public static string? BackupLegacyFolderCovers(string stateDbPath, string backupDir)
    {
        if (!File.Exists(stateDbPath)) return null;
        try
        {
            using var store = new ReadingStateStore(stateDbPath);
            if (!store.TableExists("FolderCovers")) return null;
            var rows = new List<object>();
            using (var cmd = store._conn.CreateCommand())
            {
                cmd.CommandText = "SELECT FolderPath, RelPath FROM FolderCovers";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    rows.Add(new { Folder = r.GetString(0), Image = r.GetString(1) });
            }
            string? backupFile = null;
            if (rows.Count > 0)
            {
                Directory.CreateDirectory(backupDir);
                backupFile = Path.Combine(backupDir, $"legacy-covers-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(backupFile,
                    System.Text.Json.JsonSerializer.Serialize(new { exportedAt = DateTime.UtcNow.ToString("O"), covers = rows },
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
            using (var drop = store._conn.CreateCommand())
            {
                drop.CommandText = "DROP TABLE IF EXISTS FolderCovers";
                drop.ExecuteNonQuery();
            }
            return backupFile;
        }
        catch
        {
            return null;   // 备份/删表失败不影响启动（下次再试）
        }
    }

    private bool TableExists(string table)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", table);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
    public void Dispose() => _conn.Dispose();
}