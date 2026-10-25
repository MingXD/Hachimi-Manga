using MangaAgent.Core.Comics;

namespace MangaAgent.Core.Library;

/// <summary>按文件夹层级扫描（保留目录树结构，用于层级库视图）。
/// v4.19：扫描只做结构 + 轻量元数据（不打开压缩包/PDF 读页数），页数由界面按需懒补；
/// 另提供 <see cref="RefreshShallowAsync"/> 原地浅刷新，供导航/外部变更/窗口激活时增量更新（毫秒级）。</summary>
public static class FolderTreeScanner
{
    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    /// <summary>全量结构扫描：递归整个目录树，登记子文件夹与书（页数=0，待界面懒补）。
    /// 开销 ≈ 目录枚举（6.7 万文件约 1~2 秒），远低于旧实现"逐个开包读页数"。</summary>
    public static async Task<FolderNode> ScanAsync(string root, CancellationToken ct = default)
    {
        var node = new FolderNode { Name = new DirectoryInfo(root).Name, Path = root };
        await FillStructureAsync(node, ct);
        return node;
    }

    /// <summary>原地浅刷新（v4.19）：重读 node 的直接子目录与书，并补上各子目录的书 + 孙目录名
    /// （父层因此能正确判定"单行本文件夹"与统计）；保留已有节点对象——
    /// CurrentFolder/侧边栏选中/展开状态都不失效。只做目录枚举，不打开压缩包。</summary>
    public static Task RefreshShallowAsync(FolderNode node, CancellationToken ct = default)
        => Task.Run(() => RefreshShallowCore(node, ct), ct);

    private static async Task FillStructureAsync(FolderNode node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SyncChildFolders(node);
        FillBooksShallow(node);
        foreach (var child in node.Children) await FillStructureAsync(child, ct);
    }

    private static void RefreshShallowCore(FolderNode node, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SyncChildFolders(node);
        FillBooksShallow(node);
        foreach (var child in node.Children)
        {
            ct.ThrowIfCancellationRequested();
            FillBooksShallow(child);
            SyncChildFolders(child);
        }
    }

    /// <summary>按文件系统同步直接子文件夹（复用同路径既有节点，保持对象身份与已加载的深层数据）。</summary>
    private static void SyncChildFolders(FolderNode node)
    {
        var existing = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in node.Children) existing[c.Path] = c;
        string[] subs;
        try { subs = Directory.GetDirectories(node.Path); }
        catch { subs = Array.Empty<string>(); }   // 目录被移走/无权限：保留旧节点，不抛
        Array.Sort(subs, StringComparer.OrdinalIgnoreCase);
        node.Children.Clear();
        foreach (var sub in subs)
        {
            var child = existing.TryGetValue(sub, out var e) ? e : new FolderNode { Name = Path.GetFileName(sub), Path = sub };
            child.Parent = node;
            node.Children.Add(child);
        }
    }

    /// <summary>登记本层书（不打开压缩包）：CBZ/ZIP、PDF（有 pdfium 时）+ 单行本文件夹的"目录本身"书。
    /// v4.19 幽灵书修复：目录本身算一本书只在"无子文件夹 + 无文件型书 + 直接有图片"（单行本文件夹）时成立，
    /// 分类目录（有子文件夹或压缩包）里的散图不再自成一本。</summary>
    private static void FillBooksShallow(FolderNode node)
    {
        string[] files;
        try { files = Directory.GetFiles(node.Path); }
        catch { return; }
        node.Books.Clear();
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            BookKind kind;
            if (ext is ".cbz" or ".zip") kind = BookKind.Zip;
            else if (ext == ".pdf" && Comics.Pdf.PdfiumLibrary.IsAvailable) kind = BookKind.Pdf;
            else continue;
            var info = new FileInfo(file);
            // 页数 0 = 未解析（界面后台懒补；打开阅读器时由 ComicSource 给出真实页数）
            node.Books.Add(new BookInfo(file, Path.GetFileNameWithoutExtension(file), null, null, kind,
                info.LastWriteTimeUtc, 0));
        }
        if (node.Children.Count == 0 && node.Books.Count == 0)
        {
            int images = files.Count(f => Array.IndexOf(ImageExts, Path.GetExtension(f).ToLowerInvariant()) >= 0);
            if (images > 0)
                node.Books.Add(new BookInfo(node.Path, new DirectoryInfo(node.Path).Name, null, null, BookKind.Folder,
                    Directory.GetLastWriteTimeUtc(node.Path), images));
        }
        node.Books.Sort((a, b) => ImageInfo.CompareNatural(a.Title, b.Title));
    }
}
