namespace MangaAgent.Core.Library;

public enum BookKind { Zip, Folder }

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
}
