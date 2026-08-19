namespace MangaAgent.Core.Library;

/// <summary>文件夹树节点（层级库视图）。</summary>
public sealed class FolderNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public List<FolderNode> Children { get; init; } = new();
    public List<BookInfo> Books { get; init; } = new();
    public bool IsBook => false;   // 叶子书由 Books 表达

    /// <summary>父节点（目录浏览器的"返回上级"用；根为 null）。</summary>
    public FolderNode? Parent { get; set; }

    /// <summary>面包屑路径（根 → 当前）。</summary>
    public IReadOnlyList<FolderNode> PathChain
    {
        get
        {
            var chain = new List<FolderNode>();
            for (var n = this; n is not null; n = n.Parent) chain.Insert(0, n);
            return chain;
        }
    }
}
