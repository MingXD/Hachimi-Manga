using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>批量读取（目录浏览用）：folderPath(绝对) → relPath。
    /// v4.14 修复：改为逐条查询——旧实现按"存储键→绝对路径"反转映射，若多个条目换算成同一键（如库根自身曾存 "."）
    /// 会互相覆盖/串号（用户反馈"库根文件夹设封面后返回再进入一定消失"的根因）。条目数小，逐条查询开销可忽略。</summary>
    public Dictionary<string, string> LoadFolderCovers(IEnumerable<string> folderPaths)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var abs in folderPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var rel = LoadFolderCover(abs);
            if (rel is not null) dict[abs] = rel;
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
    /// v4.14：库根本身条目现在存绝对（不再 "."）；历史遗留的 "." 记录无法还原归属 → 清理（用户需重设那几个库根文件夹的封面）。
    /// 主窗口加载库后调用一次（旧版本数据升级用）。</summary>
    public void MigratePathsToRelative()
    {
        MigrateTableToRelative("ReadingState", "Path");
        try { MigrateTableToRelative("Bookmarks", "Path"); } catch { /* 主键冲突可忽略 */ }
        MigrateTableToRelative("FolderCovers", "FolderPath");
        // v4.14：清理无意义的 "." 记录（旧实现把"路径==库根"存成 "."，多条目互相覆盖后无法还原）
        DeleteExact("ReadingState", "Path", ".");
        DeleteExact("Bookmarks", "Path", ".");
        DeleteExact("FolderCovers", "FolderPath", ".");
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

    // ---------- v4.14 导出 / 导入（设置页"数据管理"；只存设置不存图片，路径用库内相对值，跨机按存在性匹配） ----------

    /// <summary>导出全部封面/阅读进度/书签到 JSON 文件。</summary>
    public void ExportToJson(string filePath)
    {
        var data = new ExportDataDto { ExportedAt = DateTime.UtcNow.ToString("O") };
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT FolderPath, RelPath FROM FolderCovers";
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Covers.Add(new CoverRecordDto { Folder = r.GetString(0), Image = r.GetString(1) });
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Path, LogicalPage, Mode, Direction FROM ReadingState";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                data.Progress.Add(new ProgressRecordDto
                {
                    Book = r.GetString(0),
                    Page = r.GetInt32(1),
                    Mode = r.GetString(2),
                    Direction = r.GetString(3),
                });
        }
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Path, LogicalPage FROM Bookmarks";
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Bookmarks.Add(new BookmarkRecordDto { Book = r.GetString(0), Page = r.GetInt32(1) });
        }
        File.WriteAllText(filePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>从 JSON 导入（每条记录按"相对路径在当前库根下是否存在"匹配；overwrite=false 时已存在记录保留）。
    /// 返回 (应用数, 跳过数, 未匹配数)。</summary>
    public (int Applied, int Skipped, int Unmatched) ImportFromJson(string filePath, bool overwrite)
    {
        var data = JsonSerializer.Deserialize<ExportDataDto>(File.ReadAllText(filePath))
                   ?? throw new InvalidOperationException("JSON 解析失败");
        int applied = 0, skipped = 0, unmatched = 0;
        foreach (var c in data.Covers)
        {
            var abs = ResolveStoredPath(c.Folder);
            if (abs is null) { unmatched++; continue; }
            if (!overwrite && LoadFolderCover(abs) is not null) { skipped++; continue; }
            if (SetFolderCover(abs, c.Image)) applied++;
            else unmatched++;
        }
        foreach (var p in data.Progress)
        {
            var abs = ResolveStoredPath(p.Book);
            if (abs is null) { unmatched++; continue; }
            if (!overwrite && LoadState(abs) is not null) { skipped++; continue; }
            SaveState(abs, p.Page,
                Enum.TryParse<ReadingMode>(p.Mode, out var m) ? m : ReadingMode.Single,
                Enum.TryParse<ReadingDirection2>(p.Direction, out var d) ? d : ReadingDirection2.Rtl);
            applied++;
        }
        foreach (var b in data.Bookmarks)
        {
            var abs = ResolveStoredPath(b.Book);
            if (abs is null) { unmatched++; continue; }
            if (!overwrite && LoadBookmarks(abs).Contains(b.Page)) { skipped++; continue; }
            AddBookmark(abs, b.Page);
            applied++;
        }
        return (applied, skipped, unmatched);
    }

    /// <summary>存储值 → 绝对路径：绝对 → 校验存在；相对 → 遍历库根（具体优先）找存在者；都不存在 → null（未匹配）。</summary>
    private static string? ResolveStoredPath(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            if (Path.IsPathRooted(stored))
                return (File.Exists(stored) || Directory.Exists(stored)) ? stored : null;
        }
        catch { return null; }
        foreach (var root in LibraryRoots.OrderByDescending(r => r.Length))
        {
            string cand;
            try { cand = Path.Combine(root, stored); } catch { continue; }
            if (File.Exists(cand) || Directory.Exists(cand)) return cand;
        }
        return null;
    }

    public void Dispose() => _conn.Dispose();
}

// ---------- v4.14 导出/导入数据对象（JSON 键名固定，保证跨版本/跨机器兼容） ----------

public sealed class CoverRecordDto
{
    [JsonPropertyName("folder")] public string Folder { get; set; } = "";
    [JsonPropertyName("image")] public string Image { get; set; } = "";
}

public sealed class ProgressRecordDto
{
    [JsonPropertyName("book")] public string Book { get; set; } = "";
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("mode")] public string Mode { get; set; } = "Single";
    [JsonPropertyName("direction")] public string Direction { get; set; } = "Rtl";
}

public sealed class BookmarkRecordDto
{
    [JsonPropertyName("book")] public string Book { get; set; } = "";
    [JsonPropertyName("page")] public int Page { get; set; }
}

public sealed class ExportDataDto
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("exportedAt")] public string? ExportedAt { get; set; }
    [JsonPropertyName("covers")] public List<CoverRecordDto> Covers { get; set; } = new();
    [JsonPropertyName("progress")] public List<ProgressRecordDto> Progress { get; set; } = new();
    [JsonPropertyName("bookmarks")] public List<BookmarkRecordDto> Bookmarks { get; set; } = new();
}
