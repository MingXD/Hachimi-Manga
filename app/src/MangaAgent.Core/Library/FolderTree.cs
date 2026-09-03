using MangaAgent.Core.Comics;

namespace MangaAgent.Core.Library;

/// <summary>按文件夹层级扫描（保留目录树结构，用于层级库视图）。</summary>
public static class FolderTreeScanner
{
    public static async Task<FolderNode> ScanAsync(string root, CancellationToken ct = default)
    {
        var node = new FolderNode { Name = new DirectoryInfo(root).Name, Path = root };
        await FillAsync(node, ct);
        return node;
    }

    private static async Task FillAsync(FolderNode node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // 目录浏览器 = 文件系统导航：保留所有子文件夹（含暂无漫画的，用户可能手动整理后放入）
        foreach (var sub in Directory.GetDirectories(node.Path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var child = new FolderNode { Name = Path.GetFileName(sub), Path = sub, Parent = node };
            await FillAsync(child, ct);
            node.Children.Add(child);
        }
        // 本目录的书（CBZ/PDF 文件书 + 文件夹型：直接含图片的目录 → 一本漫画）。
        // 单个坏文件（损坏/加密/无 pdfium.dll）跳过，不中断整树（与 LibraryScanner 的坏文件哲学一致）
        foreach (var file in Directory.GetFiles(node.Path))
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            bool isArchive = ext is ".cbz" or ".zip";
            bool isPdf = ext == ".pdf" && Comics.Pdf.PdfiumLibrary.IsAvailable;
            if (!isArchive && !isPdf) continue;
            try
            {
                node.Books.Add(await MakeBookAsync(file, ct));
            }
            catch
            {
                // 坏文件跳过
            }
        }
        // 扩展名与 FolderComicSource/LibraryScanner 一致（含 gif/bmp）；PageCount = 本目录直接图片数
        var images = Directory.GetFiles(node.Path).Where(f =>
            Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp").ToList();
        if (images.Count > 0)
        {
            node.Books.Add(new BookInfo(node.Path, new DirectoryInfo(node.Path).Name, null, null, BookKind.Folder,
                Directory.GetLastWriteTimeUtc(node.Path), images.Count));
        }
        node.Books.Sort((a, b) => ImageInfo.CompareNatural(a.Title, b.Title));
    }

    /// <summary>文件型书：按扩展名打开源读元数据/页数（CBZ/PDF 共用）。</summary>
    private static async Task<BookInfo> MakeBookAsync(string file, CancellationToken ct)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext is ".cbz" or ".zip")
        {
            await using var src = new ZipComicSource(file);
            var meta = await src.ReadMetadataAsync(ct);
            var info = new FileInfo(file);
            return new BookInfo(file, meta?.Title ?? Path.GetFileNameWithoutExtension(file), meta?.Series,
                meta?.Volume, BookKind.Zip, info.LastWriteTimeUtc, src.PageCount);
        }
        // PDF：dll 缺失/损坏跳过（不做 Book —— 目录浏览不显示，与扫描行为一致）
        await using var pdf = new Comics.Pdf.PdfComicSource(file);
        var pmeta = await pdf.ReadMetadataAsync(ct);
        var pinfo = new FileInfo(file);
        return new BookInfo(file, pmeta?.Title ?? Path.GetFileNameWithoutExtension(file), pmeta?.Series,
            pmeta?.Volume, BookKind.Pdf, pinfo.LastWriteTimeUtc, pdf.PageCount);
    }
}
