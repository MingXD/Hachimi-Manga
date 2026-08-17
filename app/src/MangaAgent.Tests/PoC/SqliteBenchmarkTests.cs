using System.Diagnostics;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>M0.5 PoC：SQLite 万级书目性能。</summary>
public class SqliteBenchmarkTests
{
    [Fact]
    public void Insert_10k_And_Query_Benchmark()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mangaagent_poc_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new LibraryDb(dbPath);
            var books = new List<(string, string?, string?, string?, int)>(10_000);
            for (int i = 0; i < 10_000; i++)
            {
                books.Add(($"C:\\library\\S{i % 50}\\S{i % 50}_vol{i / 50}.cbz",
                    $"Vol {i / 50}", $"S{i % 50}", (i / 50).ToString(), 30 + i % 100));
            }

            var sw = Stopwatch.StartNew();
            db.InsertBulk(books);
            sw.Stop();
            var insertMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var count = db.Count();
            var bySeries = db.QueryBySeries("S7");
            sw.Stop();
            var queryMs = sw.Elapsed.TotalMilliseconds;

            Assert.Equal(10_000, count);
            Assert.Equal(200, bySeries.Count);

            var line = $"[BENCH] sqlite: 插入 10k={insertMs:F0}ms, 计数+按系列查询={queryMs:F1}ms ({bySeries.Count} 条)";
            Console.WriteLine(line);
            Assert.True(insertMs < 5000, $"插入超时: {insertMs:F0}ms");
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }
}
