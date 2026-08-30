using System.Collections.Concurrent;
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

/// <summary>阅读器：逻辑页序列（PageLayouter）+ 当前页解码 + 续读/书签。
/// P1-2 按职责拆分为 partial 文件：Decode（缓存/解码/引用计数）、Nav（翻页/书签/跳转）、State（构造/保存/释放）。</summary>
public partial class ReaderViewModel : ViewModelBase, IAsyncDisposable
{
    public BookInfo Book { get; }
    public string BookTitle => Book.DisplayName;

    private readonly ComicSource _source;
    private List<LogicalPage> _logical = new();
    // P2-1：LoadSizes 异步化后后台线程写、UI 线程读 → 用并发字典防撕裂
    private readonly ConcurrentDictionary<int, PageSize> _sizes = new();

    /// <summary>双页配对起点（0=1-2 相接，1=2-3 相接）。</summary>
    [ObservableProperty]
    private int _pairOffset;

    public string PairText => PairOffset == 0 ? "配对起点1" : "配对起点2";

    [ObservableProperty] private Bitmap? _image1;
    [ObservableProperty] private Bitmap? _image2;
    [ObservableProperty] private double _ratio1 = 0.7;
    [ObservableProperty] private double _ratio2 = 0.7;
    [ObservableProperty] private bool _isDoublePageShown;
    [ObservableProperty] private bool _isWebtoon;
    [ObservableProperty] private int _currentIndex;
    /// <summary>当前物理页（0-based）—— BUG-A v1.9：物理页为导航基准，单页逐物理页。</summary>
    private int _currentPhysical;
    [ObservableProperty] private int _totalLogical = 1;
    [ObservableProperty] private ViewMode _viewMode = ViewMode.Single;
    [ObservableProperty] private ReadingDirection2 _direction = ReadingDirection2.Rtl;

    partial void OnDirectionChanged(ReadingDirection2 value)
    {
        // 问题2修复：DirectionText 是表达式属性，Direction 变化时必须显式通知刷新
        OnPropertyChanged(nameof(DirectionText));
    }
    [ObservableProperty] private bool _isDark = true;
    // 默认适应屏幕（v1.8：打开漫画/切模式默认 FitScreen，看到整页）
    [ObservableProperty] private FitMode _fitMode = FitMode.FitScreen;
    [ObservableProperty] private double _scale = 1.0;   // Custom 模式的倍率（1.0 = 适应宽度基准）
    [ObservableProperty] private string _pageText = "1 / 1";
    /// <summary>缩放百分比显示（基准 = 适应屏幕，v1.9.8）。由视图在缩放/复位后更新。</summary>
    [ObservableProperty] private double _zoomPercent = 100;
    public string ZoomPercentText => $"{ZoomPercent:F0}%";
    [ObservableProperty] private ObservableCollection<int> _bookmarks = new();
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _hasStatus;

    /// <summary>布局版本：RebuildLayout/ToggleMode 等改变逻辑页结构时自增。
    /// LoadCurrentAsync 快照此版本，仅当版本过期才丢弃结果（BUG-3：此前对比 FirstIndex 会把
    /// 配对切换后的新布局误判为竞态而丢弃 → 画面不更新）。</summary>
    private int _layoutVersion;

    public string BackgroundHex => IsDark ? "#101014" : "#F3F1EC";
    public string ToolbarHex => IsDark ? "#1C1C24" : "#FFFFFF";
    public string ForegroundHex => IsDark ? "#E8E8E8" : "#1A1A1A";

    // 工具栏按钮配色（UI-2 v1.9：白天/夜间统一清晰 —— 半透明底 + 边框 + 悬停高亮）
    public string ToolbarButtonBg => IsDark ? "#2A2A34" : "#E8E6E1";
    public string ToolbarButtonBorder => IsDark ? "#3A3A46" : "#C8C4BC";
    public string ToolbarButtonHover => IsDark ? "#3A3A48" : "#D8D4CC";
    public string ToolbarButtonBorderHover => IsDark ? "#5A5A6A" : "#A8A49C";

    // 预览栏背景（白天浅半透明/夜间深半透明 —— 均半透明，不遮死画面）
    public string PreviewStripBg => IsDark ? "#CC1C1C24" : "#CCFFFFFF";
    public string PreviewStripItemBg => IsDark ? "#3A3A44" : "#E8E6E1";
    public string PreviewStripLabel => IsDark ? "#CCC" : "#333";
    // 预览栏当前页高亮（边框/页码变色）：夜金 / 昼蓝
    public string PreviewStripHighlight => IsDark ? "#FFD700" : "#0078D4";
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
        // 自定义缩放：与 −/+ 中间文本一致（基准=适应屏幕）—— 两边对齐
        _ => ZoomPercentText,
    };

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
        // v3 #4：滚动模式下比例/布局变化（LoadSizes 补全后）→ 通知视图重建当前页 fit（真实比例）
        if (ViewMode == ViewMode.Webtoon) WebtoonLayoutChanged?.Invoke();
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
    partial void OnZoomPercentChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomPercentText));
        OnPropertyChanged(nameof(FitText));   // Custom 模式 FitText 显示 ZoomPercentText
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
        // 按钮配色也必须通知（否则白天/夜间切换后按钮背景停留在旧值 → 白天按钮仍黑色）
        OnPropertyChanged(nameof(ToolbarButtonBg));
        OnPropertyChanged(nameof(ToolbarButtonBorder));
        OnPropertyChanged(nameof(ToolbarButtonHover));
        OnPropertyChanged(nameof(ToolbarButtonBorderHover));
        OnPropertyChanged(nameof(PreviewStripBg));
        OnPropertyChanged(nameof(PreviewStripItemBg));
        OnPropertyChanged(nameof(PreviewStripLabel));
        OnPropertyChanged(nameof(PreviewStripHighlight));
    }

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
}
