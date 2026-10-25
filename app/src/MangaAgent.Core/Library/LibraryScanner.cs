using MangaAgent.Core.Comics;

namespace MangaAgent.Core.Library;

/// <summary>
/// 扫描一个库根目录：递归收集 CBZ/ZIP/PDF 文件 + 含图片的叶子目录（单行本文件夹）。
/// 系列聚合 = 父目录名；卷号从文件名解析（vol.?d+）。
/// v4.19：默认轻量模式（<c>readPageCounts=false</c>）—— 不打开压缩包/PDF，页数记 0 由界面懒补；
/// 并且每个目录只枚举一次（旧实现每层 GetFiles 4 次），启动/添加库都走这条快路径。
/// </summary>
public static class LibraryScanner
{
    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    public static async Task<List<BookInfo>> ScanAsync(string root, CancellationToken ct = default,
        Action<string, int>? progress = null, bool readPageCounts = false)
    {
        var books = new List<BookInfo>();
        await ScanDirectoryAsync(root, books, ct, progress, readPageCounts);
        return books.OrderBy(b => b.Series, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => ParseVolumeNumber(b.Volume), Comparer<int>.Default)
                    .ThenBy(b => b.Title, StringComparer.OrdinalIgnoreCase)
                    .ToList();
    }

    private static async Task ScanDirectoryAsync(string dir, List<BookInfo> books, CancellationToken ct,
        Action<string, int>? progress = null, bool readPageCounts = false)
    {
        ct.ThrowIfCancellationRequested();
        var series = new DirectoryInfo(dir).Name;
        progress?.Invoke(dir, books.Count);

        // 一次枚举（v4.19）：目录项/图片/压缩包同时收集，避免每层多次 GetFiles/GetDirectories
        var subDirs = new List<string>();
        var archives = new List<string>();
        int images = 0;
        try
        {
            foreach (var fi in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                if ((fi.Attributes & FileAttributes.Directory) != 0) { subDirs.Add(fi.FullName); continue; }
                if (fi is not FileInfo file) continue;
                var ext = fi.Extension.ToLowerInvariant();
                if (Array.IndexOf(ImageExts, ext) >= 0) images++;
                else if (ext is ".cbz" or ".zip" or ".pdf") archives.Add(file.FullName);
            }
        }
        catch (Exception ex)
        {
            // 权限/目录消失：跳过该层（不中断整库扫描）
            System.Diagnostics.Debug.WriteLine($"SKIP dir {dir}: {ex.Message}");
            return;
        }

        // v4.19 幽灵书修复：目录本身算一本书只在"无子文件夹 + 无压缩包/PDF + 直接有图片"（单行本文件夹）时成立
        if (images > 0 && subDirs.Count == 0 && archives.Count == 0)
        {
            books.Add(new BookInfo(
                dir,
                TitleFromPath(dir),
                series,
                null,
                BookKind.Folder,
                Directory.GetLastWriteTimeUtc(dir),
                images));
        }

        // 递归子目录 + 收集 CBZ/PDF
        foreach (var sub in subDirs)
            await ScanDirectoryAsync(sub, books, ct, progress, readPageCounts);

        foreach (var file in archives)
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(file).ToLowerInvariant();
            try
            {
                books.Add(ext == ".pdf"
                    ? await MakePdfBookAsync(file, series, ct, readPageCounts)
                    : await MakeZipBookAsync(file, series, ct, readPageCounts));
            }
            catch (Exception ex)
            {
                // 坏文件/缺 pdfium/加密 → 跳过（不中断扫描）
                System.Diagnostics.Debug.WriteLine($"SKIP bad book {file}: {ex.Message}");
            }
        }
    }

    private static async Task<BookInfo> MakeZipBookAsync(string file, string? series, CancellationToken ct, bool readPageCounts)
    {
        var info = new FileInfo(file);
        if (!readPageCounts)
            return new BookInfo(file, Path.GetFileNameWithoutExtension(file), series,
                ParseVolumeFromName(Path.GetFileNameWithoutExtension(file)), BookKind.Zip, info.LastWriteTimeUtc, 0);
        await using var src = new ZipComicSource(file);
        var meta = await src.ReadMetadataAsync(ct);
        var title = meta?.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(file);
        var volume = meta?.Volume ?? ParseVolumeFromName(Path.GetFileNameWithoutExtension(file));
        return new BookInfo(file, title, meta?.Series ?? series, volume, BookKind.Zip, info.LastWriteTimeUtc, src.PageCount);
    }

    /// <summary>PDF 书：依赖 pdfium.dll —— dll 缺失/加密/损坏均跳过（沿用坏文件哲学，不中断扫描）。</summary>
    private static async Task<BookInfo> MakePdfBookAsync(string file, string? series, CancellationToken ct, bool readPageCounts)
    {
        var info = new FileInfo(file);
        if (!readPageCounts || !Comics.Pdf.PdfiumLibrary.IsAvailable)
            return new BookInfo(file, Path.GetFileNameWithoutExtension(file), series,
                ParseVolumeFromName(Path.GetFileNameWithoutExtension(file)), BookKind.Pdf, info.LastWriteTimeUtc, 0);
        await using var src = new Comics.Pdf.PdfComicSource(file);
        var meta = await src.ReadMetadataAsync(ct);
        var title = meta?.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(file);
        var volume = meta?.Volume ?? ParseVolumeFromName(Path.GetFileNameWithoutExtension(file));
        return new BookInfo(file, title, meta?.Series ?? series, volume, BookKind.Pdf, info.LastWriteTimeUtc, src.PageCount);
    }

    private static string TitleFromPath(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? dir : name;
    }

    internal static string? ParseVolumeFromName(string name)
    {
        // v4-PDF 修复①：原模式中的 \b 在 C# 字符串里是退格字符（非正则词边界），且 .?s* 意图不明 ——
        // 文件名卷号 fallback 从未生效（CBZ 有 ComicInfo 掩盖；PDF 无元数据即暴露）。
        // v4.1 扩展：支持中文卷号（第10卷 / 第 3 巻）——中文漫画命名（晚安布布系列）默认形态。
        var patterns = new[]
        {
            @"(?i)(?<![a-z0-9])v(?:ol)?\.?\s*(\d+)",   // vol.10 / v2 / Vol 3
            @"第\s*(\d+)\s*[卷巻]",                    // 第10卷 / 第 3 巻
        };
        foreach (var p in patterns)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, p);
            if (m.Success) return m.Groups[1].Value;
        }
        return null;
    }

    internal static int ParseVolumeNumber(string? volume)
        => int.TryParse(volume, out var v) ? v : int.MaxValue;
}
