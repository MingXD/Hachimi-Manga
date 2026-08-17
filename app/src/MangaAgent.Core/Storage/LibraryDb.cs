using Microsoft.Data.Sqlite;

namespace MangaAgent.Core.Storage;

/// <summary>库索引数据库（PoC 最小实现）：书目表 + 系列索引。</summary>
public sealed class LibraryDb : IDisposable
{
    private readonly SqliteConnection _conn;

    public LibraryDb(string path)
    {
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Books (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Path TEXT NOT NULL UNIQUE,
                Title TEXT,
                Series TEXT,
                Volume TEXT,
                PageCount INTEGER,
                ModifiedAt TEXT,
                Hash TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_Books_Series ON Books(Series);
            """;
        cmd.ExecuteNonQuery();
    }

    public void InsertBulk(IEnumerable<(string Path, string? Title, string? Series, string? Volume, int Pages)> books)
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO Books (Path, Title, Series, Volume, PageCount)
            VALUES ($p, $t, $s, $v, $pc)
            """;
        var pP = cmd.Parameters.Add("$p", SqliteType.Text);
        var pT = cmd.Parameters.Add("$t", SqliteType.Text);
        var pS = cmd.Parameters.Add("$s", SqliteType.Text);
        var pV = cmd.Parameters.Add("$v", SqliteType.Text);
        var pC = cmd.Parameters.Add("$pc", SqliteType.Integer);
        foreach (var b in books)
        {
            pP.Value = b.Path; pT.Value = (object?)b.Title ?? DBNull.Value;
            pS.Value = (object?)b.Series ?? DBNull.Value; pV.Value = (object?)b.Volume ?? DBNull.Value;
            pC.Value = b.Pages;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public long Count()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Books";
        return (long)cmd.ExecuteScalar()!;
    }

    public List<string> QueryBySeries(string series)
    {
        var list = new List<string>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Path FROM Books WHERE Series = $s ORDER BY Volume";
        cmd.Parameters.AddWithValue("$s", series);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public void Dispose() => _conn.Dispose();
}
