using MangaAgent.Core.Comics;

namespace MangaAgent.Core.Library;

/// <summary>
/// 扫描一个库根目录：递归收集 CBZ/ZIP 文件 + 含图片的叶子目录。
/// 系列聚合 = 父目录名；卷号从文件名解析（vol.?d+）。
/// </summary>
public static class LibraryScanner
{
    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    public static async Task<List<BookInfo>> ScanAsync(string root, CancellationToken ct = default,
        Action<string, int>? progress = null)
    {
        var books = new List<BookInfo>();
        await ScanDirectoryAsync(root, books, ct, progress);
        return books.OrderBy(b => b.Series, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => ParseVolumeNumber(b.Volume), Comparer<int>.Default)
                    .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    private static async Task ScanDirectoryAsync(string dir, List<BookInfo> books, CancellationToken ct,
        Action<string, int>? progress = null)
    {
        ct.ThrowIfCancellationRequested();
        var parent = new DirectoryInfo(dir);
        var series = parent.Name;
        progress?.Invoke(dir, books.Count);

        // 本目录内直接的图片 → 文件夹型书
        var images = Directory.GetFiles(dir)
            .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        if (images.Count > 0)
        {
            books.Add(new BookInfo(
                dir,
                TitleFromPath(dir),
                series,
                null,
                BookKind.Folder,
                Directory.GetLastWriteTimeUtc(dir),
                images.Count));
        }

        // 递归子目录 + 收集 CBZ
        foreach (var sub in Directory.GetDirectories(dir))
            await ScanDirectoryAsync(sub, books, ct, progress);

        foreach (var file in Directory.GetFiles(dir, "*.cbz", SearchOption.TopDirectoryOnly))
        {
            books.Add(await MakeZipBookAsync(file, series, ct));
        }
        foreach (var file in Directory.GetFiles(dir, "*.zip", SearchOption.TopDirectoryOnly))
        {
            books.Add(await MakeZipBookAsync(file, series, ct));
        }
    }

    private static async Task<BookInfo> MakeZipBookAsync(string file, string? series, CancellationToken ct)
    {
        await using var src = new ZipComicSource(file);
        var meta = await src.ReadMetadataAsync(ct);
        var info = new FileInfo(file);
        var title = meta?.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(file);
        var volume = meta?.Volume ?? ParseVolumeFromName(Path.GetFileNameWithoutExtension(file));
        return new BookInfo(file, title, meta?.Series ?? series, volume, BookKind.Zip, info.LastWriteTimeUtc, src.PageCount);
    }

    private static string TitleFromPath(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? dir : name;
    }

    internal static string? ParseVolumeFromName(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(?i)(?:vol|v).?s*(d+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    internal static int ParseVolumeNumber(string? volume)
        => int.TryParse(volume, out var v) ? v : int.MaxValue;
}
