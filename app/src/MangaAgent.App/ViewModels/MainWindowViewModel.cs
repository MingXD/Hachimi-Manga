using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaAgent.App.Views;
using MangaAgent.App.Services;
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

    /// <summary>v4.21：阅读进度/书签的共享服务（库内 progress.json；库外路径与只读库回退 reading.db）。
    /// 由主窗口创建、所有阅读器共用；关窗时 Flush + Dispose。</summary>
    public LibraryStateService StateService { get; }

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
        GalleryMode.Large => 388,   // = 卡片高380 + Margin4×2（v4.7 恢复 v2.3.8 原值）
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
        StateService = new LibraryStateService(StateDbPath);
        StateService.OnWarning = msg => Avalonia.Threading.Dispatcher.UIThread.Post(() => StatusText = msg);
        _ = LoadSavedLibrariesAsync();
    }

    /// <summary>v4.21：主窗口关闭前调用 —— 把节流中的进度/书签强制落盘并释放 db 连接。</summary>
    public void ShutdownStateService()
    {
        try { StateService.Flush(); } catch { }
        try { StateService.Dispose(); } catch { }
    }

    /// <summary>v4.21：低频兜底落盘（主窗口定时器每 3 秒调用一次），避免异常退出丢最近页码。</summary>
    public void FlushStateIfDirty()
    {
        try { StateService.FlushIfDirty(); } catch { }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedLibraryChanged(LibraryItemViewModel? value)
    {
        if (value is null) return;
        if (!value.IsValid)
        {
            // v4.20：失效库不建树（否则扫描抛错），提示用"关联"按钮重新指定位置
            ShowBooks(value);
            FolderTree.Clear();
            FolderEntries.Clear();
            Breadcrumbs.Clear();
            CurrentFolder = null;
            StatusText = $"资料库“{value.Name}”路径失效（库可能被改名/移动）：点左侧“关联”按钮选择新位置";
            return;
        }
        _ = ShowLibraryAsync(value);
    }

    /// <summary>切换/加载某库：更新封面与书列表 → 扫描库根建树 → 显示根目录内容（失效库直接跳过）。</summary>
    private async Task ShowLibraryAsync(LibraryItemViewModel lib)
    {
        ShowBooks(lib);
        if (!lib.IsValid)
        {
            StatusText = $"资料库“{lib.Name}”路径失效：点左侧“关联”按钮选择新位置";
            return;
        }
        try
        {
            var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(lib.Path);
            FolderTree.Clear();
            FolderTree.Add(tree);
            ShowFolderContents(tree);
        }
        catch (Exception ex)
        {
            StatusText = $"打开资料库失败：{ex.Message}";
            App.SLog($"LIB SWITCH FAIL {lib.Path}: {ex.Message}");
        }
    }

    /// <summary>v4.20：重新关联资料库（库被改名/移动后，把库条目指到新路径，无需清空重建）。
    /// 库列表与 libraries.txt 同步更新；封面/进度随库目录自动恢复（covers.json 就在库里）。</summary>
    public async Task<bool> RelinkLibraryAsync(LibraryItemViewModel lib, string newPath)
    {
        if (lib is null) return false;
        if (string.IsNullOrWhiteSpace(newPath)) return false;
        var full = newPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(full))
        {
            StatusText = $"所选目录不存在：{full}";
            return false;
        }
        if (Libraries.Any(l => !ReferenceEquals(l, lib) && l.Path.Equals(full, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = "该目录已在资料库列表中，无需重复关联";
            return false;
        }
        var old = lib.Path;
        lib.UpdatePath(full);
        SyncLibraryRootsFromLibraries();
        await RewriteLibrariesFileAsync();
        App.SLog($"LIB_RELINK {old} -> {full}");
        if (!ReferenceEquals(SelectedLibrary, lib)) SelectedLibrary = lib;   // 触发正常切换流程（含建树）
        else await ShowLibraryAsync(lib);                                    // 已选中该库：手动重建
        StatusText = $"已重新关联资料库：{lib.Name}（{full}）";
        return true;
    }

    /// <summary>v4.20：用"库列表"整体重写 libraries.txt（增删改统一走这里，避免追加式写入留下旧行）。</summary>
    private async Task RewriteLibrariesFileAsync()
    {
        try
        {
            await File.WriteAllLinesAsync(LibrariesFile,
                Libraries.Select(l => l.Path).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) { App.SLog($"LIB_FILE WRITE FAIL: {ex.Message}"); }
    }

    /// <summary>v4.20："关联"按钮：为当前选中的失效库选择新位置。</summary>
    [RelayCommand]
    private async Task RelinkLibrary()
    {
        var lib = SelectedLibrary;
        if (lib is null)
        {
            StatusText = "请先在左侧选择一个资料库";
            return;
        }
        if (lib.IsValid)
        {
            StatusText = $"当前资料库路径正常，无需重新关联：{lib.Path}";
            return;
        }
        var window = GetOwnerWindow?.Invoke();
        var top = window is not null ? TopLevel.GetTopLevel(window) : null;
        if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"选择资料库“{lib.Name}”的新位置",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
        {
            StatusText = "无法解析所选路径（可能含特殊字符或不可访问）";
            return;
        }
        await RelinkLibraryAsync(lib, path);
    }

    /// <summary>清空所有资料库：移除库列表 + 树/目录/面包屑 + 清空 libraries.txt。
    /// v4.20：同时重置内存库根列表（否则下次添加库时封面读取仍用旧库根 → 首次加库看不到封面）。</summary>
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
        MangaAgent.Core.Storage.ReadingStateStore.SetLibraryRoots(Array.Empty<string>());
        _coverConfigs.Clear();
        await RewriteLibrariesFileAsync();
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
        SyncLibraryRootsFromLibraries();   // v4.20：渲染前先同步库根（否则刚添加的库读不到封面）
        CurrentFolder = folder;
        FolderEntries.Clear();
        // BUG-D v1.9（v4.18 细化）：子文件夹判定 —— 有子文件夹 → 文件夹条目；
        // 单行本文件夹（无子文件夹且直接装图片，FolderNode.IsSingleVolumeFolder）→ 折叠为漫画条目；
        // 其余（装 zip/pdf 的分类文件夹，如 "[nemuke？ (似せ)]"）必须保持文件夹身份，
        // 否则会被误渲染成一本漫画（书名取 Books[0]），用户看不到文件夹本身，
        // 对那张卡"移动到该文件夹"还会因源=目标目录而失败。
        foreach (var child in folder.Children)
        {
            if (child.IsSingleVolumeFolder)
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
        // v4.20：文件夹自定义封面 —— 优先库里 covers.json（随库走），db 里迁移失败的旧记录作回退
        ApplyFolderCovers(FolderEntries.Where(e => e.IsFolder && e.Folder is not null).ToList());

        // 面包屑
        Breadcrumbs.Clear();
        foreach (var n in folder.PathChain) Breadcrumbs.Add(n);
        // 侧边栏树选中同步（避免递归：SelectedFolder 已在同一值时不触发变化）
        SelectedFolder = folder;
        // v4.19：记录"已渲染时的目录时间戳"（窗口激活时据此判断外部改动）；并挂上当前目录监听（P3）
        _shownFolderMtime = DirMtime(folder.Path);
        _shownRootMtime = FolderTree.Count > 0 ? DirMtime(FolderTree[0].Path) : DateTime.MinValue;
        // 目录监听只在有窗口时启用（headless 测试/无窗口场景不挂 watcher，避免测试环境干扰）
        if (GetOwnerWindow is not null)
            _watcher.Watch(folder.Path, () => _ = RefreshCurrentFolderAsync());
        // v4.19：文件型书页数后台懒补（扫描不再打开压缩包）
        _ = EnrichPageCountsAsync();
    }

    // ---------- v4.19 自动更新机制（P1 浅刷新 / P2 导航+激活 / P3 当前目录 watcher） ----------
    private readonly FolderWatcher _watcher = new();
    private DateTime _shownFolderMtime = DateTime.MinValue;
    private DateTime _shownRootMtime = DateTime.MinValue;
    private int _enrichVersion;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // ---------- v4.20 库内封面配置（covers.json，随库移动/改名） ----------
    private readonly Dictionary<string, MangaAgent.Core.Storage.FolderCoverConfig> _coverConfigs =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _coverFallbackWarned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>绝对路径 → 所属库根（最长匹配；无库根返回 false，例如库外临时目录）。</summary>
    private static bool TryResolveLibraryRoot(string absPath, out string root)
    {
        var roots = CandidateRoots(absPath);
        root = roots.Count > 0 ? roots[0] : "";
        return roots.Count > 0;
    }

    /// <summary>v4.20：某路径的全部候选库根（按具体程度 长→短 排序）。
    /// 嵌套库根（如 bz 与 bz\25.08 同时是库）时，读取按"最具体 → 最外层"逐级回退。</summary>
    private static List<string> CandidateRoots(string absPath)
    {
        var list = new List<string>();
        string full;
        try { full = Path.GetFullPath(absPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return list; }
        foreach (var r in MangaAgent.Core.Storage.ReadingStateStore.LibraryRoots.OrderByDescending(x => x.Length))
        {
            if (string.IsNullOrEmpty(r)) continue;
            if (full.Equals(r, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                list.Add(r);
        }
        return list;
    }

    /// <summary>取（并缓存）某库根的封面配置实例。</summary>
    private MangaAgent.Core.Storage.FolderCoverConfig CoverConfigFor(string root)
    {
        if (_coverConfigs.TryGetValue(root, out var cfg)) return cfg;
        cfg = MangaAgent.Core.Storage.FolderCoverConfig.Load(root);
        _coverConfigs[root] = cfg;
        return cfg;
    }

    /// <summary>v4.20（bug 修复）：把"当前库列表"同步进 <see cref="MangaAgent.Core.Storage.ReadingStateStore.LibraryRoots"/>。
    /// 封面的相对路径解析完全依赖这个库根列表；若"先渲染、后更新库根"（添加库时的调用顺序），
    /// 新加的库会把封面全部读不到（用户实测：清空后第一次加库没封面、第二次才好）。渲染前调用即可根治。</summary>
    private void SyncLibraryRootsFromLibraries()
    {
        var roots = Libraries.Select(l => l.Path).ToList();
        var current = MangaAgent.Core.Storage.ReadingStateStore.LibraryRoots;
        if (roots.Count == current.Count && roots.SequenceEqual(current, StringComparer.OrdinalIgnoreCase)) return;
        try
        {
            MangaAgent.Core.Storage.ReadingStateStore.SetLibraryRoots(roots);
            _coverConfigs.Clear();   // 库根变化 → 丢弃封面配置缓存
            StateService.InvalidateCache();   // v4.21：进度配置缓存同理
            App.SLog($"SYNC_LIB_ROOTS {roots.Count} 个：{string.Join(" | ", roots)}");
        }
        catch (Exception ex) { App.SLog($"SYNC_LIB_ROOTS FAIL: {ex.Message}"); }
    }

    /// <summary>v4.20：把封面注入文件夹条目 —— 按"最具体 → 最外层"库根的 covers.json 逐级查找，谁有键用谁。</summary>
    private void ApplyFolderCovers(List<FolderEntryItemViewModel> folderEntries)
    {
        foreach (var e in folderEntries)
        {
            foreach (var root in CandidateRoots(e.Folder!.Path))
            {
                try
                {
                    if (CoverConfigFor(root).GetCoverFullPath(e.Folder.Path) is { } coverAbs)
                    {
                        e.SetFolderCoverPath(coverAbs);
                        break;
                    }
                }
                catch (Exception ex) { App.SLog($"COVER_CFG READ FAIL {root}: {ex.Message}"); }
            }
        }
    }

    /// <summary>库根不可写时提示一次（每库仅一次，避免刷状态栏）。</summary>
    private void WarnCoverWriteFailed(string root, string? error)
    {
        App.SLog($"COVER_CFG WRITE FAIL {root}: {error}");
        if (!_coverFallbackWarned.Add(root)) return;
        StatusText = $"该库无法写入 covers.json（{error}）：请检查目录只读属性或权限";
    }

    private static DateTime DirMtime(string path)
    {
        try { return Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path) : DateTime.MinValue; }
        catch { return DateTime.MinValue; }
    }

    /// <summary>当前目录浅刷新并重渲染（watcher/导航/操作后统一入口）。</summary>
    public async Task RefreshCurrentFolderAsync()
    {
        if (CurrentFolder is null) return;
        await RefreshNodeAndRenderAsync(CurrentFolder);
    }

    /// <summary>浅刷新指定节点（只列目录，毫秒级）→ 重渲染当前视图。
    /// 并发策略（v4.19）：用 <see cref="SemaphoreSlim"/> 串行化 —— 与"排队丢弃"不同，
    /// 每个调用方都会等到自己的刷新真正完成（粘贴/移动后 await 即可断言结果），
    /// 连续操作只会多跑几次毫秒级刷新，不会丢刷新。</summary>
    private async Task RefreshNodeAndRenderAsync(MangaAgent.Core.Library.FolderNode node)
    {
        await _refreshLock.WaitAsync();
        try
        {
            await MangaAgent.Core.Library.FolderTreeScanner.RefreshShallowAsync(node);
            if (_searchActive) { ApplyFilter(); return; }
            if (CurrentFolder is not null && ReferenceEquals(CurrentFolder, node))
                ShowFolderContents(node);
            else
                _shownFolderMtime = DirMtime(node.Path);   // 非当前层：仅更新缓存时间戳
        }
        catch (Exception ex)
        {
            App.SLog($"REFRESH_SHALLOW FAIL {node.Path}: {ex.Message}");
        }
        finally { _refreshLock.Release(); }
    }

    /// <summary>窗口重新激活（P2②）：当前目录或库根的时间戳变了（外部新建/移动/删除）→ 浅刷新。
    /// 目录的 LastWriteTime 在"子项增删改名"时会变化，所以这次检查天然便宜且够用。</summary>
    public async Task RefreshIfStaleAsync()
    {
        if (CurrentFolder is null) return;
        var folder = CurrentFolder;
        var root = FolderTree.Count > 0 ? FolderTree[0] : null;
        bool folderChanged = DirMtime(folder.Path) != _shownFolderMtime;
        bool rootChanged = root is not null && !ReferenceEquals(root, folder) && DirMtime(root.Path) != _shownRootMtime;
        if (!folderChanged && !rootChanged) return;
        App.SLog($"REFRESH_STALE folder={folderChanged} root={rootChanged} {folder.Path}");
        try
        {
            if (rootChanged && root is not null)
            {
                await MangaAgent.Core.Library.FolderTreeScanner.RefreshShallowAsync(root);
                _shownRootMtime = DirMtime(root.Path);
            }
            await MangaAgent.Core.Library.FolderTreeScanner.RefreshShallowAsync(folder);
            if (!_searchActive && ReferenceEquals(CurrentFolder, folder)) ShowFolderContents(folder);
            else _shownFolderMtime = DirMtime(folder.Path);
        }
        catch (Exception ex) { App.SLog($"REFRESH_STALE FAIL {folder.Path}: {ex.Message}"); }
    }

    /// <summary>v4.19：为当前显示的文件型书后台补页数（每本只读一次索引，不阻塞界面）。
    /// 打开的压缩包失败（损坏/加密）则保持 "… 页"。</summary>
    public async Task EnrichPageCountsAsync(CancellationToken ct = default)
    {
        var version = ++_enrichVersion;
        var targets = FolderEntries
            .Where(e => !e.IsFolder && e.Book is { } b && b.IsFileKind && e.ResolvedPageCount <= 0)
            .Take(200).ToList();
        if (targets.Count == 0) return;
        foreach (var item in targets)
        {
            if (ct.IsCancellationRequested || version != _enrichVersion) return;
            var book = item.Book;
            if (book is null) continue;
            int pages = 0;
            try
            {
                // 开包读索引放到线程池（ZipArchive 初始化/中央目录读取可能耗时，不能占 UI 线程）
                pages = await Task.Run(async () =>
                {
                    await using var src = global::MangaAgent.Core.Comics.ComicSourceFactory.Open(book.Path);
                    return src.PageCount;
                }, ct);
            }
            catch { continue; }
            if (pages <= 0) continue;
            if (version != _enrichVersion) return;
            var captured = pages;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != _enrichVersion) return;
                item.SetPageCount(captured);
                // 同步节点记录：重渲染/排序时也保留已解析页数
                if (CurrentFolder is { } cur)
                    for (int i = 0; i < cur.Books.Count; i++)
                    {
                        var b = cur.Books[i];
                        if (!ReferenceEquals(b, book) && !b.Path.Equals(book.Path, StringComparison.OrdinalIgnoreCase)) continue;
                        cur.Books[i] = b with { PageCount = captured };
                        break;
                    }
            });
        }
    }

    /// <summary>v4.19：关窗/切换库时释放目录监听。</summary>
    public void StopWatching() => _watcher.Dispose();

    /// <summary>v4.19：窗口就绪后补挂当前目录监听（首次渲染可能早于 GetOwnerWindow 注入）。</summary>
    public void RefreshWatcherTarget()
    {
        if (GetOwnerWindow is null || CurrentFolder is null) return;
        _watcher.Watch(CurrentFolder.Path, () => _ = RefreshCurrentFolderAsync());
        _ = RefreshIfStaleAsync();   // 顺带校准一次"窗口打开时的外部改动"
    }

    [ObservableProperty] private MangaAgent.Core.Library.FolderNode? _currentFolder;
    /// <summary>全库搜索激活中（目录浏览被搜索结果替换；清空搜索或点文件夹条目时退出）。</summary>
    private bool _searchActive;

    /// <summary>返回上级文件夹（面包屑 ⬆ 按钮）。</summary>
    [RelayCommand]
    private void GoUp()
    {
        if (CurrentFolder?.Parent is { } parent)
        {
            PushHistory();
            _forwardStack.Clear();
            ShowFolderContents(parent);
            _ = RefreshNodeAndRenderAsync(parent);   // v4.19：返回上级也浅刷新（兄弟目录可能已变化）
        }
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
        _ = RefreshNodeAndRenderAsync(folder);   // v4.19：进入目录即浅刷新（外部新建/移动进来的内容马上可见）
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

    /// <summary>批量删除到回收站。返回成功数。v4.21：同时清理被删项的封面配置键与阅读进度/书签键。</summary>
    public int DeletePaths(IEnumerable<string> paths)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        int done = 0;
        foreach (var p in list)
        {
            if (!MangaAgent.Core.Library.FileOps.DeleteToRecycleBin(p)) continue;
            done++;
            CleanStoredKeysFor(p);
        }
        if (done > 0) _ = RefreshCurrentViewAsync();
        StatusText = $"已删除 {done}/{list.Count} 项（回收站）";
        return done;
    }

    /// <summary>v4.21：删除后清理该路径（或该文件夹）在库配置里的封面键与进度/书签键。</summary>
    private void CleanStoredKeysFor(string path)
    {
        try
        {
            foreach (var root in CandidateRoots(path))
            {
                var cfg = CoverConfigFor(root);
                if (cfg.TryClear(path) && !cfg.Flush()) WarnCoverWriteFailed(root, cfg.LastError);
            }
            StateService.ClearBook(path);
        }
        catch (Exception ex) { App.SLog($"CLEAN_KEYS FAIL {path}: {ex.Message}"); }
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

    /// <summary>重建当前视图（v4.19：改为"浅刷新" —— 只重列当前目录一层 + 直接子目录的书，毫秒级；
    /// 不再重建整棵库树、也不再打开压缩包读页数）。删除/移动/复制/新建后统一调用。
    /// 若当前目录已被外部移走/删除 → 回退到最近仍存在的祖先（不再残留幽灵目录）。</summary>
    public async Task RefreshCurrentViewAsync()
    {
        var node = CurrentFolder;
        if (node is null) return;
        if (!Directory.Exists(node.Path))
        {
            var fallback = node.Parent;
            while (fallback is not null && !Directory.Exists(fallback.Path)) fallback = fallback.Parent;
            if (fallback is null) return;
            App.SLog($"REFRESH_FALLBACK {node.Path} -> {fallback.Path}");
            ShowFolderContents(fallback);
            node = fallback;
        }
        await RefreshNodeAndRenderAsync(node);
        _shownRootMtime = FolderTree.Count > 0 ? DirMtime(FolderTree[0].Path) : DateTime.MinValue;
    }

    /// <summary>面包屑跳转。</summary>
    [RelayCommand]
    private void NavigateBreadcrumb(MangaAgent.Core.Library.FolderNode? folder)
    {
        if (folder is null) return;
        ShowFolderContents(folder);
        _ = RefreshNodeAndRenderAsync(folder);   // v4.19：进入即浅刷新（外部新内容立即可见）
    }

    private FolderEntryItemViewModel WireEntry(FolderEntryItemViewModel item)
    {
        // 功能-1：卡片尺寸随展示模式（v4.7：恢复 v2.3.8 原始卡片高度；文件夹封面同尺寸+两行合并文本放得下）
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
        // v4.5 文件夹自定义封面区 = 漫画封面同尺寸（216×309 / 144×206 / 96×137）
        (item.FolderCoverW, item.FolderCoverH) = GalleryMode switch
        {
            GalleryMode.Large => (216.0, 309.0),
            GalleryMode.Small => (96.0, 137.0),
            GalleryMode.Detail => (0.0, 0.0),
            _ => (144.0, 206.0),
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

    /// <summary>v4.3 多选批量移动到指定目录（右键"移动到…"）。返回成功数。
    /// v4.12：目录移动成功后迁移自定义封面记录（FolderPath 旧键 → 实际目标路径）。</summary>
    public async Task<int> MovePathsToAsync(IReadOnlyList<string> paths, string destDir)
    {
        var list = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (list.Count == 0) return 0;
        int done = 0, fail = 0;
        for (int i = 0; i < list.Count; i++)
        {
            StatusText = $"正在移动 {i + 1}/{list.Count}…";
            // v4.20：移动前快照库内封面记录（移动后源路径已不存在，取不到旧值）
            var coverSnapshot = SnapshotCoverForMove(list[i]);
            var (ok, err, targetPath) = await Task.Run(() => MangaAgent.Core.Library.FileOps.MoveToAsync(list[i], destDir));
            if (ok)
            {
                done++;
                if (targetPath is not null && Directory.Exists(targetPath))
                    ApplyCoverAfterMove(coverSnapshot, list[i], targetPath);   // v4.20：库内 covers.json 改键
            }
            else { fail++; App.SLog($"MOVE FAIL {list[i]}: {err}"); }
        }
        if (done > 0) await RefreshCurrentViewAsync();
        StatusText = fail == 0 ? $"已移动 {done} 项" : $"移动完成：{done}/{list.Count} 项（失败 {fail}）";
        return done;
    }

    /// <summary>v4.20：移动前捕获库内封面快照（库根 + 配置键 + 封面相对库根路径）。</summary>
    private (string Root, string Key, string Rel)? SnapshotCoverForMove(string srcDir)
    {
        try
        {
            if (!Directory.Exists(srcDir)) return null;
            if (!TryResolveLibraryRoot(srcDir, out var root)) return null;
            var cfg = CoverConfigFor(root);
            if (!cfg.TrySnapshot(srcDir, out var key, out var rel)) return null;
            return (root, key, rel);
        }
        catch { return null; }
    }

    /// <summary>v4.20：目录移动后改封面键 —— 目标端写"最具体"库根；源端清掉所有候选库根里的旧键
    /// （封面若随目录移动，值的前缀替换为目标键；否则封面在库内别处，值不变）。</summary>
    private void ApplyCoverAfterMove((string Root, string Key, string Rel)? snapshot, string srcDir, string dstDir)
    {
        if (snapshot is not { } snap) return;
        try
        {
            // 目标端：写到"最具体"库根（读时逐级回退，父库根无需重复写）
            var dstRoots = CandidateRoots(dstDir);
            if (dstRoots.Count > 0)
            {
                var dstCfg = CoverConfigFor(dstRoots[0]);
                if (dstCfg.TryKeyFor(dstDir, out var dstKey))
                {
                    var newRel = snap.Rel;
                    if (snap.Rel.Equals(snap.Key, StringComparison.OrdinalIgnoreCase)
                        || snap.Rel.StartsWith(snap.Key + "/", StringComparison.OrdinalIgnoreCase))
                        newRel = dstKey + snap.Rel.Substring(snap.Key.Length);
                    if (dstCfg.TrySetByRel(dstKey, newRel, out var err))
                    {
                        if (!dstCfg.Flush()) WarnCoverWriteFailed(dstRoots[0], dstCfg.LastError);
                    }
                    else App.SLog($"COVER_MOVE FAIL {srcDir} -> {dstDir}: {err}");
                }
            }
        }
        catch (Exception ex) { App.SLog($"COVER_MOVE FAIL {srcDir} -> {dstDir}: {ex.Message}"); }
        finally
        {
            // 源端：所有候选库根都清掉旧键（含父库根，避免残留让封面"留在原位置"）
            foreach (var root in CandidateRoots(srcDir))
            {
                try
                {
                    var cfg = CoverConfigFor(root);
                    if (cfg.TryClear(srcDir) && !cfg.Flush()) WarnCoverWriteFailed(root, cfg.LastError);
                }
                catch (Exception ex) { App.SLog($"COVER_MOVE CLEAR FAIL {root}: {ex.Message}"); }
            }
        }
    }

    /// <summary>v4.20 为文件夹设置自定义封面：写进该库根的 covers.json（随库移动/改名）；库不可写时回退 reading.db。
    /// 封面图片可放在库内任意位置（相对库根记录），不再限制必须在该文件夹内。</summary>
    public async Task SetFolderCoverAsync(FolderEntryItemViewModel entry)
    {
        if (entry.Folder is null) return;
        var window = GetOwnerWindow?.Invoke();
        var top = window is not null ? TopLevel.GetTopLevel(window) : null;
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文件夹封面图片（须位于资料库内）",
            AllowMultiple = false,
            SuggestedStartLocation = await top.StorageProvider.TryGetFolderFromPathAsync(entry.Folder.Path),
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
        });
        if (files.Count == 0) return;
        var chosen = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(chosen))
        {
            StatusText = "无法解析所选文件路径";
            return;
        }
        var folderPath = entry.Folder.Path;
        var roots = CandidateRoots(folderPath);
        if (roots.Count == 0)
        {
            StatusText = "该文件夹不属于任何资料库：请先把它所在目录添加为资料库";
            MangaAgent.App.Views.AlertDialog.Show("该文件夹不属于任何资料库，无法保存封面配置。请先添加其所在目录为资料库。");
            return;
        }
        var root = roots[0];   // 写"最具体"库根；读取时会逐级回退
        var cfg = CoverConfigFor(root);
        if (!cfg.TrySet(folderPath, chosen, out var err))
        {
            StatusText = $"封面无效：{err}";
            MangaAgent.App.Views.AlertDialog.Show($"封面无效：{err}");
            return;
        }
        if (!cfg.Flush())
        {
            WarnCoverWriteFailed(root, cfg.LastError);
            StatusText = $"保存封面失败：无法写入 {cfg.FilePath}（{cfg.LastError}）";
            return;
        }
        entry.SetFolderCoverPath(chosen);
        StatusText = "已设置文件夹封面（写入库内 covers.json）";
        App.SLog($"FOLDER_COVER SET {folderPath} -> {chosen} (covers.json)");
    }

    /// <summary>v4.20 清除文件夹封面：清掉所有候选库根里的同键（否则父库根残留会让封面"清不掉"）。</summary>
    public void ClearFolderCover(FolderEntryItemViewModel entry)
    {
        if (entry.Folder is null) return;
        var folderPath = entry.Folder.Path;
        var roots = CandidateRoots(folderPath);
        if (roots.Count == 0) { entry.ClearFolderCover(); return; }
        bool anyFailed = false;
        foreach (var root in roots)
        {
            try
            {
                var cfg = CoverConfigFor(root);
                cfg.TryClear(folderPath);
                if (!cfg.Flush()) { WarnCoverWriteFailed(root, cfg.LastError); anyFailed = true; }
            }
            catch (Exception ex) { App.SLog($"COVER_CLEAR FAIL {root}: {ex.Message}"); anyFailed = true; }
        }
        entry.ClearFolderCover();
        StatusText = anyFailed ? "已清除界面封面，但部分库配置写入失败（见状态提示/日志）" : "已清除文件夹封面";
        App.SLog($"FOLDER_COVER CLEAR {folderPath}");
    }

    private async Task LoadSavedLibrariesAsync()
    {
        if (!File.Exists(LibrariesFile)) return;
        int missing = 0;
        foreach (var line in await File.ReadAllLinesAsync(LibrariesFile))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var path = line.Trim();
            bool exists = Directory.Exists(path);
            try
            {
                // v4.20：失效路径也加入列表（标记 IsValid=false），供"关联"按钮重新指定位置
                var books = exists ? await LibraryScanner.ScanAsync(path) : new List<MangaAgent.Core.Library.BookInfo>();
                var lib = new LibraryItemViewModel(path, books);
                Libraries.Add(lib);
                if (!exists)
                {
                    missing++;
                    App.SLog($"LOAD_LIB MISSING {path}（可在下拉选中后点“关联”重新指定位置）");
                }
            }
            catch (Exception ex)
            {
                // 启动加载失败不得静默：记日志（用户可从调试面板/日志看到哪个库失败）
                App.SLog($"LOAD_LIB FAIL {path}: {ex.Message}");
            }
        }
        if (missing > 0)
            StatusText = $"有 {missing} 个资料库路径失效（可能被移动/改名）：在下拉里选中它并点“关联”按钮重新指定新位置";
        var firstValid = Libraries.FirstOrDefault(l => l.IsValid);
        if (firstValid is not null)
        {
            SelectedLibrary = firstValid;
            // BUG-1：启动加载历史库后也建文件夹树（否则重启左侧树空白）
            try
            {
                var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(firstValid.Path);
                FolderTree.Clear();
                FolderTree.Add(tree);
                SelectedFolder = tree;
            }
            catch { /* 建树失败不阻塞启动 */ }
        }
        // v4.13：路径以库根相对存储 —— 设置库根并把旧版本绝对路径记录迁移为相对（幂等）
        RefreshLibraryRootsAndMigrate();
    }

    /// <summary>v4.13：更新 ReadingStateStore 的库根列表，并把库根下旧绝对路径记录迁移为相对（幂等）。
    /// v4.20：封面已改为库内 covers.json；这里只把历史 FolderCovers 表备份成 JSON 后删除（一次性，幂等）。</summary>
    private void RefreshLibraryRootsAndMigrate()
    {
        try
        {
            MangaAgent.Core.Storage.ReadingStateStore.SetLibraryRoots(Libraries.Select(l => l.Path));
            using var db = new MangaAgent.Core.Storage.ReadingStateStore(StateDbPath);
            db.MigratePathsToRelative();
        }
        catch (Exception ex)
        {
            App.SLog($"LIB_ROOTS FAIL: {ex.Message}");
        }
        _coverConfigs.Clear();   // 库根可能变化 → 丢弃旧的配置缓存（下次读取按 mtime 重新加载）
        StateService.InvalidateCache();   // v4.21：进度配置缓存同理
        try
        {
            var backup = MangaAgent.Core.Storage.ReadingStateStore.BackupLegacyFolderCovers(StateDbPath, AppData);
            if (backup is not null) App.SLog($"LEGACY_COVER_BACKUP {backup}");
        }
        catch (Exception ex) { App.SLog($"LEGACY_COVER_BACKUP FAIL: {ex.Message}"); }
        // v4.21：一次性把 reading.db 里的阅读进度/书签搬进各库 progress.json（幂等，之后 db 只做回退）
        try
        {
            var (migrated, kept) = MangaAgent.Core.Storage.LibraryStateService.MigrateFromDatabase(StateDbPath);
            if (migrated > 0 || kept > 0) App.SLog($"STATE_MIGRATE {migrated} 条 → progress.json（保留 {kept} 条在 db）");
        }
        catch (Exception ex) { App.SLog($"STATE_MIGRATE FAIL: {ex.Message}"); }
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
            RefreshLibraryRootsAndMigrate();   // v4.20 bug 修复：先同步库根，再选中/建树/渲染（否则封面读不到）
            SelectedLibrary = lib;
            // 构建文件夹树
            var tree = await MangaAgent.Core.Library.FolderTreeScanner.ScanAsync(path);
            FolderTree.Clear();
            FolderTree.Add(tree);
            SelectedFolder = tree;
            StatusText = $"已添加：{lib.Name}（{lib.Books.Count} 本）";
            await RewriteLibrariesFileAsync();   // v4.20：用库列表整体重写（增删改统一入口）
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
        bool nodeIsPure = node.IsSingleVolumeFolder;
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
            bool childIsPure = child.IsSingleVolumeFolder;
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
            var vm = new ReaderViewModel(book.Book, StateService);   // v4.21：共享进度服务（库内 progress.json）
            new ReaderWindow { DataContext = vm }.Show();
        }
        catch (Exception ex)
        {
            global::MangaAgent.App.App.SLog("OpenBook EXCEPTION: " + ex);
        }
    }

    public static string DbPath => LibraryDbPath;
}
