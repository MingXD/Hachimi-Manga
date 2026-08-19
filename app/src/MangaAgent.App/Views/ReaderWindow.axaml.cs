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
    private double _wheelAccum;
    private Avalonia.Point _lastWheelPos;

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
                    or nameof(ReaderViewModel.Scale)
                    or nameof(ReaderViewModel.Direction))
                {
                    Relayout();
                    ArrangeDoublePageOrder();
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

    /// <summary>缩放灵敏度（1.0 默认；>1 更快，<1 更慢）。设置 UI 后续提供。</summary>
    public double ZoomSensitivity { get; set; } = 1.0;

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
            ArrangeDoublePageOrder();
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

    /// <summary>内容渲染区域（视口坐标）。</summary>
    private Avalonia.Rect ContentScreenRect()
    {
        double cw = ComputeBaseW() * _scale;
        double ch = BaseH * _scale;
        return new Avalonia.Rect(_tx, _ty, cw, ch);
    }

    /// <summary>焦点：指针位置 clamp 到内容区域（指针在画面外时取最近边界点）。</summary>
    private Avalonia.Point EffectiveFocus(Avalonia.Point mouse)
    {
        var r = ContentScreenRect();
        return new Avalonia.Point(
            Math.Clamp(mouse.X, r.Left, Math.Max(r.Left, r.Right)),
            Math.Clamp(mouse.Y, r.Top, Math.Max(r.Top, r.Bottom)));
    }

    /// <summary>标准焦点锚定缩放（zoooom 等 pan/zoom 库算法）。</summary>
    public void ZoomToFocus(double factor, Avalonia.Point mouse)
    {
        double old = _scale;
        if (_vm is not null && _vm.FitMode != FitMode.Custom)
            _vm.FitMode = FitMode.Custom;
        double target = Math.Clamp(_scale * factor, 0.02, 8.0);
        double r = target / old;
        var f = EffectiveFocus(mouse);
        _tx = f.X - (f.X - _tx) * r;
        _ty = f.Y - (f.Y - _ty) * r;
        _scale = target;
        ApplyTransform();
        ClampToViewportIfSmaller();
        if (_vm is not null)
        {
            _vm.SyncScaleFromView(_scale / CurrentFitScale());
            double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
            _vm.DecodeTargetWidth = (int)Math.Clamp(ComputeBaseW() * _scale * dpr, 1200, 4096);
        }
    }

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
        // 分页模式：滚轮 = 缩放（累积阈值触发，不依赖单次 Delta 绝对值——Avalonia #9858）
        // 每累积 60 触发一次 5% 缩放；普通鼠标一格≈120 → 每格 ≈10%；灵敏度系数可调
        _lastWheelPos = e.GetPosition(Viewport);
        _wheelAccum += e.Delta.Y;
        double threshold = 60.0 / Math.Max(0.1, ZoomSensitivity);
        while (_wheelAccum >= threshold)
        {
            _wheelAccum -= threshold;
            ZoomToFocus(1.05, _lastWheelPos);
        }
        while (_wheelAccum <= -threshold)
        {
            _wheelAccum += threshold;
            ZoomToFocus(1.0 / 1.05, _lastWheelPos);
        }
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

    private static readonly double[] SensLevels = { 0.5, 1.0, 2.0 };
    private int _sensIdx = 1;

    private void OnSensitivityClick(object? sender, RoutedEventArgs e)
    {
        _sensIdx = (_sensIdx + 1) % SensLevels.Length;
        ZoomSensitivity = SensLevels[_sensIdx];
        if (SensitivityButton is not null)
            SensitivityButton.Content = $"缩放×{ZoomSensitivity:0.#}";
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e) => ZoomRelative(0.8);

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.FitMode = FitMode.FitWidth;
    }

    

    /// <summary>按阅读方向排列双页左右顺序（RTL：右=前页；LTR：左=前页）。</summary>
    private void ArrangeDoublePageOrder()
    {
        if (_vm is null || !_vm.IsDoublePageShown) return;
        bool rtl = _vm.Direction == ReadingDirection2.Rtl;
        // PagePanel 子顺序：[PageImage1(前), PageImage2(后)] LTR；RTL 反转
        int i1 = PagePanel.Children.IndexOf(PageImage1);
        int i2 = PagePanel.Children.IndexOf(PageImage2);
        if (i1 < 0 || i2 < 0) return;
        if (rtl && i1 < i2)
        {
            PagePanel.Children.Remove(PageImage1);
            PagePanel.Children.Insert(i2, PageImage1);
        }
        else if (!rtl && i1 > i2)
        {
            PagePanel.Children.Remove(PageImage2);
            PagePanel.Children.Insert(i1, PageImage2);
        }
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
