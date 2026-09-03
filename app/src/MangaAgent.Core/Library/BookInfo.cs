namespace MangaAgent.Core.Library;

public enum BookKind { Zip, Folder, Pdf }

/// <summary>库中的一本书（文件系统事实的快照）。</summary>
public sealed record BookInfo(
    string Path,
    string Title,
    string? Series,
    string? Volume,
    BookKind Kind,
    DateTime ModifiedAt,
    int PageCount)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Series)
        ? Title
        : Volume is null ? $"{Series} - {Title}" : $"{Series} Vol.{Volume}";

    /// <summary>类型展示名（封面墙/目录浏览器/属性对话框共用；文件夹书显示"文件夹"）。</summary>
    public string KindLabel => Kind switch
    {
        BookKind.Pdf => "PDF",
        BookKind.Zip => "CBZ",
        _ => "文件夹",
    };

    /// <summary>文件型书（非纯图片文件夹书）：Zip/PDF —— 重命名需保留扩展名等场景。</summary>
    public bool IsFileKind => Kind is BookKind.Zip or BookKind.Pdf;

    /// <summary>按路径推断书类型（目录 → 文件夹书；.cbz/.zip → Zip；.pdf → Pdf；未知扩展按 Zip 处理，保持旧行为）。</summary>
    public static BookKind KindForPath(string path)
    {
        if (Directory.Exists(path)) return BookKind.Folder;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => BookKind.Pdf,
            ".cbz" or ".zip" => BookKind.Zip,
            _ => BookKind.Zip,
        };
    }
}
