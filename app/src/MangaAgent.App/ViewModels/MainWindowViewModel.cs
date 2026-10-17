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
    /// <summary>数据根目录（v3 测试隔离：可覆盖，同 SettingsStore.BaseDir 模式）。</summary>
    public static string AppData { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HachimiManga");
    public static string LibrariesFile => Path.Combine(AppData, "libraries.txt");
    public static string LibraryDbPath => Path.Combine(AppData, "library.db");
    public static string StateDbPath => Path.Combine(AppData, "reading.db");

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
    /// <summary>详情模式标识（内容区切换：详情 ListBox vs 卡片 ListBox）。</summary>
    public bool IsDetailView => GalleryMode == GalleryMode.Detail;

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
        OnPropertyChanged(nameof(IsDetailView));
        // 重建条目以应用新尺寸
        if (CurrentFolder is not null) ShowFolderContents(CurrentFolder);
    }

    /// <summary>封面墙卡片尺寸（随展示模式变化）。</summary>
    public double GalleryItemWidth => GalleryMode switch
    {
        GalleryMode.Large => 248,   // = 卡片宽240 + Margin4×2
        GalleryMode.Small => 128,
        GalleryMode.Detail => 560,  // 详情：行式条宽
        _ => 176,
    };
    public double GalleryItemHeight => GalleryMode switch
    {
        GalleryMode.Large => 388,   // = 卡片高380 + Margin4×2
        GalleryMode.Small => 204,
        GalleryMode.Detail => 40,  // 详情：行式条高
        _ => 280,
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
    partial void OnSelectedLibraryChanged(LibraryItemViewModel? value)
    {
        if (value is null) return;
        ShowBooks(value);
        // 库切换（资料库下拉选择）：重建该库文件夹树 + 目录浏览
        _ = RebuildTreeForLibraryAsync(value);
    }

    /// <summary>库切换：扫描库根建树 → 显示根目录内容。</summary>
    private async Task RebuildTreeForLibraryAsync(LibraryItemViewModel lib)
    {
        try
        {
            var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(lib.Path);
            FolderTree.Clear();
            FolderTree.Add(tree);
            ShowFolderContents(tree);
        }
        catch (Exception ex)
        {
            App.SLog($"LIB SWITCH FAIL {lib.Path}: {ex.Message}");
        }
    }

    /// <summary>清空所有资料库：移除库列表 + 树/目录/面包屑 + 清空 libraries.txt。</summary>
    [RelayCommand]
    private async Task ClearLibraries()
    {
        App.SLog("CLEAR_TRY");
        if (!await MangaAgent.App.Views.ConfirmDialog.ShowAsync("确定清空所有资料库？")) return;
        Libraries.Clear();
        FolderTree.Clear();
        FolderEntries.Clear();
        Breadcrumbs.Clear();
        SearchText = string.Empty;
        SelectedLibrary = null;
        CurrentFolder = null;
        try { File.WriteAllText(LibrariesFile, string.Empty); }
        catch (Exception ex) { App.SLog($"CLEAR LIB FAIL: {ex.Message}"); }
        StatusText = "已清空资料库";
        App.SLog("CLEAR_LIBRARIES");
    }

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
        // 默认排序：文件夹在前 → 再按名称（用户要求）
        var sorted = FolderEntries.OrderByDescending(e => e.IsFolder)
                                  .ThenBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
                                  .ToList();
        FolderEntries.Clear();
        foreach (var s in sorted) FolderEntries.Add(s);
        // 懒加载可见封面（前 24 个条目；漫画 + 有自定义封面的文件夹）
        foreach (var item in FolderEntries.Take(24)) _ = item.LoadThumbnailAsync();
        // v4.3：文件夹自定义封面（一次批量查询 FolderCovers 后注入各文件夹条目）
        var folderPaths = FolderEntries.Where(e => e.IsFolder && e.Folder is not null)
            .Select(e => e.Folder!.Path).ToList();
        if (folderPaths.Count > 0)
        {
            try
            {
                using var coverState = new MangaAgent.Core.Storage.ReadingStateStore(StateDbPath);
                var covers = coverState.LoadFolderCovers(folderPaths);
                foreach (var e in FolderEntries.Where(e => e.IsFolder && e.Folder is not null))
                    if (covers.TryGetValue(e.Folder!.Path, out var rel))
                        e.SetFolderCover(e.Folder!.Path, rel);
            }
            catch { /* 封面记录读取失败不阻塞目录浏览 */ }
        }

        // 面包屑
        Breadcrumbs.Clear();
        foreach (var n in folder.PathChain) Breadcrumbs.Add(n);
        // 侧边栏树选中同步（避免递归：SelectedFolder 已在同一值时不触发变化）
        SelectedFolder = folder;
    }

    [ObservableProperty] private MangaAgent.Core.Library.FolderNode? _currentFolder;
    /// <summary>全库搜索激活中（目录浏览被搜索结果替换；清空搜索或点文件夹条目时退出）。</summary>
    private bool _searchActive;

    /// <summary>返回上级文件夹（面包屑 ⬆ 按钮）。</summary>
    [RelayCommand]
    private void GoUp()
    {
        if (CurrentFolder?.Parent is { } parent) { PushHistory(); _forwardStack.Clear(); ShowFolderContents(parent); }
    }

    // ---------- v3 #4：多选快捷键（复制/剪切/粘贴/删除 + 鼠标侧键导航历史） ----------
    private readonly Stack<MangaAgent.Core.Library.FolderNode> _backStack = new();
    private readonly Stack<MangaAgent.Core.Library.FolderNode> _forwardStack = new();
    private List<string> _clipboardPaths = new();
    private bool _clipboardCut;

    [RelayCommand]
    private void EnterFolder(MangaAgent.Core.Library.FolderNode? folder)
    {
        if (folder is null) return;
        if (!ReferenceEquals(folder, CurrentFolder)) { PushHistory(); _forwardStack.Clear(); }
        ShowFolderContents(folder);
    }

    private void PushHistory()
    {
        if (CurrentFolder is { } cur)
        {
            _backStack.Push(cur);
            if (_backStack.Count > 200) _backStack.Pop();
        }
    }

    /// <summary>后退（鼠标侧键/快捷键）。历史节点可能是旧树快照 → 回退后重扫反映最新文件系统。</summary>
    [RelayCommand]
    private async Task GoBack()
    {
        if (_backStack.Count == 0 || CurrentFolder is null) return;
        _forwardStack.Push(CurrentFolder);
        ShowFolderContents(_backStack.Pop());
        await RefreshCurrentViewAsync();
    }

    /// <summary>前进（鼠标侧键/快捷键）。同上，回退/前进后重扫。</summary>
    [RelayCommand]
    private async Task GoForward()
    {
        if (_forwardStack.Count == 0 || CurrentFolder is null) return;
        _backStack.Push(CurrentFolder);
        ShowFolderContents(_forwardStack.Pop());
        await RefreshCurrentViewAsync();
    }

    /// <summary>复制选中项到内部剪贴板（路径列表，粘贴时复制）。</summary>
    public void CopyToClipboard(IEnumerable<string> paths)
    {
        _clipboardPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        _clipboardCut = false;
        StatusText = $"已复制 {_clipboardPaths.Count} 项";
        App.SLog($"CLIPBOARD COPY {_clipboardPaths.Count} items");
    }

    /// <summary>剪切选中项（粘贴时移动）。</summary>
    public void CutToClipboard(IEnumerable<string> paths)
    {
        _clipboardPaths = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        _clipboardCut = true;
        StatusText = $"已剪切 {_clipboardPaths.Count} 项";
        App.SLog($"CLIPBOARD CUT {_clipboardPaths.Count} items");
    }

    /// <summary>粘贴到当前目录（复制或移动，UniquePath 防覆盖）。返回成功数。</summary>
    public async Task<int> PasteClipboardAsync()
    {
        if (CurrentFolder is null || _clipboardPaths.Count == 0) return 0;
        int total = _clipboardPaths.Count, done = 0;
        foreach (var p in _clipboardPaths.ToList())
        {
            var (ok, error) = await Task.Run(() => _clipboardCut
                ? MangaAgent.Core.Library.FileOps.MoveTo(p, CurrentFolder.Path)
                : MangaAgent.Core.Library.FileOps.CopyTo(p, CurrentFolder.Path));
            if (ok) done++;
            else App.SLog($"PASTE FAIL {p}: {error}");
        }
        if (done > 0) await RefreshCurrentViewAsync();
        StatusText = $"粘贴完成：{done}/{total} 项";
        _clipboardPaths.Clear();
        _clipboardCut = false;
        return done;
    }

    /// <summary>批量删除到回收站。返回成功数。</summary>
    public int DeletePaths(IEnumerable<string> paths)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        int done = 0;
        foreach (var p in list)
            if (MangaAgent.Core.Library.FileOps.DeleteToRecycleBin(p)) done++;
        if (done > 0) _ = RefreshCurrentViewAsync();
        StatusText = $"已删除 {done}/{list.Count} 项（回收站）";
        return done;
    }

    /// <summary>新建文件夹（v3 #6）：弹对话框输入名称 → 在当前目录创建 → 刷新并保持位置。
    /// v3 #16 修复：CurrentFolder 为 null（启动未进入目录/树未加载）时不再静默 return——
    /// 有库则用库根作为创建目标，无库则明确提示。
    /// v3 #2 修复：await 对话框（ShowDialog 异步不阻塞，原同步写法拿不到输入 → 从未创建）。</summary>
    [RelayCommand]
    private async Task NewFolder()
    {
        var target = CurrentFolder;
        if (target is null)
        {
            if (FolderTree.Count > 0 && FolderTree[0] is { } root)
                target = root;   // 未进入目录：在库根创建
            else
            {
                StatusText = "请先添加资料库";
                App.SLog("NEWFOLDER no library");
                return;
            }
        }
        var name = await MangaAgent.App.Views.NewFolderDialog.ShowAsync();
        if (string.IsNullOrWhiteSpace(name)) return;
        StatusText = NewFolderCore(target.Path, name);
        if (StatusText.StartsWith("已创建"))
            _ = RefreshCurrentViewAsync();
    }

    /// <summary>新建文件夹纯逻辑（测试入口）：返回状态消息。</summary>
    public string NewFolderCore(string parentDir, string name)
    {
        try
        {
            var target = Path.Combine(parentDir, name);
            if (Directory.Exists(target) || File.Exists(target)) return $"同名条目已存在：{name}";
            Directory.CreateDirectory(target);
            App.SLog($"NEWFOLDER {target}");
            return $"已创建：{name}";
        }
        catch (Exception ex)
        {
            App.SLog($"NEWFOLDER FAIL {parentDir}\\{name}: {ex.Message}");
            return $"创建失败：{ex.Message}";
        }
    }

    /// <summary>重建当前库树并保持当前目录位置（新建/删除文件夹后调用，避免跳到库根）。
    /// v3 小改：公开为 RefreshCurrentViewAsync，供删除/移动/复制/新建统一自动刷新（原 RefreshAction 用旧树快照重建 → 文件系统已变但节点不变，界面"不刷新"）。
    /// 库根取 CurrentFolder.PathChain[0]，不依赖 SelectedLibrary（测试/未选库场景也可用）。</summary>
    public async Task RefreshCurrentViewAsync()
    {
        if (CurrentFolder is null) return;
        try
        {
            var libRoot = CurrentFolder.PathChain[0].Path;
            var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(libRoot);
            FolderTree.Clear();
            FolderTree.Add(tree);
            if (_searchActive)
            {
                // 搜索态：基于新树重算命中
                ApplyFilter();
                return;
            }
            // 沿当前路径（根后各级名称）找新节点
            var names = CurrentFolder.PathChain.Select(n => n.Name).Skip(1).ToList();
            var node = tree;
            foreach (var n in names)
            {
                node = node.Children.FirstOrDefault(c => c.Name == n);
                if (node is null) break;
            }
            if (node is not null) ShowFolderContents(node);
        }
        catch (Exception ex)
        {
            App.SLog($"REFRESH_TREE_KEEP FAIL: {ex.Message}");
        }
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
            // 卡片 = 封面(0.7 单行本比例) + 文本区；详情 = 行式条
            GalleryMode.Large => (240.0, 380.0),
            GalleryMode.Small => (120.0, 196.0),
            GalleryMode.Detail => (560.0, 40.0),
            _ => (168.0, 272.0),
        };
        // 封面统一 0.7 比例（单行本：新书判148×210≈0.705 / B6 182×257≈0.708），三模式等比 ×1.5
        (item.CoverWidth, item.CoverHeight) = GalleryMode switch
        {
            GalleryMode.Large => (216.0, 309.0),
            GalleryMode.Small => (96.0, 137.0),
            GalleryMode.Detail => (0.0, 0.0),
            _ => (144.0, 206.0),
        };
        item.IsDetail = GalleryMode == GalleryMode.Detail;
        item.TitleFontSize = GalleryMode switch { GalleryMode.Small => 11, GalleryMode.Large => 13, _ => 12 };
        item.SubtitleFontSize = GalleryMode switch { GalleryMode.Small => 9, _ => 10 };
        item.OpenAction = e =>
        {
            if (e.IsFolder)
            {
                if (_searchActive) SearchText = string.Empty;   // 搜索态点文件夹 → 退出搜索并进入
                EnterFolderCommand.Execute(e.Folder);
            }
            else if (e.Book is { } book) OpenBookCommand.Execute(new BookItemViewModel(book));
        };
        item.RefreshAction = () => _ = RefreshCurrentViewAsync();
        // v3 #3：复制/移动失败 → 弹窗 + 状态栏（不再静默）
        item.NotifyError = msg =>
        {
            StatusText = msg;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => MangaAgent.App.Views.AlertDialog.Show(msg));
        };
        // v3 #1：复制/移动进度 → 状态栏文本（Progress.Report 已回 UI 线程）
        item.ProgressAction = msg => StatusText = msg;
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
        // v4.4 文件夹自定义封面区尺寸（与漫画封面同宽、略矮给三行文本留空间；空隙统一 11px）
        (item.FolderCoverW, item.FolderCoverH) = GalleryMode switch
        {
            GalleryMode.Large => (216.0, 281.0),
            GalleryMode.Small => (96.0, 118.0),
            GalleryMode.Detail => (0.0, 0.0),
            _ => (144.0, 183.0),
        };
        // v4.4 封面解码 DPI（主窗口缩放系数；headless 无窗口 = 1.0）
        item.CoverDpi = GetOwnerWindow?.Invoke()?.RenderScaling ?? 1.0;
        return item;
    }

    /// <summary>v4.3 多选批量复制到指定目录（右键"复制到…"）。返回成功数。</summary>
    public async Task<int> CopyPathsToAsync(IReadOnlyList<string> paths, string destDir)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (list.Count == 0) return 0;
        int done = 0, fail = 0;
        for (int i = 0; i < list.Count; i++)
        {
            StatusText = $"正在复制 {i + 1}/{list.Count}…";
            var (ok, err) = await Task.Run(() => MangaAgent.Core.Library.FileOps.CopyToAsync(list[i], destDir));
            if (ok) done++;
            else { fail++; App.SLog($"COPY FAIL {list[i]}: {err}"); }
        }
        if (done > 0) await RefreshCurrentViewAsync();
        StatusText = fail == 0 ? $"已复制 {done} 项" : $"复制完成：{done}/{list.Count} 项（失败 {fail}）";
        return done;
    }

    /// <summary>v4.3 多选批量移动到指定目录（右键"移动到…"）。返回成功数。</summary>
    public async Task<int> MovePathsToAsync(IReadOnlyList<string> paths, string destDir)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (list.Count == 0) return 0;
        int done = 0, fail = 0;
        for (int i = 0; i < list.Count; i++)
        {
            StatusText = $"正在移动 {i + 1}/{list.Count}…";
            var (ok, err) = await Task.Run(() => MangaAgent.Core.Library.FileOps.MoveToAsync(list[i], destDir));
            if (ok) done++;
            else { fail++; App.SLog($"MOVE FAIL {list[i]}: {err}"); }
        }
        if (done > 0) await RefreshCurrentViewAsync();
        StatusText = fail == 0 ? $"已移动 {done} 项" : $"移动完成：{done}/{list.Count} 项（失败 {fail}）";
        return done;
    }

    /// <summary>v4.3 为文件夹设置自定义封面：系统文件选择器（初始目录=该文件夹）→ 校验必须在该文件夹内 → 只存相对路径记录。</summary>
    public async Task SetFolderCoverAsync(FolderEntryItemViewModel entry)
    {
        if (entry.Folder is null) return;
        var window = GetOwnerWindow?.Invoke();
        var top = window is not null ? TopLevel.GetTopLevel(window) : null;
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文件夹封面图片（限该文件夹内）",
            AllowMultiple = false,
            SuggestedStartLocation = await top.StorageProvider.TryGetFolderFromPathAsync(entry.Folder.Path),
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
        });
        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            StatusText = "无法解析所选文件路径";
            return;
        }
        var rel = Path.GetRelativePath(entry.Folder.Path, path);
        using var state = new MangaAgent.Core.Storage.ReadingStateStore(StateDbPath);
        if (!state.SetFolderCover(entry.Folder.Path, rel))
        {
            StatusText = "封面无效：必须选择该文件夹内的图片文件";
            MangaAgent.App.Views.AlertDialog.Show("封面无效：必须选择该文件夹内的图片文件");
            return;
        }
        entry.SetFolderCover(entry.Folder.Path, rel);
        StatusText = "已设置文件夹封面";
        App.SLog($"FOLDER_COVER SET {entry.Folder.Path} -> {rel}");
    }

    /// <summary>v4.3 清除文件夹封面（恢复 📁 原样式）。</summary>
    public void ClearFolderCover(FolderEntryItemViewModel entry)
    {
        if (entry.Folder is null) return;
        using var state = new MangaAgent.Core.Storage.ReadingStateStore(StateDbPath);
        state.ClearFolderCover(entry.Folder.Path);
        entry.ClearFolderCover();
        StatusText = "已清除文件夹封面";
        App.SLog($"FOLDER_COVER CLEAR {entry.Folder.Path}");
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
        // v3：封面墙/库视图操作后同样重建树刷新（原 hack：SelectedFolder=SelectedFolder 不触发真实重扫）
        item.RefreshAction = () => _ = RefreshCurrentViewAsync();
        return item;
    }

    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? string.Empty;
        if (q.Length == 0)
        {
            // 退出搜索态 → 恢复目录浏览（当前目录；无则库根）
            _searchActive = false;
            if (CurrentFolder is not null) ShowFolderContents(CurrentFolder);
            else if (FolderTree.Count > 0) ShowFolderContents(FolderTree[0]);
            return;
        }
        // 全库搜索：遍历当前库整棵树，命中文件夹 + 漫画（含纯图片文件夹漫画）
        _searchActive = true;
        var hits = new List<FolderEntryItemViewModel>();
        if (FolderTree.Count > 0) CollectSearchHits(FolderTree[0], q, hits);
        // 默认排序：文件夹在前 → 名称
        FolderEntries.Clear();
        foreach (var h in hits.OrderByDescending(x => x.IsFolder)
                              .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
            FolderEntries.Add(h);
        foreach (var item in FolderEntries.Where(e => !e.IsFolder).Take(24)) _ = item.LoadThumbnailAsync();
    }

    /// <summary>递归收集命中（从库根调用，根自身不作为匹配对象）：
    /// ①纯图片节点的 FolderBook + 所有 Zip = 漫画（按标题/系列）；
    /// ②非纯图片子文件夹按名匹配 = 文件夹（其内部图片漫画不单独命中，避免同名重复）。</summary>
    private void CollectSearchHits(MangaAgent.Core.Library.FolderNode node, string q, List<FolderEntryItemViewModel> hits)
    {
        bool nodeIsPure = node.Children.Count == 0 && node.Books.Any(b => b.Kind == BookKind.Folder);
        foreach (var b in node.Books)
        {
            // v4-PDF：文件型书（CBZ/PDF）全参与命中；FolderBook 仅纯图片节点命中
            bool include = b.IsFileKind || (b.Kind == BookKind.Folder && nodeIsPure);
            if (include && (b.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (b.Series?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)))
                hits.Add(WireEntry(new FolderEntryItemViewModel(b)));
        }
        foreach (var child in node.Children)
        {
            bool childIsPure = child.Children.Count == 0 && child.Books.Any(x => x.Kind == BookKind.Folder);
            if (!childIsPure && child.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                hits.Add(WireEntry(new FolderEntryItemViewModel(child)));
            CollectSearchHits(child, q, hits);
        }
    }

    [RelayCommand]
    public void OpenBook(BookItemViewModel? book)
    {
        if (book is null) return;
        try
        {
            // v5.2：打开书前注入设置的"默认阅读模式/续读"
            var s = global::MangaAgent.App.Services.SettingsStore.Load();
            ReaderViewModel.DefaultMode = s.DefaultMode switch
            {
                "Double" => ViewMode.Double,
                "Webtoon" => ViewMode.Webtoon,
                _ => ViewMode.Single,
            };
            // v3 #2b：默认方向（无保存状态时生效）
            ReaderViewModel.DefaultDirection = s.DefaultDirection == "Ltr"
                ? MangaAgent.Core.Storage.ReadingDirection2.Ltr
                : MangaAgent.Core.Storage.ReadingDirection2.Rtl;
            ReaderViewModel.ResumePosition = s.ResumePosition;
            ReaderViewModel.PdfCropDefault = s.PdfCropDefault;   // v4.1-PDF：裁切默认（仅 PDF 生效）
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
