using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MangaAgent.App.Services;
using MangaAgent.Core.Library;

namespace MangaAgent.App.ViewModels;

public partial class BookItemViewModel : ViewModelBase
{
    public BookInfo Book { get; }

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public string DisplayName => Book.DisplayName;
    public string Subtitle => $"{Book.PageCount} 页 · {(Book.Kind == BookKind.Zip ? "CBZ" : "文件夹")}";

    public BookItemViewModel(BookInfo book) => Book = book;

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail is null)
            Thumbnail = await ThumbnailService.GetThumbnailAsync(Book);
    }
}
