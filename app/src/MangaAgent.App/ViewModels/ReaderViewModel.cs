using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Paging;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.ViewModels;

public enum FitMode { FitWidth, FitHeight, FitScreen, Custom }

public enum ViewMode { Single, Double, Webtoon }

/// <summary>阅读器：逻辑页序列（PageLayouter）+ 当前页解码 + 续读/书签。</summary>
public partial class ReaderViewModel : ViewModelBase, IAsyncDisposable
{
    public BookInfo Book { get; }
    public string BookTitle => Book.DisplayName;

    private readonly ComicSource _source;
    private List<LogicalPage> _logical = new();
    private readonly Dictionary<int, PageSize> _sizes = new();

    /// <summary>双页配对起点（0=1-2 相接，1=2-3 相接）。</summary>
    [ObservableProperty]
    private int _pairOffset;

    public string PairText => PairOffset == 0 ? "配对起点1" : "配对起点2";
    private readonly ReadingStateStore _state;

    [ObservableProperty] private Bitmap? _image1;
    [ObservableProperty] private Bitmap? _image2;
    [ObservableProperty] private double _ratio1 = 0.7;
    [ObservableProperty] private double _ratio2 = 0.7;
    [ObservableProperty] private bool _isDoublePageShown;
    [ObservableProperty] private bool _isWebtoon;
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private int _totalLogical = 1;
    [ObservableProperty] private ViewMode _viewMode = ViewMode.Single;
    [ObservableProperty] private ReadingDirection2 _direction = ReadingDirection2.Rtl;
    [ObservableProperty] private bool _isDark = true;
    // 默认适应屏幕（v1.8：打开漫画/切模式默认 FitScreen，看到整页）
    [ObservableProperty] private FitMode _fitMode = FitMode.FitScreen;
    [ObservableProperty] private double _scale = 1.0;   // Custom 模式的倍率（1.0 = 适应宽度基准）
    [ObservableProperty] private string _pageText = "1 / 1";
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private ObservableCollection<int> _bookmarks = new();
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasStatus;

    // 页面位图 LRU 缓存（上限 CacheCap 页，淘汰时 Dispose，控制内存峰值 —— BUG-001 修复）
    private const int CacheCap = 6;
    private readonly Dictionary<int, Bitmap> _pageCache = new();
    private readonly LinkedList<int> _cacheOrder = new();
    // BUG-004：UI 引用计数 —— 位图同时被缓存与 UI Image 控件引用（缓存共享返回同一对象），
    // 缓存淘汰/重解码直接 Dispose 会让布局时 Image.MeasureOverride → Bitmap.get_Size() 抛
    // ObjectDisposedException（模式切换大量解码触发淘汰时闪退）。
    // 修复：被 UI 引用的位图延迟 Dispose，直到引用归零且不在缓存。
    private readonly Dictionary<Bitmap, int> _uiRefs = new();

    /// <summary>解码目标宽度（按显示尺寸降采样，4K 扫描页从 104MB 降到 ~10MB）。</summary>
    [ObservableProperty]
    private int _decodeTargetWidth = 2048;

    private int _loadSeq;
    /// <summary>布局版本：RebuildLayout/ToggleMode 等改变逻辑页结构时自增。
    /// LoadCurrentAsync 快照此版本，仅当版本过期才丢弃结果（BUG-3：此前对比 FirstIndex 会把
    /// 配对切换后的新布局误判为竞态而丢弃 → 画面不更新）。</summary>
    private int _layoutVersion;

    public string BackgroundHex => IsDark ? "#101014" : "#F3F1EC";
    public string ToolbarHex => IsDark ? "#1C1C24" : "#FFFFFF";
    public string ForegroundHex => IsDark ? "#E8E8E8" : "#1A1A1A";
    public string ModeText => ViewMode switch
    {
        ViewMode.Double => "双页",
        ViewMode.Webtoon => "滚动",
        _ => "单页",
    };
    public string DirectionText => Direction == ReadingDirection2.Rtl ? "右→左" : "左→右";
    public string ThemeText => IsDark ? "夜间" : "白天";
    public string FitText => FitMode switch
    {
        FitMode.FitWidth => "适应宽度",
        FitMode.FitHeight => "适应高度",
        FitMode.FitScreen => "适应屏幕",
        _ => $"{Scale * 100:F0}%",
    };

    public ReaderViewModel(BookInfo book, string stateDbPath)
    {
        Book = book;
        _state = new ReadingStateStore(stateDbPath);
        _source = ComicSourceFactory.Open(book.Path);
        LoadSizes();
        RebuildLayout();

        var saved = _state.LoadState(book.Path);
        _currentIndex = saved is null ? 0 : Math.Clamp(saved.Value.LogicalPage, 0, TotalLogical - 1);
        _viewMode = saved?.Mode switch
        {
            ReadingMode.Double => ViewMode.Double,
            _ => ViewMode.Single,
        };
        _direction = saved?.Direction ?? ReadingDirection2.Rtl;
        LoadBookmarks();
        _ = LoadCurrentAsync();
    }

    private void LoadSizes()
    {
        // 同步读取页尺寸（避免在 UI 线程阻塞异步续体导致死锁）
        for (int i = 0; i < _source.PageCount; i++)
        {
            var sz = _source.GetPageSize(i);
            if (sz is { } s) _sizes[i] = s;
        }
    }

    private void RebuildLayout()
    {
        _layoutVersion++;
        _logical = PageLayouter.Layout(
            Enumerable.Range(0, _source.PageCount)
                .Select(i => new PhysicalPageRef(i, _sizes.TryGetValue(i, out var sz) ? sz.AspectRatio : 0.7))
                .ToList(),
            new PageLayoutOptions { FirstPairOffset = PairOffset });
        TotalLogical = Math.Max(1, _logical.Count);
        if (CurrentIndex >= _logical.Count) CurrentIndex = Math.Max(0, _logical.Count - 1);
        _ = LoadCurrentAsync();
    }

    partial void OnPairOffsetChanged(int value)
    {
        OnPropertyChanged(nameof(PairText));
        RebuildLayout();
    }

    [RelayCommand]
    private void TogglePairOffset()
    {
        PairOffset = 1 - PairOffset;
        SaveState();
    }

    partial void OnCurrentIndexChanged(int value) => _ = LoadCurrentAsync();
    partial void OnViewModeChanged(ViewMode value)
    {
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(IsDoublePageShown));
        _ = LoadCurrentAsync();
    }
    partial void OnScaleChanged(double value)
    {
        // FitMode 由 CycleFit/Reset 或画布缩放（code-behind）管理；此处仅刷新显示
        OnPropertyChanged(nameof(FitText));
    }

    partial void OnFitModeChanged(FitMode value)
    {
        if (value != FitMode.Custom) Scale = 1.0;
        OnPropertyChanged(nameof(FitText));
    }

    [RelayCommand]
    private void CycleFit() =>
        FitMode = FitMode switch
        {
            FitMode.FitWidth => FitMode.FitHeight,
            FitMode.FitHeight => FitMode.FitScreen,
            FitMode.FitScreen => FitMode.FitWidth,
            _ => FitMode.FitWidth,
        };
    partial void OnIsDarkChanged(bool value)
    {
        OnPropertyChanged(nameof(BackgroundHex));
        OnPropertyChanged(nameof(ToolbarHex));
        OnPropertyChanged(nameof(ForegroundHex));
        OnPropertyChanged(nameof(ThemeText));
    }

    private async Task LoadCurrentAsync()
    {
        if (_logical.Count == 0) return;
        var mySeq = ++_loadSeq;
        var myLayout = _layoutVersion;
        var lp = _logical[CurrentIndex];
        var showDouble = ViewMode == ViewMode.Double && lp.IsDouble;

        var img1 = await DecodeAsync(lp.FirstIndex);
        Bitmap? img2 = null;
        if (showDouble && lp.SecondIndex >= 0)
            img2 = await DecodeAsync(lp.SecondIndex);

        // 竞态防护：期间用户已翻页/切换模式/重布局，则丢弃本次结果。
        // 注意：只对比"版本号/序号"，不再对比 FirstIndex —— 配对切换会重建布局
        // （同 CurrentIndex 的 FirstIndex 变化），对比 FirstIndex 会把新布局误杀（BUG-3）。
        if (mySeq != _loadSeq || myLayout != _layoutVersion || CurrentIndex >= _logical.Count)
        {
            ReleaseUiBitmap(img1);
            ReleaseUiBitmap(img2);
            return;
        }

        HasStatus = false;
        IsDoublePageShown = showDouble;
        // UI 引用计数（BUG-004）：DecodeAsync 返回时已 Acquire；这里只 Release 旧位图（替换瞬间不 Dispose）
        var old1 = Image1;
        var old2 = Image2;
        Image1 = img1;
        Ratio1 = RatioOf(lp.FirstIndex);
        Image2 = img2;
        if (img2 is not null) Ratio2 = RatioOf(lp.SecondIndex);
        ReleaseUiBitmap(old1);
        ReleaseUiBitmap(old2);
        PageText = $"{CurrentIndex + 1} / {TotalLogical}";
        ProgressValue = CurrentIndex + 1;
    }

    private double RatioOf(int physical) => _sizes.TryGetValue(physical, out var sz) ? sz.AspectRatio : 0.7;

    /// <summary>Webtoon：获取指定物理页位图（带缓存）。</summary>
    public Task<Bitmap?> GetPageBitmapAsync(int physical) => DecodeAsync(physical);

    /// <summary>Webtoon：页数。</summary>
    public int PhysicalPageCount => _source.PageCount;

    /// <summary>Webtoon：指定页宽高比。</summary>
    public double RatioOfPage(int physical) => RatioOf(physical);

    /// <summary>Webtoon：当前逻辑页对应的首个物理页索引（进入 Webtoon 时定位用）。</summary>
    public int CurrentFirstPhysical
    {
        get
        {
            if (_logical.Count == 0) return 0;
            var lp = _logical[Math.Clamp(CurrentIndex, 0, _logical.Count - 1)];
            return Math.Max(0, lp.FirstIndex);
        }
    }

    /// <summary>Webtoon：滚动时更新状态显示（只改 PageText，不动 CurrentIndex —— 避免每滚动一页触发重解码）。</summary>
    public void UpdateWebtoonPageText(int physical)
    {
        PageText = $"{Math.Clamp(physical + 1, 1, _source.PageCount)} / {_source.PageCount}";
    }

    private readonly Dictionary<int, int> _pageCacheWidth = new();

    private async Task<Bitmap?> DecodeAsync(int physical)
    {
        int wantWidth = Math.Max(400, DecodeTargetWidth);
        if (_pageCache.TryGetValue(physical, out var cached))
        {
            // 按需高清：放大超过当前解码宽度时重新解码更大（C 阶段）
            if (_pageCacheWidth.TryGetValue(physical, out var w) && w >= wantWidth)
            {
                _cacheOrder.Remove(physical);
                _cacheOrder.AddLast(physical);
                AcquireUiBitmap(cached);   // 返回前 Acquire：位图从缓存取出即受 UI 保护（BUG-004）
                return cached;
            }
            // 高清重解码：若 cached 仍被 UI（PageImage/Webtoon）引用，则复用不重解码——
            // 否则从缓存移除后若另一竞态路径 Release 使计数归零 → Dispose 掉仍被 UI 引用的位图 → 布局崩溃
            if (_uiRefs.ContainsKey(cached))
            {
                _cacheOrder.Remove(physical);
                _cacheOrder.AddLast(physical);
                return cached;
            }
            TryDispose(cached);
            _pageCache.Remove(physical);
            _pageCacheWidth.Remove(physical);
            _cacheOrder.Remove(physical);
        }

        Bitmap? decoded = null;
        string? error = null;
        try
        {
            using var stream = await _source.OpenPageAsync(physical);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            // 按显示尺寸降采样解码（BUG-001 根因修复：避免 4K 扫描页全尺寸解码 104MB）
            decoded = Bitmap.DecodeToWidth(ms, Math.Max(400, DecodeTargetWidth));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (decoded is null)
        {
            StatusText = $"无法解码第 {physical + 1} 页：{error}";
            HasStatus = true;
            return null;
        }

        HasStatus = false;
        _pageCache[physical] = decoded;
        _cacheOrder.AddLast(physical);
        EvictCache();
        AcquireUiBitmap(decoded);   // 返回前 Acquire（BUG-004）
        return decoded;
    }

    private void EvictCache()
    {
        while (_cacheOrder.Count > CacheCap)
        {
            var oldest = _cacheOrder.First!.Value;
            _cacheOrder.RemoveFirst();
            if (_pageCache.Remove(oldest, out var bmp))
                TryDispose(bmp);
        }
    }

    /// <summary>UI 取得位图引用（Image.Source 赋值前调用；缓存共享对象，计数防止被淘汰 Dispose）。</summary>
    public void AcquireUiBitmap(Bitmap? bmp)
    {
        if (bmp is null) return;
        _uiRefs.TryGetValue(bmp, out var n);
        _uiRefs[bmp] = n + 1;
    }

    /// <summary>UI 释放位图引用（Image.Source 替换/移除时调用）。引用归零且不在缓存 → Dispose。</summary>
    public void ReleaseUiBitmap(Bitmap? bmp)
    {
        if (bmp is null) return;
        if (!_uiRefs.TryGetValue(bmp, out var n)) return;
        if (n <= 1)
        {
            _uiRefs.Remove(bmp);
            TryDispose(bmp);
        }
        else _uiRefs[bmp] = n - 1;
    }

    /// <summary>安全释放：仍被 UI 引用或仍在缓存 → 不 Dispose（缓存淘汰/重解码用）。</summary>
    private void TryDispose(Bitmap bmp)
    {
        if (_uiRefs.ContainsKey(bmp)) return;
        if (_pageCache.ContainsValue(bmp)) return;
        bmp.Dispose();
    }

    private void SaveState() =>
        _state.SaveState(Book.Path, CurrentIndex,
            ViewMode == ViewMode.Double ? ReadingMode.Double : ReadingMode.Single, Direction);

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentIndex < TotalLogical - 1) { CurrentIndex++; global::MangaAgent.App.App.SLog($"PAGE -> {CurrentIndex + 1}/{TotalLogical}"); SaveState(); }
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (CurrentIndex > 0) { CurrentIndex--; global::MangaAgent.App.App.SLog($"PAGE -> {CurrentIndex + 1}/{TotalLogical}"); SaveState(); }
    }

    /// <summary>快速翻页：跳转到指定逻辑页（0-based），越界 clamp（长漫画几百页 → 快速定位）。</summary>
    [RelayCommand]
    private void JumpToPage(int page)
    {
        if (_logical.Count == 0) return;
        int target = Math.Clamp(page, 0, _logical.Count - 1);
        if (target != CurrentIndex)
        {
            CurrentIndex = target;
            global::MangaAgent.App.App.SLog($"JUMP -> logical {target + 1}/{TotalLogical}");
            SaveState();
        }
    }

    /// <summary>快速翻页：跳转到指定物理页（Webtoon 用），转成逻辑页后跳转。</summary>
    [RelayCommand]
    private void JumpToPhysical(int physical)
    {
        if (_logical.Count == 0) return;
        int target = Math.Clamp(physical, 0, _source.PageCount - 1);
        int newIndex = CurrentIndex;
        for (int i = 0; i < _logical.Count; i++)
        {
            var p = _logical[i];
            // 单页 (First,-1)：仅 FirstIndex == target；双页 (First,Second)：First <= target <= Second
            bool hit = p.SecondIndex < 0 ? p.FirstIndex == target
                                         : p.FirstIndex <= target && target <= p.SecondIndex;
            if (hit) { newIndex = i; break; }
        }
        if (newIndex != CurrentIndex)
        {
            CurrentIndex = newIndex;
            global::MangaAgent.App.App.SLog($"JUMP_PHYS -> physical {target + 1} logical {newIndex + 1}/{TotalLogical}");
            SaveState();
        }
    }

    [RelayCommand]
    private void ToggleMode()
    {
        _layoutVersion++;
        ViewMode = ViewMode switch
        {
            ViewMode.Single => ViewMode.Double,
            ViewMode.Double => ViewMode.Webtoon,
            _ => ViewMode.Single,
        };
        global::MangaAgent.App.App.SLog($"MODE -> {ViewMode} (index={CurrentIndex} logical={TotalLogical})");
        SaveState();
    }

    /// <summary>直接设置阅读模式（下拉菜单三选一，v1.8）。</summary>
    public void SetViewMode(ViewMode mode)
    {
        if (ViewMode == mode) return;
        _layoutVersion++;
        ViewMode = mode;
        global::MangaAgent.App.App.SLog($"MODE -> {mode} (index={CurrentIndex} logical={TotalLogical})");
        SaveState();
    }
    [RelayCommand] private void ToggleDirection() { Direction = Direction == ReadingDirection2.Rtl ? ReadingDirection2.Ltr : ReadingDirection2.Rtl; SaveState(); }
    [RelayCommand] private void ToggleTheme() => IsDark = !IsDark;
    // 缩放由画布视图（ReaderWindow）管理：滚轮/双击/工具栏按钮（Click 事件）

    /// <summary>供画布视图在 Fit 模式下同步显示倍率（不触发 Custom 切换）。</summary>
    public void ResetScaleInternal()
    {
        Scale = 1.0;
    }

    /// <summary>画布缩放后同步显示倍率（relative to FitWidth）。</summary>
    public void SyncScaleFromView(double relative)
    {
        Scale = Math.Max(0.02, relative);
    }

    [RelayCommand]
    private void ToggleBookmark()
    {
        var page = CurrentIndex;
        if (Bookmarks.Contains(page)) _state.RemoveBookmark(Book.Path, page);
        else _state.AddBookmark(Book.Path, page);
        LoadBookmarks();
    }

    private void LoadBookmarks()
    {
        Bookmarks.Clear();
        foreach (var b in _state.LoadBookmarks(Book.Path)) Bookmarks.Add(b);
    }

    [RelayCommand]
    private void NextBookmark()
    {
        if (Bookmarks.Count == 0) return;
        var next = Bookmarks.FirstOrDefault(b => b > CurrentIndex);
        if (next == 0 && !Bookmarks.Contains(0)) next = Bookmarks[0];
        CurrentIndex = next;
        SaveState();
    }

    [RelayCommand]
    private void PrevBookmark()
    {
        if (Bookmarks.Count == 0) return;
        var prev = Bookmarks.LastOrDefault(b => b < CurrentIndex);
        if (prev == 0 && !Bookmarks.Contains(0)) prev = Bookmarks[^1];
        CurrentIndex = prev;
        SaveState();
    }

    [RelayCommand]
    private void JumpToBookmark(int page)
    {
        if (page >= 0 && page < TotalLogical) { CurrentIndex = page; SaveState(); }
    }

    public async ValueTask DisposeAsync()
    {
        SaveState();
        await _source.DisposeAsync();
        _state.Dispose();
        // 关闭时 UI 即将销毁：Dispose 全部位图（含 UI 引用中的 —— BUG-004 计数清零）
        foreach (var bmp in _pageCache.Values)
            bmp.Dispose();
        foreach (var bmp in _uiRefs.Keys)
        {
            if (!_pageCache.ContainsValue(bmp))
                bmp.Dispose();
        }
        _pageCache.Clear();
        _cacheOrder.Clear();
        _uiRefs.Clear();
    }
}
