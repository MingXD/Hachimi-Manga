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

    /// <summary>文件操作失败提示（v3 #3：复制/移动失败不再静默，注入弹窗/状态栏显示错误）。</summary>
    public Action<string>? NotifyError { get; set; }

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

    /// <summary>副标题第 1 行：文件夹 = 本层 M 本漫画 · N 个子文件夹（M/N 只统计这一层）；书 = 页数（纯图片无后缀，CBZ 带 · CBZ）。</summary>
    public string Subtitle
    {
        get
        {
            if (IsFolder)
            {
                if (Folder is null) return "";
                int m = CountDirectBooks(Folder);
                int n = CountDirectSubfolders(Folder);
                if (m > 0 && n > 0) return $"{m} 本漫画 · {n} 个子文件夹";
                if (m > 0) return $"{m} 本漫画";
                if (n > 0) return $"{n} 个子文件夹";
                return "";
            }
            return Book is null ? "" : $"{Book.PageCount} 页" + (Book.Kind == BookKind.Zip ? " · CBZ" : "");
        }
    }

    /// <summary>详情模式单行副标题：M 本漫画 · N 个子文件夹 · 共 X 本漫画（各段为 0 时省略；漫画条目 = 页数）。</summary>
    public string SubtitleOneLine
    {
        get
        {
            if (IsFolder)
            {
                if (Folder is null) return "";
                int m = CountDirectBooks(Folder);
                int n = CountDirectSubfolders(Folder);
                int x = CountBooksRecursive(Folder);
                var parts = new List<string>();
                if (m > 0) parts.Add($"{m} 本漫画");
                if (n > 0) parts.Add($"{n} 个子文件夹");
                if (x > 0) parts.Add($"共 {x} 本漫画");
                return string.Join(" · ", parts);
            }
            return Subtitle;
        }
    }

    /// <summary>副标题第 2 行（仅文件夹）：递归统计目录下共 X 本漫画（X=0 不显示）。</summary>
    public string StatsLine2
    {
        get
        {
            if (!IsFolder || Folder is null) return "";
            int x = CountBooksRecursive(Folder);
            return x > 0 ? $"目录下共 {x} 本漫画" : "";
        }
    }

    // ---------- 统计辅助（v2.3.7：M/N 只统计本层，X 递归） ----------
    /// <summary>纯图片文件夹 = 漫画：无子文件夹且本目录直接含图片。</summary>
    internal static bool IsPureImageFolder(MangaAgent.Core.Library.FolderNode n)
        => n.Children.Count == 0 && n.Books.Any(b => b.Kind == BookKind.Folder);

    /// <summary>本层直接漫画数 M：纯图片子文件夹数 + 本目录直接图片(FolderBook)数 + 本目录 Zip/CBZ 数。</summary>
    internal static int CountDirectBooks(MangaAgent.Core.Library.FolderNode n)
        => n.Children.Count(IsPureImageFolder) + n.Books.Count(b => b.Kind is BookKind.Folder or BookKind.Zip);

    /// <summary>本层直接子文件夹数 N：非纯图片的直接子节点数。</summary>
    internal static int CountDirectSubfolders(MangaAgent.Core.Library.FolderNode n)
        => n.Children.Count(c => !IsPureImageFolder(c));

    /// <summary>递归漫画总数 X：本目录直接（图片 FolderBook + Zip）+ 所有子节点递归。</summary>
    internal static int CountBooksRecursive(MangaAgent.Core.Library.FolderNode n)
    {
        int count = n.Books.Count(b => b.Kind is BookKind.Folder or BookKind.Zip);
        foreach (var c in n.Children) count += CountBooksRecursive(c);
        return count;
    }

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
    private async Task Rename()
    {
        if (SourcePath.Length == 0) return;
        // v3 #2：await 对话框（ShowDialog 异步不阻塞，原同步写法拿不到新名称 → 重命名无效）
        var newName = await MangaAgent.App.Views.RenameDialog.ShowAsync(DisplayName);
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
        // v3 #3：异步执行（原同步 File.Copy 大文件夹会卡 UI）+ 失败显示原因
        var (ok, error) = await Task.Run(() => MangaAgent.Core.Library.FileOps.CopyTo(SourcePath, dest));
        if (ok) RefreshAction?.Invoke();
        else NotifyError?.Invoke($"复制失败：{error}");
        global::MangaAgent.App.App.SLog($"COPY {SourcePath} -> {dest} ok={ok} err={error}");
    }

    [RelayCommand]
    private async Task MoveToAsync()
    {
        if (SourcePath.Length == 0 || PickTargetFolderAction is null) return;
        var dest = await PickTargetFolderAction();
        if (string.IsNullOrEmpty(dest)) return;
        // v3 #3：异步执行 + 跨卷回退复制删源（FileOps.MoveTo）+ 失败显示原因
        var (ok, error) = await Task.Run(() => MangaAgent.Core.Library.FileOps.MoveTo(SourcePath, dest));
        if (ok) RefreshAction?.Invoke();
        else NotifyError?.Invoke($"移动失败：{error}");
        global::MangaAgent.App.App.SLog($"MOVE {SourcePath} -> {dest} ok={ok} err={error}");
    }
}
