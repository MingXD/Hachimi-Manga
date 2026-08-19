namespace MangaAgent.Core.Library;

/// <summary>文件夹树节点（层级库视图）。</summary>
public sealed class FolderNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public List<FolderNode> Children { get; init; } = new();
    public List<BookInfo> Books { get; init; } = new();
    public bool IsBook => false;   // 叶子书由 Books 表达
}
