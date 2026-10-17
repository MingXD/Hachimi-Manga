using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaAgent.App.Services;
using MangaAgent.Core.Library;

namespace MangaAgent.App.ViewModels;

public partial class BookItemViewModel : ViewModelBase
{
    public BookInfo Book { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>打开阅读器（由 MainWindow 注入）。</summary>
    public Action<BookItemViewModel>? OpenAction { get; set; }

    /// <summary>通知刷新（文件操作后重建列表）。</summary>
    public Action? RefreshAction { get; set; }

    public string DisplayName => Book.DisplayName;
    public string Subtitle => $"{Book.PageCount} 页 · {Book.KindLabel}";

    public BookItemViewModel(BookInfo book) => Book = book;

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail is null)
            Thumbnail = await ThumbnailService.GetThumbnailAsync(Book, 216, 309, 1.0);   // 大图模式封面框
    }

    [RelayCommand]
    private void Open() => OpenAction?.Invoke(this);

    [RelayCommand]
    private void ShowInExplorer() => FileOps.ShowInExplorer(Book.Path);

    [RelayCommand]
    private void Delete()
    {
        var ok = FileOps.DeleteToRecycleBin(Book.Path);
        if (ok) RefreshAction?.Invoke();
    }

    [RelayCommand]
    private async Task Rename()
    {
        // v3 #2：await 对话框（ShowDialog 异步不阻塞，原同步写法拿不到新名称 → 重命名无效）
        var newName = await MangaAgent.App.Views.RenameDialog.ShowAsync(Book.DisplayName);
        if (string.IsNullOrWhiteSpace(newName)) return;
        try
        {
            var ext = Path.GetExtension(Book.Path);
            // v4-PDF：文件型书（Zip/PDF）重命名保留扩展名（文件夹书无扩展名概念）
            FileOps.Rename(Book.Path,
                newName + (Book.IsFileKind && !newName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? ext : ""));
            RefreshAction?.Invoke();
        }
        catch { /* 重命名失败忽略（后续提示） */ }
    }

    [RelayCommand]
    private void Properties() => MangaAgent.App.Views.PropertiesDialog.Show(Book);
}

