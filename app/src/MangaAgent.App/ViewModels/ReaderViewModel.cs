using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Paging;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.ViewModels;

/// <summary>阅读器：逻辑页序列（PageLayouter）+ 当前页解码 + 续读/书签。</summary>
public partial class ReaderViewModel : ViewModelBase, IAsyncDisposable
{
    public BookInfo Book { get; }
    public string BookTitle => Book.DisplayName;

    private readonly ComicSource _source;
    private readonly List<LogicalPage> _logical;
    private readonly Dictionary<int, PageSize> _sizes = new();
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
    [ObservableProperty] private double _scale = 1.0;
    [ObservableProperty] private string _pageText = "1 / 1";
    [ObservableProperty] private int _progressValue;
    [ObservableProperty] private ObservableCollection<int> _bookmarks = new();

    public string BackgroundHex => IsDark ? "#101014" : "#F3F1EC";
    public string ToolbarHex => IsDark ? "#1C1C24" : "#FFFFFF";
    public string ForegroundHex => IsDark ? "#E8E8E8" : "#1A1A1A";
    public string ModeText => DoubleMode ? "双页" : "单页";
    public string DirectionText => Direction == ReadingDirection2.Rtl ? "RTL" : "LTR";
    public string ThemeText => IsDark ? "夜间" : "白天";
    public string ScaleText => $"{Scale * 100:F0}%";

    public ReaderViewModel(BookInfo book, string stateDbPath)
    {
        Book = book;
        _state = new ReadingStateStore(stateDbPath);
        _source = ComicSourceFactory.Open(book.Path);
        LoadSizes();
        _logical = PageLayouter.Layout(
            Enumerable.Range(0, _source.PageCount)
                .Select(i => new PhysicalPageRef(i, _sizes.TryGetValue(i, out var sz) ? sz.AspectRatio : 0.7))
                .ToList());
        TotalLogical = _logical.Count;

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

    partial void OnCurrentIndexChanged(int value) => _ = LoadCurrentAsync();
    partial void OnDoubleModeChanged(bool value) => _ = LoadCurrentAsync();
    partial void OnScaleChanged(double value) => OnPropertyChanged(nameof(ScaleText));
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
        var lp = _logical[CurrentIndex];
        var showDouble = DoubleMode && lp.IsDouble;
        IsDoublePageShown = showDouble;

        Image1 = await DecodeAsync(lp.FirstIndex);
        Ratio1 = RatioOf(lp.FirstIndex);
        if (showDouble && lp.SecondIndex >= 0)
        {
            Image2 = await DecodeAsync(lp.SecondIndex);
            Ratio2 = RatioOf(lp.SecondIndex);
        }
        else
        {
            Image2 = null;
        }
        PageText = $"{CurrentIndex + 1} / {TotalLogical}";
        ProgressValue = CurrentIndex + 1;
    }

    private double RatioOf(int physical) => _sizes.TryGetValue(physical, out var sz) ? sz.AspectRatio : 0.7;

    private async Task<Bitmap?> DecodeAsync(int physical)
    {
        try
        {
            using var s = await _source.OpenPageAsync(physical);
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch { return null; }
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
    [RelayCommand] private void ZoomIn() { Scale = Math.Min(4.0, Scale * 1.25); }
    [RelayCommand] private void ZoomOut() { Scale = Math.Max(0.25, Scale / 1.25); }
    [RelayCommand] private void ResetZoom() => Scale = 1.0;

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
    }
}
