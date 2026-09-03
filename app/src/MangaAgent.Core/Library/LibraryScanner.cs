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

        // 递归子目录 + 收集 CBZ/PDF
        foreach (var sub in Directory.GetDirectories(dir))
            await ScanDirectoryAsync(sub, books, ct, progress);

        await AddZipBooksAsync(dir, series, books, ct);
        await AddPdfBooksAsync(dir, series, books, ct);
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

    /// <summary>安全收集 CBZ/ZIP：单个损坏/无权限文件跳过，不中断整个扫描（BUG-C v1.9）。</summary>
    private static async Task AddZipBooksAsync(string dir, string series, List<BookInfo> books, CancellationToken ct)
    {
        foreach (var file in Directory.GetFiles(dir, "*.cbz", SearchOption.TopDirectoryOnly)
                     .Concat(Directory.GetFiles(dir, "*.zip", SearchOption.TopDirectoryOnly)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                books.Add(await MakeZipBookAsync(file, series, ct));
            }
            catch (Exception ex)
            {
                // 坏文件跳过（不中断扫描）；由调用方记录
                System.Diagnostics.Debug.WriteLine($"SKIP bad archive {file}: {ex.Message}");
            }
        }
    }

    /// <summary>安全收集 PDF：依赖 pdfium.dll —— dll 缺失/加密/损坏均跳过（沿用坏文件哲学，不中断扫描）。</summary>
    private static async Task AddPdfBooksAsync(string dir, string series, List<BookInfo> books, CancellationToken ct)
    {
        if (!Comics.Pdf.PdfiumLibrary.IsAvailable) return;   // 无渲染组件：整个库跳过 PDF（避免每个文件抛异常）
        foreach (var file in Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var src = new Comics.Pdf.PdfComicSource(file);
                var meta = await src.ReadMetadataAsync(ct);
                var info = new FileInfo(file);
                var title = meta?.Title is { Length: > 0 } t ? t : Path.GetFileNameWithoutExtension(file);
                var volume = meta?.Volume ?? ParseVolumeFromName(Path.GetFileNameWithoutExtension(file));
                books.Add(new BookInfo(file, title, meta?.Series ?? series, volume, BookKind.Pdf,
                    info.LastWriteTimeUtc, src.PageCount));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SKIP bad pdf {file}: {ex.Message}");
            }
        }
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
