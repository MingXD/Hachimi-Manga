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

    public void Dispose() => _conn.Dispose();
}
