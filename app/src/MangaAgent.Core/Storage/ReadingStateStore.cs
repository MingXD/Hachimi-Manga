using Microsoft.Data.Sqlite;

namespace MangaAgent.Core.Storage;

public enum ReadingMode { Single, Double, Webtoon }
public enum ReadingDirection2 { Ltr, Rtl }

/// <summary>阅读状态（续读/模式/方向）+ 书签。</summary>
public sealed class ReadingStateStore : IDisposable
{
    private readonly SqliteConnection _conn;

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
        cmd.Parameters.AddWithValue("$p", bookPath);
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
        cmd.Parameters.AddWithValue("$p", bookPath);
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
        cmd.Parameters.AddWithValue("$p", bookPath);
        cmd.Parameters.AddWithValue("$l", logicalPage);
        cmd.Parameters.AddWithValue("$n", (object?)note ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void RemoveBookmark(string bookPath, int logicalPage)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Bookmarks WHERE Path = $p AND LogicalPage = $l";
        cmd.Parameters.AddWithValue("$p", bookPath);
        cmd.Parameters.AddWithValue("$l", logicalPage);
        cmd.ExecuteNonQuery();
    }

    public List<int> LoadBookmarks(string bookPath)
    {
        var list = new List<int>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT LogicalPage FROM Bookmarks WHERE Path = $p ORDER BY LogicalPage";
        cmd.Parameters.AddWithValue("$p", bookPath);
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

    /// <summary>保存文件夹封面（校验失败返回 false，不写入）。</summary>
    public bool SetFolderCover(string folderPath, string relPath)
    {
        if (!ValidateFolderCoverRelPath(folderPath, relPath, out _)) return false;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO FolderCovers (FolderPath, RelPath, UpdatedAt) VALUES ($p, $r, $t)
            ON CONFLICT(FolderPath) DO UPDATE SET RelPath = excluded.RelPath, UpdatedAt = excluded.UpdatedAt
            """;
        cmd.Parameters.AddWithValue("$p", folderPath);
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
        cmd.Parameters.AddWithValue("$p", folderPath);
        using var r = cmd.ExecuteReader();
        return r.Read() ? r.GetString(0) : null;
    }

    /// <summary>批量读取（目录浏览一次查询）：folderPath → relPath。</summary>
    public Dictionary<string, string> LoadFolderCovers(IEnumerable<string> folderPaths)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var list = folderPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return dict;
        using var cmd = _conn.CreateCommand();
        var pars = new List<string>();
        for (int i = 0; i < list.Count; i++)
        {
            pars.Add("$p" + i);
            cmd.Parameters.AddWithValue("$p" + i, list[i]);
        }
        cmd.CommandText = "SELECT FolderPath, RelPath FROM FolderCovers WHERE FolderPath IN (" + string.Join(",", pars) + ")";
        using var r = cmd.ExecuteReader();
        while (r.Read()) dict[r.GetString(0)] = r.GetString(1);
        return dict;
    }

    public void ClearFolderCover(string folderPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM FolderCovers WHERE FolderPath = $p";
        cmd.Parameters.AddWithValue("$p", folderPath);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}
