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
    public string Subtitle => $"{Book.PageCount} 页 · {(Book.Kind == BookKind.Zip ? "CBZ" : "文件夹")}";

    public BookItemViewModel(BookInfo book) => Book = book;

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail is null)
            Thumbnail = await ThumbnailService.GetThumbnailAsync(Book);
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
    private void Rename()
    {
        var newName = MangaAgent.App.Views.RenameDialog.Show(Book.DisplayName);
        if (string.IsNullOrWhiteSpace(newName)) return;
        try
        {
            var ext = Path.GetExtension(Book.Path);
            FileOps.Rename(Book.Path, newName + (Book.Kind == BookKind.Zip && !newName.EndsWith(ext) ? ext : ""));
            RefreshAction?.Invoke();
        }
        catch { /* 重命名失败忽略（后续提示） */ }
    }

    [RelayCommand]
    private void Properties() => MangaAgent.App.Views.PropertiesDialog.Show(Book);
}

