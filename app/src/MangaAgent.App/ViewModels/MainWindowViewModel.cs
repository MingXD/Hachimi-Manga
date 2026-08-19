using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaAgent.App.Views;
using MangaAgent.Core.Library;
using Avalonia.Threading;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private static readonly string AppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MangaAgent");
    private static readonly string LibrariesFile = Path.Combine(AppData, "libraries.txt");
    private static readonly string LibraryDbPath = Path.Combine(AppData, "library.db");
    private static readonly string StateDbPath = Path.Combine(AppData, "reading.db");

    public ObservableCollection<LibraryItemViewModel> Libraries { get; } = new();
    public ObservableCollection<BookItemViewModel> Books { get; } = new();
    public ObservableCollection<MangaAgent.Core.Library.FolderNode> FolderTree { get; } = new();

    [ObservableProperty] private LibraryItemViewModel? _selectedLibrary;
    [ObservableProperty] private MangaAgent.Core.Library.FolderNode? _selectedFolder;
    [ObservableProperty] private BookItemViewModel? _selectedBook;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isScanning;

    /// <summary>由 MainWindow 注入：返回当前窗口（用于文件夹选择器等）。</summary>
    public Func<Window>? GetOwnerWindow { get; set; }

    public MainWindowViewModel()
    {
        Directory.CreateDirectory(AppData);
        _ = LoadSavedLibrariesAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLibraryChanged(LibraryItemViewModel? value) { if (value is not null) ShowBooks(value); }

    partial void OnSelectedFolderChanged(MangaAgent.Core.Library.FolderNode? value)
    {
        if (value is null) return;
        Books.Clear();
        foreach (var b in value.Books) Books.Add(new BookItemViewModel(b));
        foreach (var item in Books.Take(24)) _ = item.LoadThumbnailAsync();
    }

    private async Task LoadSavedLibrariesAsync()
    {
        if (!File.Exists(LibrariesFile)) return;
        foreach (var line in await File.ReadAllLinesAsync(LibrariesFile))
        {
            if (string.IsNullOrWhiteSpace(line) || !Directory.Exists(line)) continue;
            try
            {
                var lib = new LibraryItemViewModel(line, await LibraryScanner.ScanAsync(line));
                Libraries.Add(lib);
            }
            catch { /* 跳过不可读的库 */ }
        }
        if (Libraries.Count > 0) SelectedLibrary = Libraries[0];
    }

    [RelayCommand]
    private async Task AddLibraryAsync()
    {
        var window = GetOwnerWindow?.Invoke();
        var top = window is not null ? TopLevel.GetTopLevel(window) : null;
        if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择漫画资料库文件夹",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path) || Libraries.Any(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;

        IsScanning = true;
        StatusText = "正在扫描…";
        try
        {
            var lib = new LibraryItemViewModel(path, await LibraryScanner.ScanAsync(path));
            Libraries.Add(lib);
            SelectedLibrary = lib;
            // 构建文件夹树
            var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(path);
            FolderTree.Clear();
            FolderTree.Add(tree);
            SelectedFolder = tree;
            StatusText = $"已添加：{lib.Name}（{lib.Books.Count} 本）";
            // 持久化库根
            var existing = File.Exists(LibrariesFile) ? await File.ReadAllLinesAsync(LibrariesFile) : Array.Empty<string>();
            await File.WriteAllLinesAsync(LibrariesFile, existing.Append(path).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        if (SelectedLibrary is null) return;
        IsScanning = true;
        StatusText = "正在重新扫描…";
        try
        {
            var lib = new LibraryItemViewModel(SelectedLibrary.Path, await LibraryScanner.ScanAsync(SelectedLibrary.Path));
            int idx = Libraries.IndexOf(SelectedLibrary);
            if (idx >= 0) Libraries[idx] = lib;
            SelectedLibrary = lib;
            StatusText = $"扫描完成：{lib.Books.Count} 本";
        }
        finally { IsScanning = false; }
    }

    private void ShowBooks(LibraryItemViewModel lib)
    {
        Books.Clear();
        foreach (var b in lib.Books) Books.Add(Wire(new BookItemViewModel(b)));
        ApplyFilter();
        foreach (var item in Books.Take(24)) _ = item.LoadThumbnailAsync();
    }

    private BookItemViewModel Wire(BookItemViewModel item)
    {
        item.OpenAction = b => OpenBookCommand.Execute(b);
        item.RefreshAction = () => Dispatcher.UIThread.Post(() =>
        {
            if (SelectedFolder is not null) SelectedFolder = SelectedFolder;   // 触发刷新
            else RescanCommand.Execute(null);
        });
        return item;
    }

    private void ApplyFilter()
    {
        if (SelectedLibrary is null) return;
        var q = SearchText?.Trim() ?? string.Empty;
        Books.Clear();
        foreach (var b in SelectedLibrary.Books)
        {
            if (q.Length == 0 || b.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (b.Series?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                Books.Add(Wire(new BookItemViewModel(b)));
        }
        foreach (var item in Books.Take(24)) _ = item.LoadThumbnailAsync();
    }

    [RelayCommand]
    public void OpenBook(BookItemViewModel? book)
    {
        if (book is null) return;
        try
        {
            var vm = new ReaderViewModel(book.Book, StateDbPath);
            new ReaderWindow { DataContext = vm }.Show();
        }
        catch (Exception ex)
        {
            global::MangaAgent.App.App.SLog("OpenBook EXCEPTION: " + ex);
        }
    }

    public static string DbPath => LibraryDbPath;
}
