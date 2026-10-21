// TEMP verify - 检查真实库封面记录可解析性（跑完即删）
using Microsoft.Data.Sqlite;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.Temp;

public class TempFolderCoverDbCheck
{
    private static void Log(string s) =>
        File.AppendAllText(@"D:\MangaAgent\app\temp-coverdb.txt", s + Environment.NewLine);

    [Fact]
    public void Check_Real_Cover_Records()
    {
        File.WriteAllText(@"D:\MangaAgent\app\temp-coverdb.txt", "START\n");
        var data = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(data, "HachimiManga", "reading.db");
        var libsFile = Path.Combine(data, "HachimiManga", "libraries.txt");
        var tempDb = Path.Combine(Path.GetTempPath(), $"covercheck_{Guid.NewGuid():N}.db");
        if (!File.Exists(dbPath)) { Log("DB MISSING"); return; }
        File.Copy(dbPath, tempDb, true);
        // 读取用户库根
        var roots = File.Exists(libsFile)
            ? File.ReadAllLines(libsFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
            : new List<string>();
        Log($"library roots ({roots.Count}):");
        foreach (var r in roots) Log($"  {r}  exists={Directory.Exists(r)}");
        ReadingStateStore.SetLibraryRoots(roots);
        int total = 0, absolute = 0, relative = 0, resolved = 0, missing = 0;
        var missingSamples = new List<string>();
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = tempDb }.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT FolderPath, RelPath FROM FolderCovers";
            using var r2 = cmd.ExecuteReader();
            while (r2.Read())
            {
                total++;
                var stored = r2.GetString(0);
                var rel = r2.GetString(1);
                bool isAbs = Path.IsPathRooted(stored);
                if (isAbs) absolute++; else relative++;
                var abs = ReadingStateStore.FromStoragePath(stored);
                var img = Path.Combine(abs, rel);
                if (File.Exists(img)) resolved++;
                else
                {
                    missing++;
                    if (missingSamples.Count < 8)
                        missingSamples.Add($"stored='{stored}' rel='{rel}' -> abs='{abs}' file='{img}'");
                }
            }
        }
        Log($"records={total} absolute={absolute} relative={relative} resolved={resolved} missing={missing}");
        foreach (var s in missingSamples) Log("  MISS: " + s);
        ReadingStateStore.ResetLibraryRootsForTests();
        try { File.Delete(tempDb); } catch { }
        Log("DONE");
    }
}
