using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>阅读器 UI 测试共享基建（P1-3 从 ScrollModeFixesTests 提取）：
/// 夹具路径 / 压力书 / 等待条件 / 打开窗口进滚动模式 / 构造坏 CBZ。</summary>
internal static class ReaderTestFixtures
{
    private static readonly string Fixtures = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "reference", "fixtures", "library"));

    /// <summary>压力书：300 页 800×1200 PNG（比例 0.6667，全部等比例）。</summary>
    public static BookInfo StressBook => new(
        Path.Combine(Fixtures, "Stress", "stress_300p.cbz"),
        "stress_300p", "Stress", null, BookKind.Zip, DateTime.UtcNow, 300);

    public static readonly double PageRatio = 800.0 / 1200.0;

    public static async Task WaitUntilAsync(Func<bool> cond, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!cond() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(30);
        Assert.True(cond(), "条件超时未满足");
    }

    /// <summary>打开窗口并切到滚动模式（Single→Double→Webtoon）。
    /// 等页尺寸后台补全完成（P2-1：LoadSizes 异步化后，深页比例敏感断言需先等 SizesLoaded）。</summary>
    public static async Task<ReaderWindow> OpenWebtoonAsync(ReaderViewModel vm)
    {
        var win = new ReaderWindow { DataContext = vm };
        win.Show();
        win.Measure(new Size(1100, 800));
        win.Arrange(new Rect(0, 0, 1100, 800));
        vm.ToggleModeCommand.Execute(null);   // Single -> Double
        vm.ToggleModeCommand.Execute(null);   // Double -> Webtoon
        await WaitUntilAsync(() => vm.ViewMode == ViewMode.Webtoon);
        await WaitUntilAsync(() => vm.SizesLoaded, 15000);
        return win;
    }

    /// <summary>构造含 1 个损坏条目的 CBZ：页 2 的 deflate 压缩数据被破坏 →
    /// ZipArchive 读取该条目时抛 InvalidDataException → 真实"解码失败"路径。
    /// 注：DecodeToWidth 对垃圾字节从不抛异常（返回占位图），必须破坏流读取才能触发失败。</summary>
    public static string CreateCorruptCbz()
    {
        var path = Path.Combine(Path.GetTempPath(), $"manga_corrupt_{Guid.NewGuid():N}.cbz");
        var png1 = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var e1 = zip.CreateEntry("0001.png", CompressionLevel.Optimal); using (var s = e1.Open()) s.Write(png1);
            var e2 = zip.CreateEntry("0002.png", CompressionLevel.Optimal); using (var s = e2.Open()) s.Write(png1);
            var e3 = zip.CreateEntry("0003.png", CompressionLevel.Optimal); using (var s = e3.Open()) s.Write(png1);
        }
        // 解析本地头，定位条目 2 的 deflate 数据并破坏（→ 解压抛异常）
        var bytes = File.ReadAllBytes(path);
        int off = 0;
        for (int entry = 1; entry <= 3; entry++)
        {
            int compSize = BitConverter.ToInt32(bytes, off + 18);          // 压缩大小
            int nameLen = BitConverter.ToUInt16(bytes, off + 26);          // 文件名长
            int extraLen = BitConverter.ToUInt16(bytes, off + 28);         // 扩展长
            int dataStart = off + 30 + nameLen + extraLen;
            if (entry == 2)
                for (int i = dataStart; i < dataStart + Math.Min(compSize, 8); i++) bytes[i] = 0x00;
            off = dataStart + compSize;
        }
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
