using Microsoft.Data.Sqlite;

namespace MangaAgent.Core.Storage;

public enum ReadingMode { Single, Double, Webtoon }
public enum ReadingDirection2 { Ltr, Rtl }

/// <summary>阅读状态（续读/模式/方向）+ 书签 + 文件夹封面。
/// v4.13：路径统一以「库根相对路径」存储（见 LibraryRoots）——整个资源库移动后重新添加库即可匹配。</summary>
public sealed class ReadingStateStore : IDisposable
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
            return best is null ? absPath : Path.GetRelativePath(Path.GetFullPath(best), full);
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
            CREATE TABLE IF NOT EXISTS FolderCovers (
                FolderPath TEXT PRIMARY KEY,
                RelPath TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
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

    // ---------- v4.3 文件夹自定义封面（只存相对路径记录，不写文件夹/图片任何属性） ----------

    /// <summary>校验封面相对路径：不含 ".."、解析后必须落在 folderPath 内、必须是图片文件且存在。</summary>
    public static bool ValidateFolderCoverRelPath(string folderPath, string relPath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(relPath) || relPath.Contains("..", StringComparison.Ordinal)) return false;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(folderPath, relPath));
        }
        catch
        {
            return false;
        }
        var root = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".avif")) return false;
        return File.Exists(fullPath);
    }

    /// <summary>保存文件夹封面（校验失败返回 false，不写入）。v4.13：FolderPath 存库根相对路径。</summary>
    public bool SetFolderCover(string folderPath, string relPath)
    {
        if (!ValidateFolderCoverRelPath(folderPath, relPath, out _)) return false;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FolderCovers (FolderPath, RelPath, UpdatedAt) VALUES ($p, $r, $t)
            ON CONFLICT(FolderPath) DO UPDATE SET RelPath = excluded.RelPath, UpdatedAt = excluded.UpdatedAt
            """;
        cmd.Parameters.AddWithValue("$p", ToStoragePath(folderPath));
        cmd.Parameters.AddWithValue("$r", relPath);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        return true;
    }

    /// <summary>读取文件夹封面相对路径（无记录返回 null）。</summary>
    public string? LoadFolderCover(string folderPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT RelPath FROM FolderCovers WHERE FolderPath = $p";
        cmd.Parameters.AddWithValue("$p", ToStoragePath(folderPath));
        using var r = cmd.ExecuteReader();
        return r.Read() ? r.GetString(0) : null;
    }

    /// <summary>批量读取（目录浏览一次查询）：folderPath(绝对) → relPath。内部按库根相对路径查询，结果键转回绝对路径。</summary>
    public Dictionary<string, string> LoadFolderCovers(IEnumerable<string> folderPaths)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var absList = folderPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (absList.Count == 0) return dict;
        // 绝对 → 存储键（rel）；同一存储键可能对应多个绝对（罕见），反转映射回填
        var storageToAbs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var abs in absList) storageToAbs[ToStoragePath(abs)] = abs;
        using var cmd = _conn.CreateCommand();
        var pars = new List<string>();
        int i = 0;
        foreach (var key in storageToAbs.Keys)
        {
            pars.Add("$p" + i);
            cmd.Parameters.AddWithValue("$p" + i, key);
            i++;
        }
        cmd.CommandText = "SELECT FolderPath, RelPath FROM FolderCovers WHERE FolderPath IN (" + string.Join(",", pars) + ")";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (storageToAbs.TryGetValue(r.GetString(0), out var abs)) dict[abs] = r.GetString(1);
        }
        return dict;
    }

    public void ClearFolderCover(string folderPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM FolderCovers WHERE FolderPath = $p";
        cmd.Parameters.AddWithValue("$p", ToStoragePath(folderPath));
        cmd.ExecuteNonQuery();
    }

    /// <summary>v4.12：目录移动后迁移封面记录（旧文件夹路径 → 新路径；相对路径不变，目录内部结构未动）。
    /// 先清理目标路径的旧记录（防残留），再更新源记录；源无记录时无操作。v4.13：两路径都换算为存储格式。</summary>
    public void MoveFolderCover(string srcDir, string dstDir)
    {
        if (string.Equals(srcDir, dstDir, StringComparison.OrdinalIgnoreCase)) return;
        var srcKey = ToStoragePath(srcDir);
        var dstKey = ToStoragePath(dstDir);
        if (string.Equals(srcKey, dstKey, StringComparison.OrdinalIgnoreCase)) return;
        using (var del = _conn.CreateCommand())
        {
            del.CommandText = "DELETE FROM FolderCovers WHERE FolderPath = $p";
            del.Parameters.AddWithValue("$p", dstKey);
            del.ExecuteNonQuery();
        }
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE FolderCovers SET FolderPath = $dst, UpdatedAt = $t WHERE FolderPath = $src";
        cmd.Parameters.AddWithValue("$dst", dstKey);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$src", srcKey);
        cmd.ExecuteNonQuery();
    }

    /// <summary>v4.13：把三张表中已存的绝对路径记录迁移为库根相对路径（幂等；已相对的不动）。
    /// 主窗口加载库后调用一次（旧版本数据升级用）。</summary>
    public void MigratePathsToRelative()
    {
        MigrateTableToRelative("ReadingState", "Path");
        try { MigrateTableToRelative("Bookmarks", "Path"); } catch { /* 主键冲突可忽略 */ }
        MigrateTableToRelative("FolderCovers", "FolderPath");
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

    public void Dispose() => _conn.Dispose();
}
