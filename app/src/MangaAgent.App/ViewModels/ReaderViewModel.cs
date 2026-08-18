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

    public string PairText => PairOffset == 0 ? "配对:1-2" : "配对:2-3";
    private readonly ReadingStateStore _state;

    [ObservableProperty] private Bitmap? _image1;
    [ObservableProperty] private Bitmap? _image2;
    [ObservableProperty] private double _ratio1 = 0.7;
    [ObservableProperty] private double _ratio2 = 0.7;
    [ObservableProperty] private bool _isDoublePageShown;
    [ObservableProperty] private int _currentIndex;
    [ObservableProperty] private int _totalLogical = 1;
    [ObservableProperty] private bool _doubleMode;
    [ObservableProperty] private ReadingDirection2 _direction = ReadingDirection2.Rtl;
    [ObservableProperty] private bool _isDark = true;
    [ObservableProperty] private FitMode _fitMode = FitMode.FitWidth;
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

    /// <summary>解码目标宽度（按显示尺寸降采样，4K 扫描页从 104MB 降到 ~10MB）。</summary>
    [ObservableProperty]
    private int _decodeTargetWidth = 2048;

    private int _loadSeq;

    public string BackgroundHex => IsDark ? "#101014" : "#F3F1EC";
    public string ToolbarHex => IsDark ? "#1C1C24" : "#FFFFFF";
    public string ForegroundHex => IsDark ? "#E8E8E8" : "#1A1A1A";
    public string ModeText => DoubleMode ? "双页" : "单页";
    public string DirectionText => Direction == ReadingDirection2.Rtl ? "RTL" : "LTR";
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
        _doubleMode = saved?.Mode == ReadingMode.Double;
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
    partial void OnDoubleModeChanged(bool value) => _ = LoadCurrentAsync();
    partial void OnScaleChanged(double value)
    {
        if (FitMode != FitMode.Custom) FitMode = FitMode.Custom;
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
        var lp = _logical[CurrentIndex];
        var showDouble = DoubleMode && lp.IsDouble;

        var img1 = await DecodeAsync(lp.FirstIndex);
        Bitmap? img2 = null;
        if (showDouble && lp.SecondIndex >= 0)
            img2 = await DecodeAsync(lp.SecondIndex);

        // 竞态防护：期间用户已翻页/切换模式，则丢弃本次结果（BUG-001 修复）
        if (mySeq != _loadSeq || CurrentIndex >= _logical.Count) return;
        var cur = _logical[CurrentIndex];
        if (cur.FirstIndex != lp.FirstIndex || (showDouble != (DoubleMode && cur.IsDouble))) return;

        HasStatus = false;
        IsDoublePageShown = showDouble;
        Image1 = img1;
        Ratio1 = RatioOf(lp.FirstIndex);
        Image2 = img2;
        if (img2 is not null) Ratio2 = RatioOf(lp.SecondIndex);
        PageText = $"{CurrentIndex + 1} / {TotalLogical}";
        ProgressValue = CurrentIndex + 1;
    }

    private double RatioOf(int physical) => _sizes.TryGetValue(physical, out var sz) ? sz.AspectRatio : 0.7;

    private async Task<Bitmap?> DecodeAsync(int physical)
    {
        if (_pageCache.TryGetValue(physical, out var cached))
        {
            _cacheOrder.Remove(physical);
            _cacheOrder.AddLast(physical);
            return cached;
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
        return decoded;
    }

    private void EvictCache()
    {
        while (_cacheOrder.Count > CacheCap)
        {
            var oldest = _cacheOrder.First!.Value;
            _cacheOrder.RemoveFirst();
            if (_pageCache.Remove(oldest, out var bmp))
                bmp.Dispose();
        }
    }

    private void SaveState() =>
        _state.SaveState(Book.Path, CurrentIndex, DoubleMode ? ReadingMode.Double : ReadingMode.Single, Direction);

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentIndex < TotalLogical - 1) { CurrentIndex++; SaveState(); }
    }

    [RelayCommand]
    private void PrevPage()
    {
        if (CurrentIndex > 0) { CurrentIndex--; SaveState(); }
    }

    [RelayCommand] private void ToggleMode() { DoubleMode = !DoubleMode; SaveState(); }
    [RelayCommand] private void ToggleDirection() { Direction = Direction == ReadingDirection2.Rtl ? ReadingDirection2.Ltr : ReadingDirection2.Rtl; SaveState(); }
    [RelayCommand] private void ToggleTheme() => IsDark = !IsDark;
    [RelayCommand]
    private void ZoomIn()
    {
        FitMode = FitMode.Custom;
        Scale = Math.Min(8.0, Scale * 1.25);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        FitMode = FitMode.Custom;
        Scale = Math.Max(0.25, Scale / 1.25);
    }

    [RelayCommand]
    private void ResetZoom() => FitMode = FitMode.FitWidth;

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
        foreach (var bmp in _pageCache.Values)
            bmp.Dispose();
        _pageCache.Clear();
        _cacheOrder.Clear();
    }
}
