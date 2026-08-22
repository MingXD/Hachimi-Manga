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

    /// <summary>复制/移动目标选择（MainWindow 注入：弹文件夹选择器 → 返回目标路径；null = 取消）。</summary>
    public Func<Task<string?>>? PickTargetFolderAction { get; set; }

    /// <summary>操作后刷新（删除/重命名/复制/移动后重建列表）。</summary>
    public Action? RefreshAction { get; set; }

    /// <summary>源路径（文件夹或书）。</summary>
    public string SourcePath => IsFolder ? (Folder?.Path ?? "") : (Book?.Path ?? "");

    /// <summary>展示尺寸（功能-1 v1.9：由主窗口模式注入）。</summary>
    public double CardWidth { get; set; } = 168;
    public double CardHeight { get; set; } = 246;

    // 展示模式数据化（v2.3.6：封面/字体随模式注入，详情模式行式无图）
    /// <summary>封面显示尺寸（大/中/小模式；详情模式 0 = 隐藏图片）。</summary>
    public double CoverWidth { get; set; } = 164;
    public double CoverHeight { get; set; } = 200;
    public double TitleFontSize { get; set; } = 12;
    public double SubtitleFontSize { get; set; } = 10;
    /// <summary>详情模式：行式布局（图标 + 名称 + 副标题横排，无图片）。</summary>
    public bool IsDetail { get; set; }

    /// <summary>名称：文件夹名 / 书名。</summary>
    public string DisplayName => IsFolder ? (Folder?.Name ?? "") : (Book?.DisplayName ?? "");

    /// <summary>详情模式行式图标（文件夹 / 书本）。</summary>
    public string IconText => IsFolder ? "📁" : "📖";

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

    [RelayCommand]
    private void ShowInExplorer()
    {
        if (SourcePath.Length > 0) MangaAgent.Core.Library.FileOps.ShowInExplorer(SourcePath);
    }

    [RelayCommand]
    private void Rename()
    {
        if (SourcePath.Length == 0) return;
        var newName = MangaAgent.App.Views.RenameDialog.Show(DisplayName);
        if (string.IsNullOrWhiteSpace(newName)) return;
        try
        {
            MangaAgent.Core.Library.FileOps.Rename(SourcePath, newName);
            RefreshAction?.Invoke();
        }
        catch { }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SourcePath.Length == 0) return;
        var ok = MangaAgent.Core.Library.FileOps.DeleteToRecycleBin(SourcePath);
        if (ok) RefreshAction?.Invoke();
    }

    [RelayCommand]
    private void Properties()
    {
        if (Book is { } b) MangaAgent.App.Views.PropertiesDialog.Show(b);
    }

    [RelayCommand]
    private async Task CopyToAsync()
    {
        if (SourcePath.Length == 0 || PickTargetFolderAction is null) return;
        var dest = await PickTargetFolderAction();
        if (string.IsNullOrEmpty(dest)) return;
        var ok = MangaAgent.Core.Library.FileOps.CopyTo(SourcePath, dest);
        if (ok) RefreshAction?.Invoke();
        global::MangaAgent.App.App.SLog($"COPY {SourcePath} -> {dest} ok={ok}");
    }

    [RelayCommand]
    private async Task MoveToAsync()
    {
        if (SourcePath.Length == 0 || PickTargetFolderAction is null) return;
        var dest = await PickTargetFolderAction();
        if (string.IsNullOrEmpty(dest)) return;
        var ok = MangaAgent.Core.Library.FileOps.MoveTo(SourcePath, dest);
        if (ok) RefreshAction?.Invoke();
        global::MangaAgent.App.App.SLog($"MOVE {SourcePath} -> {dest} ok={ok}");
    }
}
