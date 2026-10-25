namespace MangaAgent.Core.Library;

using System.Collections.ObjectModel;

/// <summary>文件夹树节点（层级库视图）。</summary>
public sealed class FolderNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    /// <summary>子节点集合（v4.19：改为可观察集合 —— 浅刷新原地增删子文件夹时，侧边栏树实时更新且保留展开状态）。</summary>
    public ObservableCollection<FolderNode> Children { get; } = new();
    public List<BookInfo> Books { get; init; } = new();
    public bool IsBook => false;   // 叶子书由 Books 表达

    /// <summary>父节点（目录浏览器的"返回上级"用；根为 null）。</summary>
    public FolderNode? Parent { get; set; }

    /// <summary>单行本文件夹（v4.18）：无子文件夹，且直接装的全部是图片（FolderBook）——即"一个文件夹 = 一本漫画"。
    /// 仅这类叶子在目录浏览里折叠成漫画条目；装 zip/pdf 的分类文件夹（如 "[nemuke？ (似せ)]"）必须保持文件夹身份，
    /// 否则会被误渲染成一本漫画（书名取第一本），用户看不到文件夹本身，对它"移动到该文件夹"还会源=目标目录失败。</summary>
    public bool IsSingleVolumeFolder =>
        Children.Count == 0 && Books.Count > 0 && Books.All(b => b.Kind == BookKind.Folder);

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
