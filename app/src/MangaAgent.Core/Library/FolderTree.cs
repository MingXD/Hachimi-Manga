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
        // 本目录的书（CBZ + 文件夹型：直接含图片的目录 → 一本漫画）
        foreach (var file in Directory.GetFiles(node.Path).Where(f =>
            Path.GetExtension(f).ToLowerInvariant() is ".cbz" or ".zip"))
        {
            node.Books.Add(await MakeBookAsync(file, ct));
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

    private static async Task<BookInfo> MakeBookAsync(string file, CancellationToken ct)
    {
        await using var src = new ZipComicSource(file);
        var meta = await src.ReadMetadataAsync(ct);
        var info = new FileInfo(file);
        return new BookInfo(file, meta?.Title ?? Path.GetFileNameWithoutExtension(file), meta?.Series,
            meta?.Volume, BookKind.Zip, info.LastWriteTimeUtc, src.PageCount);
    }
}
