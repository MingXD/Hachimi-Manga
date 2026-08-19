using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.Views;

/// <summary>阅读器：画布模型（视口 + 图片层 Scale/Translate），复刻 Windows 看图器交互。
/// 滚轮=精细缩放(鼠标焦点)；拖拽图片=平移(可出界)；空白不拖；←→翻页；双页贴合。</summary>
public partial class ReaderWindow : Window
{
    private ReaderViewModel? _vm;
    private bool _ctrlDown;

    // 画布状态（图片层变换）
    private const double BaseH = 800.0;          // 图片基准高度（像素），缩放由 Scale 控制
    private double _scale = 1.0;
    private double _tx = 0, _ty = 0;

    /// <summary>测试用：画布当前缩放。</summary>
    public double CanvasScale => _scale;
    /// <summary>测试用：画布当前平移 X。</summary>
    public double CanvasX => _tx;
    /// <summary>测试用：画布当前平移 Y。</summary>
    public double CanvasY => _ty;
    private bool _isDragging;
    private Avalonia.Point _dragStart;
    private readonly ScaleTransform _layerScale = new(1, 1);
    private readonly TranslateTransform _layerTranslate = new(0, 0);

    public ReaderWindow()
    {
        InitializeComponent();
        PageLayer.RenderTransform = new TransformGroup
        {
            Children = { _layerScale, _layerTranslate },
        };
        KeyDown += (_, e) => { if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) _ctrlDown = true; };
        KeyUp += (_, e) => { if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl) _ctrlDown = false; };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ReaderViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ReaderViewModel.Image1)
                    or nameof(ReaderViewModel.Image2)
                    or nameof(ReaderViewModel.IsDoublePageShown)
                    or nameof(ReaderViewModel.FitMode)
                    or nameof(ReaderViewModel.Scale))
                {
                    Relayout();
                }
            };
            Relayout();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Relayout();
    }

    // ---------- 布局：基准内容 + Fit 计算 + 变换应用 ----------
    private double ComputeBaseW()
    {
        if (_vm is null) return 0.7 * BaseH;
        double r1 = Math.Max(0.05, _vm.Ratio1);
        return (_vm.IsDoublePageShown && _vm.Image2 is not null
            ? r1 + Math.Max(0.05, _vm.Ratio2)
            : r1) * BaseH;
    }

    private double CurrentFitScale()
    {
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();
        return _vm?.FitMode switch
        {
            FitMode.FitHeight => vpH / BaseH,
            FitMode.FitScreen => Math.Min(vpW / baseW, vpH / BaseH),
            _ => vpW / baseW,
        };
    }

    private void ApplyTransform()
    {
        _layerScale.ScaleX = _scale;
        _layerScale.ScaleY = _scale;
        _layerTranslate.X = _tx;
        _layerTranslate.Y = _ty;
    }

    private void Relayout()
    {
        if (_vm is null || Viewport is null || PageImage1 is null) return;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();

        // 基准图片尺寸（Uniform 填满控件，控件比例 = 图片比例）
        PageImage1.Width = Math.Max(1, Math.Max(0.05, _vm.Ratio1) * BaseH);
        PageImage1.Height = BaseH;
        if (_vm.Image2 is not null)
        {
            PageImage2.Width = Math.Max(1, Math.Max(0.05, _vm.Ratio2) * BaseH);
            PageImage2.Height = BaseH;
        }

        if (_vm.FitMode != FitMode.Custom)
        {
            // Fit 模式：计算 scale + 居中 translate
            _scale = CurrentFitScale();
            _tx = (vpW - baseW * _scale) / 2;
            _ty = (vpH - BaseH * _scale) / 2;
            if (Math.Abs(_vm.Scale - 1.0) > 0.001) _vm.ResetScaleInternal();
        }
        // Custom 模式：scale/translate 由画布交互管理，Relayout 不重置
        ApplyTransform();
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            EnterWebtoon();
        }
        else
        {
            ExitWebtoon();
        }
    }

    // ---------- 缩放（精细、鼠标焦点锚定） ----------
    private void ZoomTo(double target, Avalonia.Point pos)
    {
        double old = _scale;
        if (_vm is not null && _vm.FitMode != FitMode.Custom)
            _vm.FitMode = FitMode.Custom;   // 进入自定义缩放（Relayout 会重算 scale，随后覆盖回目标值）
        _scale = Math.Clamp(target, 0.02, 8.0);
        double r = _scale / old;
        _tx = pos.X - (pos.X - _tx) * r;
        _ty = pos.Y - (pos.Y - _ty) * r;
        ApplyTransform();
        ClampToViewportIfSmaller();
        if (_vm is not null)
        {
            _vm.SyncScaleFromView(_scale / CurrentFitScale());
            // 按需高清：解码目标宽度随显示尺寸增长（上限 4096）
            double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
            _vm.DecodeTargetWidth = (int)Math.Clamp(ComputeBaseW() * _scale * dpr, 1200, 4096);
        }
    }

    /// <summary>看图器规则：内容小于视口时自动居中（缩放后）；放大时保持当前位置。</summary>
    private void ClampToViewportIfSmaller()
    {
        double vpW = Math.Max(1, Viewport.Bounds.Width);
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        double cw = ComputeBaseW() * _scale;
        double ch = BaseH * _scale;
        if (cw <= vpW) _tx = (vpW - cw) / 2;
        if (ch <= vpH) _ty = (vpH - ch) / 2;
        ApplyTransform();
    }

    /// <summary>测试/按钮用：以视口中心缩放（factor=1.25/0.8）。</summary>
    public void ZoomRelative(double factor)
        => ZoomTo(_scale * factor, new Avalonia.Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2));

    // ---------- 输入 ----------
    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e) => Relayout();

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || _ctrlDown;
        if (_vm.ViewMode == ViewMode.Webtoon && !ctrl)
        {
            // Webtoon：滚轮 = 向下滚动（内容上移）
            _ty -= e.Delta.Y * 3.0;
            ApplyTransform();
            UpdateWebtoonPages();
            e.Handled = true;
            return;
        }
        // 分页模式：滚轮 = 精细缩放（复刻看图器：以鼠标为焦点）
        double factor = Math.Pow(1.0015, e.Delta.Y);
        ZoomTo(_scale * factor, e.GetPosition(Viewport));
        e.Handled = true;
    }

    private StackPanel? _webtoonPanel;
    private (int first, int last) _webtoonLoadedRange = (-1, -1);

    private void EnterWebtoon()
    {
        if (_webtoonPanel is null)
        {
            _webtoonPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 0 };
        }
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != _webtoonPanel)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(_webtoonPanel);
        }
        _webtoonLoadedRange = (-1, -1);
        UpdateWebtoonPages();
    }

    private void ExitWebtoon()
    {
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != PagePanel)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(PagePanel);
        }
        PagePanel.Orientation = Orientation.Horizontal;
    }

    /// <summary>Webtoon：按可见区域懒加载页面（复用页面位图缓存）。</summary>
    private async void UpdateWebtoonPages()
    {
        if (_vm is null || _webtoonPanel is null) return;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        double pageH = BaseH * _scale;
        if (pageH <= 0) return;
        double top = _ty;
        int first = Math.Max(0, (int)(top / pageH) - 1);
        int last = Math.Min(_vm.PhysicalPageCount - 1, (int)((top + vpH) / pageH) + 1);
        var cur = _webtoonLoadedRange;
        if (cur.first == first && cur.last == last) return;
        _webtoonLoadedRange = (first, last);
        _webtoonPanel.Children.Clear();
        for (int i = first; i <= last; i++)
        {
            var img = new Image
            {
                Stretch = Stretch.Uniform,
                Width = Math.Max(0.05, _vm.RatioOfPage(i)) * BaseH,
                Height = BaseH,
            };
            var bmp = await _vm.GetPageBitmapAsync(i);
            if (bmp is not null) img.Source = bmp;
            _webtoonPanel.Children.Add(img);
        }
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        // 只在图片上拖动（看图器行为：空白处不拖）
        if (e.Source is Image && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(Viewport);
            e.Handled = true;
        }
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;
        var pos = e.GetPosition(Viewport);
        _tx += pos.X - _dragStart.X;
        _ty += pos.Y - _dragStart.Y;   // 无 clamp → 图片可拖出应用边界
        _dragStart = pos;
        ApplyTransform();
        e.Handled = true;
    }

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Handled = true;
    }

    private void OnZoomInClick(object? sender, RoutedEventArgs e) => ZoomRelative(1.25);

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => ZoomRelative(0.8);

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.FitMode = FitMode.FitWidth;
    }

    

    private void OnViewportDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        double fit = CurrentFitScale();
        double target = Math.Abs(_scale - fit) < 0.02 ? fit * 2.0 : fit;   // Fit ↔ 200%
        ZoomTo(target, e.GetPosition(Viewport));
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        switch (e.Key)
        {
            case Key.Right:
                if (_vm.Direction == ReadingDirection2.Rtl) _vm.PrevPageCommand.Execute(null);
                else _vm.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                if (_vm.Direction == ReadingDirection2.Rtl) _vm.NextPageCommand.Execute(null);
                else _vm.PrevPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageDown:
            case Key.Space:
                _vm.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageUp:
                _vm.PrevPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.M: _vm.ToggleModeCommand.Execute(null); e.Handled = true; break;
            case Key.D: _vm.ToggleDirectionCommand.Execute(null); e.Handled = true; break;
            case Key.T: _vm.ToggleThemeCommand.Execute(null); e.Handled = true; break;
            case Key.B: _vm.ToggleBookmarkCommand.Execute(null); e.Handled = true; break;
        }
    }
}
