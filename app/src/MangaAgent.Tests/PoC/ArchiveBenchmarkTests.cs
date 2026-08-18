using System.Diagnostics;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Paging;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>M0.5 PoC：ZipArchive 随机读基准（目标：索引 <1s、随机翻页 <100ms）。</summary>
public class ArchiveBenchmarkTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));
    private static string Stress => Path.Combine(Fixtures, "Stress", "stress_300p.cbz");
    private static string SeriesA1 => Path.Combine(Fixtures, "TestSeriesA", "TestSeriesA_vol1.cbz");

    [Fact]
    public async Task Stress_300p_Index_And_RandomAccess_Benchmark()
    {
        Assert.True(File.Exists(Stress), $"缺少夹具: {Stress}");

        var sw = Stopwatch.StartNew();
        int pageCount;
        await using (var src = new ZipComicSource(Stress))
        {
            pageCount = src.PageCount;
            for (int i = 0; i < pageCount; i++) src.GetPageSizeAsync(i).GetAwaiter().GetResult();
        }
        sw.Stop();
        var indexMs = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(300, pageCount);

        sw.Restart();
        var bytes = new List<int>();
        await using (var src = new ZipComicSource(Stress))
        {
            var rng = new Random(42);
            for (int k = 0; k < 30; k++)
            {
                int idx = rng.Next(pageCount);
                using var s = src.OpenPageAsync(idx).GetAwaiter().GetResult();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                bytes.Add((int)ms.Length);
                Assert.True(ms.Length > 0, $"页 {idx} 为空");
            }
        }
        sw.Stop();
        var avgPageMs = sw.Elapsed.TotalMilliseconds / 30;

        var line = $"[BENCH] stress_300p: 索引={indexMs:F1}ms ({pageCount} 页), 随机翻页平均={avgPageMs:F1}ms, 页均字节={bytes.Average():F0}";
        Console.WriteLine(line);
        Assert.True(indexMs < 1000, $"索引超时: {indexMs:F0}ms");
        Assert.True(avgPageMs < 100, $"翻页超时: {avgPageMs:F1}ms");
    }

    [Fact]
    public async Task Spread_Detection_And_Metadata()
    {
        await using var src = new ZipComicSource(SeriesA1);
        var meta = src.ReadMetadataAsync().GetAwaiter().GetResult();
        Assert.NotNull(meta);
        Assert.Equal("TestSeriesA", meta!.Series);
        Assert.True(meta.MangaRightToLeft, "ComicInfo <Manga> 应为 YesAndRightToLeft");

        int n = src.PageCount;
        var refs = new List<PhysicalPageRef>(n);
        for (int i = 0; i < n; i++)
        {
            var sz = src.GetPageSizeAsync(i).GetAwaiter().GetResult();
            refs.Add(new PhysicalPageRef(i, sz?.AspectRatio ?? 0));
        }
        var layout = PageLayouter.Layout(refs, new PageLayoutOptions { CoverSingle = true, SpreadThreshold = 1.6 });
        Assert.False(layout[0].IsDouble);
        Assert.Equal(0, layout[0].FirstIndex);
        Assert.True(layout.Count >= n / 2);
        foreach (var lp in layout) Assert.True(lp.FirstIndex >= 0);
    }

    [Fact]
    public async Task Stress_5000p_Index_And_RandomAccess_Benchmark()
    {
        // M1 验收：5000 页大书（reference/fixtures/big/stress_5000p.cbz，由 tools 生成；不存在则跳过）
        var big = Path.GetFullPath(Path.Combine(Fixtures, "..", "big", "stress_5000p.cbz"));
        if (!File.Exists(big)) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int pageCount;
        await using (var src = new ZipComicSource(big))
        {
            pageCount = src.PageCount;
            for (int i = 0; i < pageCount; i++) src.GetPageSize(i);
        }
        sw.Stop();
        var indexMs = sw.Elapsed.TotalMilliseconds;
        Assert.Equal(5000, pageCount);

        sw.Restart();
        var rng = new Random(7);
        var samples = new List<long>();
        await using (var src = new ZipComicSource(big))
        {
            for (int k = 0; k < 50; k++)
            {
                int idx = rng.Next(pageCount);
                var s0 = System.Diagnostics.Stopwatch.StartNew();
                using var stream = await src.OpenPageAsync(idx);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                samples.Add(s0.ElapsedMilliseconds);
            }
        }
        sw.Stop();
        var avgMs = samples.Average();

        Console.WriteLine($"[BENCH] stress_5000p: 索引={indexMs:F0}ms ({pageCount} 页), 随机翻页平均={avgMs:F1}ms");
        Assert.True(indexMs < 1000, $"5000 页索引超时: {indexMs:F0}ms");
        Assert.True(avgMs < 100, $"随机翻页超时: {avgMs:F1}ms");
    }

}
