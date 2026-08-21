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

public enum GalleryMode { Large, Medium, Small, Detail }

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
    /// <summary>目录浏览器：当前层级的混合条目（子文件夹 + 漫画书）。</summary>
    public ObservableCollection<FolderEntryItemViewModel> FolderEntries { get; } = new();
    /// <summary>目录浏览器：面包屑（根 → 当前）。</summary>
    public ObservableCollection<MangaAgent.Core.Library.FolderNode> Breadcrumbs { get; } = new();

    [ObservableProperty] private LibraryItemViewModel? _selectedLibrary;
    [ObservableProperty] private MangaAgent.Core.Library.FolderNode? _selectedFolder;
    [ObservableProperty] private BookItemViewModel? _selectedBook;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusText = "就绪";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private GalleryMode _galleryMode = GalleryMode.Medium;

    /// <summary>展示模式按钮文案（功能-1 v1.9）。</summary>
    public string GalleryModeText => GalleryMode switch
    {
        GalleryMode.Large => "展示:大图",
        GalleryMode.Small => "展示:小图",
        GalleryMode.Detail => "展示:详情",
        _ => "展示:中图",
    };

    partial void OnGalleryModeChanged(GalleryMode value)
    {
        OnPropertyChanged(nameof(GalleryModeText));
        OnPropertyChanged(nameof(GalleryItemWidth));
        OnPropertyChanged(nameof(GalleryItemHeight));
        // 重建条目以应用新尺寸
        if (CurrentFolder is not null) ShowFolderContents(CurrentFolder);
    }

    /// <summary>封面墙卡片尺寸（随展示模式变化）。</summary>
    public double GalleryItemWidth => GalleryMode switch
    {
        GalleryMode.Large => 264,
        GalleryMode.Small => 132,
        GalleryMode.Detail => 0,   // 详情用行式列表，不走 WrapPanel
        _ => 184,
    };
    public double GalleryItemHeight => GalleryMode switch
    {
        GalleryMode.Large => 366,
        GalleryMode.Small => 188,
        GalleryMode.Detail => 0,
        _ => 262,
    };

    [RelayCommand]
    private void CycleGalleryMode()
        => GalleryMode = (GalleryMode)(((int)GalleryMode + 1) % 4);

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
        if (value is not null) ShowFolderContents(value);
    }

    /// <summary>目录浏览器：展示指定文件夹的混合内容（子文件夹在前 + 漫画书），同步面包屑与侧边栏选中。</summary>
    public void ShowFolderContents(MangaAgent.Core.Library.FolderNode folder)
    {
        CurrentFolder = folder;
        FolderEntries.Clear();
        // BUG-D v1.9：子文件夹判定 —— 有子文件夹 → 文件夹条目；无子文件夹但有书（纯图片叶子）→ 漫画条目
        foreach (var child in folder.Children)
        {
            if (child.Children.Count == 0 && child.Books.Count > 0)
                FolderEntries.Add(WireEntry(new FolderEntryItemViewModel(child.Books[0])));
            else
                FolderEntries.Add(WireEntry(new FolderEntryItemViewModel(child)));
        }
        foreach (var b in folder.Books)
            FolderEntries.Add(WireEntry(new FolderEntryItemViewModel(b)));
        // 懒加载可见封面（前 24 个漫画条目）
        foreach (var item in FolderEntries.Where(e => !e.IsFolder).Take(24)) _ = item.LoadThumbnailAsync();

        // 面包屑
        Breadcrumbs.Clear();
        foreach (var n in folder.PathChain) Breadcrumbs.Add(n);
        // 侧边栏树选中同步（避免递归：SelectedFolder 已在同一值时不触发变化）
        SelectedFolder = folder;
    }

    [ObservableProperty] private MangaAgent.Core.Library.FolderNode? _currentFolder;

    /// <summary>进入子文件夹（双击文件夹条目）。</summary>
    [RelayCommand]
    private void EnterFolder(MangaAgent.Core.Library.FolderNode? folder)
    {
        if (folder is not null) ShowFolderContents(folder);
    }

    /// <summary>返回上级文件夹（面包屑 ⬆ 按钮）。</summary>
    [RelayCommand]
    private void GoUp()
    {
        if (CurrentFolder?.Parent is { } parent) ShowFolderContents(parent);
    }

    /// <summary>面包屑跳转。</summary>
    [RelayCommand]
    private void NavigateBreadcrumb(MangaAgent.Core.Library.FolderNode? folder)
    {
        if (folder is not null) ShowFolderContents(folder);
    }

    private FolderEntryItemViewModel WireEntry(FolderEntryItemViewModel item)
    {
        // 功能-1：卡片尺寸随展示模式
        (item.CardWidth, item.CardHeight) = GalleryMode switch
        {
            GalleryMode.Large => (248.0, 342.0),
            GalleryMode.Small => (116.0, 164.0),
            GalleryMode.Detail => (560.0, 40.0),
            _ => (168.0, 246.0),
        };
        item.OpenAction = e =>
        {
            if (e.IsFolder) EnterFolderCommand.Execute(e.Folder);
            else if (e.Book is { } book) OpenBookCommand.Execute(new BookItemViewModel(book));
        };
        item.RefreshAction = () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (CurrentFolder is not null) ShowFolderContents(CurrentFolder);
        });
        item.PickTargetFolderAction = async () =>
        {
            var window = GetOwnerWindow?.Invoke();
            var top = window is not null ? TopLevel.GetTopLevel(window) : null;
            if (top is null) return null;
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择目标文件夹（复制/移动到）",
                AllowMultiple = false,
            });
            return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        };
        return item;
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
            catch (Exception ex)
            {
                // 启动加载失败不得静默：记日志（用户可从调试面板/日志看到哪个库失败）
                App.SLog($"LOAD_LIB FAIL {line}: {ex.Message}");
            }
        }
        if (Libraries.Count > 0)
        {
            SelectedLibrary = Libraries[0];
            // BUG-1：启动加载历史库后也建文件夹树（否则重启左侧树空白）
            try
            {
                var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(Libraries[0].Path);
                FolderTree.Clear();
                FolderTree.Add(tree);
                SelectedFolder = tree;
            }
            catch { /* 建树失败不阻塞启动 */ }
        }
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
        if (folders.Count == 0) { App.SLog("ADDLIB cancelled (no folder picked)"); return; }
        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            // 问题1 静默失败路径：TryGetLocalPath 对特殊字符/网络路径可能返回 null
            StatusText = "无法解析所选路径（可能含特殊字符或不可访问）";
            App.SLog($"ADDLIB FAIL: TryGetLocalPath returned null for {folders[0].Path}");
            return;
        }
        var existingLib = Libraries.FirstOrDefault(l => l.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existingLib is not null)
        {
            // 已在库中：不静默 return —— 切换到该库并显示内容（用户看到效果，不再误以为失败）
            StatusText = $"该目录已在库中，已切换：{existingLib.Name}";
            App.SLog($"ADDLIB already-in-library -> select {existingLib.Path}");
            SelectedLibrary = existingLib;
            // 确保树已建并选中根
            try
            {
                var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(existingLib.Path);
                FolderTree.Clear();
                FolderTree.Add(tree);
                ShowFolderContents(tree);
            }
            catch (Exception ex) { App.SLog($"ADDLIB select-tree fail {existingLib.Path}: {ex.Message}"); }
            return;
        }

        IsScanning = true;
        StatusText = "正在扫描…";
        try
        {
            // 扫描进度反馈（BUG-1：深层目录扫描慢，实时显示避免"无反应"）
            var lib = new LibraryItemViewModel(path, await Task.Run(async () =>
                await LibraryScanner.ScanAsync(path, progress: (dir, found) =>
                {
                    // 进度回调可能在线程池线程执行 → 切回 UI 线程更新状态
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        StatusText = $"正在扫描… 已发现 {found} 本（{Path.GetFileName(dir.TrimEnd('\\'))}）");
                })));
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
        catch (Exception ex)
        {
            // BUG-C v1.9：扫描异常不得静默失败 —— 显示错误 + 记日志
            StatusText = $"添加失败：{ex.Message}";
            global::MangaAgent.App.App.SLog($"ADDLIB FAIL {path}: {ex}");
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
            var lib = new LibraryItemViewModel(SelectedLibrary.Path, await Task.Run(async () =>
                await LibraryScanner.ScanAsync(SelectedLibrary.Path,
                    progress: (dir, found) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        StatusText = $"正在重新扫描… 已发现 {found} 本"))));
            int idx = Libraries.IndexOf(SelectedLibrary);
            if (idx >= 0) Libraries[idx] = lib;
            SelectedLibrary = lib;
            // BUG-1：重建文件夹树 + 选中根（此前只更新库列表 → 扫描后左侧空白）
            var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(SelectedLibrary.Path);
            FolderTree.Clear();
            FolderTree.Add(tree);
            ShowFolderContents(tree);
            StatusText = $"扫描完成：{lib.Books.Count} 本";
        }
        catch (Exception ex)
        {
            // BUG-C v1.9：扫描异常不得静默失败
            StatusText = $"重新扫描失败：{ex.Message}";
            global::MangaAgent.App.App.SLog($"RESCAN FAIL: {ex}");
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
