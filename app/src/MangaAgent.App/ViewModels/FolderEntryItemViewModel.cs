using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaAgent.App.Services;
using MangaAgent.Core.Library;

namespace MangaAgent.App.ViewModels;

/// <summary>目录浏览器条目：文件夹（可进入下一层）或漫画书（双击打开）。
/// 需求：A 下 B/C/D —— B 是文件夹样式，C/D 是漫画封面样式；双击 B 进入下一层，双击 C/D 打开漫画。</summary>
public partial class FolderEntryItemViewModel : ViewModelBase
{
    /// <summary>条目类型：文件夹 / 书。</summary>
    public bool IsFolder { get; }

    public FolderNode? Folder { get; }
    public BookInfo? Book { get; }

    /// <summary>打开动作（MainWindow 注入：文件夹 → 进入下一层；书 → 打开阅读器）。</summary>
    public Action<FolderEntryItemViewModel>? OpenAction { get; set; }

    /// <summary>名称：文件夹名 / 书名。</summary>
    public string DisplayName => IsFolder ? (Folder?.Name ?? "") : (Book?.DisplayName ?? "");

    /// <summary>副标题：文件夹显示子项数；书显示页数与类型。</summary>
    public string Subtitle => IsFolder
        ? $"{(Folder?.Children.Count ?? 0)} 个子文件夹 · {(Folder?.Books.Count ?? 0)} 本漫画"
        : $"{(Book?.PageCount ?? 0)} 页 · {(Book?.Kind == BookKind.Zip ? "CBZ" : "文件夹")}";

    /// <summary>漫画封面（文件夹条目无封面）。</summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    public FolderEntryItemViewModel(FolderNode folder)
    {
        IsFolder = true;
        Folder = folder;
    }

    public FolderEntryItemViewModel(BookInfo book)
    {
        IsFolder = false;
        Book = book;
    }

    /// <summary>漫画条目：懒加载封面（BUG-002 同款按需加载）。</summary>
    public async Task LoadThumbnailAsync()
    {
        if (IsFolder || Thumbnail is not null || Book is null) return;
        Thumbnail = await ThumbnailService.GetThumbnailAsync(Book);
    }

    [RelayCommand]
    private void Open() => OpenAction?.Invoke(this);
}
