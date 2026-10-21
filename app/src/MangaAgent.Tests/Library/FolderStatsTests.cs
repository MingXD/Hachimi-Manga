using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.Library;

/// <summary>v4.15 文件夹属性：大小统计与格式化。</summary>
public class FolderStatsTests
{
    private static string MakeDir()
    {
        var d = Path.Combine(Path.GetTempPath(), $"manga_stats_{Guid.NewGuid():N}");
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public async Task Computes_Total_Bytes_Files_And_Folders()
    {
        var root = MakeDir();
        try
        {
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[100]);
            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllBytes(Path.Combine(sub, "b.bin"), new byte[200]);
            var deep = Path.Combine(sub, "deep");
            Directory.CreateDirectory(deep);
            File.WriteAllBytes(Path.Combine(deep, "c.bin"), new byte[300]);

            var r = await FolderStats.ComputeAsync(root);
            Assert.Equal(600L, r.TotalBytes);
            Assert.Equal(3, r.Files);
            Assert.Equal(2, r.Folders);   // sub + sub\deep
            Assert.Equal(0, r.Skipped);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task Empty_Dir_Returns_Zero()
    {
        var root = MakeDir();
        try
        {
            var r = await FolderStats.ComputeAsync(root);
            Assert.Equal(0L, r.TotalBytes);
            Assert.Equal(0, r.Files);
            Assert.Equal(0, r.Folders);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task Cancellation_Is_Observed()
    {
        var root = MakeDir();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => FolderStats.ComputeAsync(root, null, cts.Token));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(999, "999 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1 MB")]
    [InlineData(1073741824, "1 GB")]
    [InlineData(2684354560L, "2.5 GB")]
    public void FormatSize_Formats_Correctly(long bytes, string expected)
        => Assert.Equal(expected, FolderStats.FormatSize(bytes));
}
