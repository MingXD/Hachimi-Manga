using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;
using Xunit;

namespace MangaAgent.Tests.Library;

public class LibraryScannerTests
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    [Fact]
    public async Task Scans_Cbz_And_Folder_Books()
    {
        var books = await LibraryScanner.ScanAsync(Fixtures, TestContext.Current.CancellationToken);
        Assert.True(books.Count >= 7, $"应找到 ≥7 本书，实际 {books.Count}");
        // 7 本 CBZ（3 系列 + 压力书）+ 1 个文件夹书
        var zips = books.Count(b => b.Kind == BookKind.Zip);
        var folders = books.Count(b => b.Kind == BookKind.Folder);
        Assert.Equal(7, zips);
        Assert.True(folders >= 1);
        // 系列聚合
        Assert.Contains(books, b => b.Series == "TestSeriesA");
        // 卷号解析
        Assert.Contains(books, b => b.Volume == "2" && b.Series == "TestSeriesA");
        // 元数据标题（ComicInfo Title）
        Assert.Contains(books, b => b.Title == "Volume 1" && b.Series == "TestSeriesA");
        // 页数
        var stress = books.Single(b => b.Path.EndsWith("stress_300p.cbz"));
        Assert.Equal(300, stress.PageCount);
    }

    [Fact]
    public void ReadingState_RoundTrip()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mangaagent_state_{Guid.NewGuid():N}.db");
        try
        {
            using var store = new ReadingStateStore(db);
            store.SaveState(@"C:\books\a.cbz", 12, ReadingMode.Double, ReadingDirection2.Rtl);
            var loaded = store.LoadState(@"C:\books\a.cbz");
            Assert.NotNull(loaded);
            Assert.Equal(12, loaded!.Value.LogicalPage);
            Assert.Equal(ReadingMode.Double, loaded.Value.Mode);
            Assert.Equal(ReadingDirection2.Rtl, loaded.Value.Direction);
            store.AddBookmark(@"C:\books\a.cbz", 5, "chapter start");
            store.AddBookmark(@"C:\books\a.cbz", 20);
            var marks = store.LoadBookmarks(@"C:\books\a.cbz");
            Assert.Equal(new[] { 5, 20 }, marks);
            // 未记录的书
            Assert.Null(store.LoadState(@"C:\books\missing.cbz"));
        }
        finally { try { File.Delete(db); } catch { } }
    }
}
