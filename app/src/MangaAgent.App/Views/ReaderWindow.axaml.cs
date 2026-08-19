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

    /// <summary>测试用：Webtoon 画布（虚拟化）。</summary>
    public Canvas? WebtoonCanvas => _webtoonCanvas;
    /// <summary>测试用：Webtoon 已实例化页数。</summary>
    public int WebtoonInstancedCount => _webtoonImages.Count;

    /// <summary>诊断：最近一次 PointerPressed 的命中源类型（测试用）。</summary>
    public string LastPressSource { get; private set; } = "";
    /// <summary>诊断：最近一次 PointerPressed 的左键状态。</summary>
    public bool LastPressLeft { get; private set; }
    /// <summary>诊断：Viewport 尺寸。</summary>
    public Size ViewportSize => Viewport?.Bounds.Size ?? default;
    /// <summary>诊断：PageImage1 可见性与位置。</summary>
    public string PageImage1Vis => $"{PageImage1?.IsVisible}@{PageImage1?.Bounds.Position} {PageImage1?.Bounds.Size}";
    private bool _isDragging;
    private Avalonia.Point _dragStart;

    public ReaderWindow()
    {
        InitializeComponent();
        // 根因（BUG-003）：Avalonia 的 RenderTransformOrigin 默认是 RelativePoint.Center（元素中心），
        // 与 WPF 的 TopLeft 不同 → 缩放围绕中心而非左上角 → p' = scale·p + t + (1-scale)·center，
        // 锚定数学 p' = scale·p + t 失效，焦点随放大向右下漂移。
        // 修复：显式设为 TopLeft，使渲染与锚定数学一致。
        PageLayer.RenderTransformOrigin = Avalonia.RelativePoint.TopLeft;
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
                    or nameof(ReaderViewModel.Direction)
                    or nameof(ReaderViewModel.PageText))
                {
                    Relayout();
                    ArrangeDoublePageOrder();
                    SyncJumpSlider();
                }
            };
            Relayout();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Relayout();
        if (Environment.GetCommandLineArgs().Contains("--diag"))
            RunZoomDiagnostics();
    }

    /// <summary>诊断：缩放前后内容中心/左上角的真实屏幕位置（TranslatePoint 含 RenderTransform）。</summary>
    private async void RunZoomDiagnostics()
    {
        await Task.Delay(2000);
        try
        {
            var sb = new System.Text.StringBuilder();
            App.SLog("DIAG start");
            if (_vm != null)
            {
                // 复现用户场景：先切到双页（含翻页制造缓存压力），再反复切换模式（快速连续，竞态窗口最大）
                _vm.ToggleModeCommand.Execute(null);   // Single -> Double
                await Task.Delay(1500);
                for (int pg = 0; pg < 8; pg++) { _vm.NextPageCommand.Execute(null); await Task.Delay(300); }
                // 快速连续切换 3 轮（Double->Webtoon->Single->Double...）
                for (int round = 0; round < 3; round++)
                {
                    try
                    {
                        _vm.ToggleModeCommand.Execute(null);
                        await Task.Delay(250);
                        _vm.ToggleModeCommand.Execute(null);
                        await Task.Delay(250);
                        sb.AppendLine($"R{round}: vm={_vm.ViewMode} img1={(_vm.Image1 is null ? "null" : "ok")} img2={(_vm.Image2 is null ? "null" : "ok")}");
                        App.SLog($"DIAG R{round} -> {_vm.ViewMode}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"R{round}: EX " + ex);
                        App.SLog("DIAG R" + round + " EX: " + ex);
                    }
                }
                // 稳定在 Webtoon 等待渲染（大量页面解码 + 淘汰）
                _vm.ToggleModeCommand.Execute(null);
                await Task.Delay(3000);
                sb.AppendLine($"FINAL: vm={_vm.ViewMode} img1={(_vm.Image1 is null ? "null" : "ok")}");
            }
            File.WriteAllText("D:\\MangaAgent\\app\\diag-zoom.txt", sb.ToString());
        }
        catch (Exception ex) { try { File.WriteAllText("D:\\MangaAgent\\app\\diag-zoom.txt", "ERR: " + ex); } catch { } }
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
        PageLayer.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _tx, _ty));
    }

    private void Relayout()
    {
        if (_vm is null || Viewport is null || PageImage1 is null) return;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();

        // 布局根因（BUG-003 残余）：StackPanel 在 Grid 中被视口压缩（Bounds 高=视口高），
        // 超高 Image 被垂直居中偏移 → 缩放后 y 方向漂移 -30×scale。
        // 修复：PageLayer/PagePanel 显式高度 = BaseH，Image 顶部对齐。
        // Webtoon 模式：画布高 = 总页数 × BaseH（虚拟化滚动），由 EnterWebtoon 维护，不在此重置。
        if (_vm.ViewMode != ViewMode.Webtoon)
        {
            PageLayer.Height = BaseH;
            PagePanel.Height = BaseH;
        }

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
            if (_vm.ViewMode != ViewMode.Webtoon)
            {
                _ty = (vpH - BaseH * _scale) / 2;   // 分页模式垂直居中；Webtoon 保留滚动位置
            }
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

    /// <summary>测试/按钮用：以内容中心为焦点缩放（factor=1.25/0.8）。</summary>
    public void ZoomRelative(double factor)
        => ZoomToFocus(factor, ContentScreenRect().Center);

    /// <summary>内容局部点 → 屏幕坐标（与 MatrixTransform 同一数学模型：p' = scale·p + t）。</summary>
    public Avalonia.Point ContentToScreen(Avalonia.Point local)
        => new(_scale * local.X + _tx, _scale * local.Y + _ty);

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
        App.SLog($"ZOOM factor={factor:F3} mouse=({mouse.X:F1},{mouse.Y:F1}) scale={_scale:F3}->{_scale * factor:F3} tx={_tx:F1} ty={_ty:F1} mode={_vm?.ViewMode}");
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
        if (_vm?.ViewMode == ViewMode.Webtoon)
        {
            // Webtoon 缩放：重算画布高 + 边界 clamp + 虚拟化刷新（页面高度随 scale 变化）
            PageLayer.Height = (_vm?.PhysicalPageCount ?? 1) * BaseH;
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
        }
        else
        {
            ClampToViewportIfSmaller();
        }
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
            // Webtoon：滚轮 = 滚动（按事件计数，固定步长 —— 修复 #9858 Delta 极小导致滚动缓慢）
            ScrollWebtoonByEvent(e);
            e.Handled = true;
            return;
        }
        // 分页模式：滚轮 = 缩放（按事件计数，完全不依赖 Delta 数值——Avalonia #9858 Delta 不稳定/极小）
        // 每次滚轮事件 = 一次缩放 step（5% × 灵敏度），方向由 Delta 符号决定
        if (e.Delta.Y != 0)
        {
            double step = 0.05 * Math.Max(0.1, ZoomSensitivity);
            ZoomToFocus(e.Delta.Y > 0 ? 1.0 + step : 1.0 / (1.0 + step), e.GetPosition(Viewport));
        }
        e.Handled = true;
    }

    // ---------- Webtoon 虚拟化（重构：Canvas 绝对定位 + 增量渲染 + 按事件滚动） ----------
    // 参考 mihon/Tachiyomi WebtoonViewer：只保留可见页 ±缓冲，滚动增量增删，无全量 Clear 重建。
    // 页高统一 = BaseH（基准坐标），缩放由 PageLayer MatrixTransform 统一处理 → 上下无缝拼接。
    private Canvas? _webtoonCanvas;
    private readonly Dictionary<int, Image> _webtoonImages = new();   // physical -> Image
    private readonly HashSet<int> _webtoonLoading = new();            // 正在异步加载的页（并发去重，防重复 Release 崩溃）
    private int _webtoonVisibleFirst, _webtoonVisibleLast;            // 最近一次目标可见范围
    private int _webtoonSeq;
    private const int WebtoonBufferPages = 2;                          // 可见范围上下缓冲页数

    private void EnterWebtoon()
    {
        if (_webtoonCanvas is null)
        {
            _webtoonCanvas = new Canvas { Background = null, Name = "WebtoonCanvas" };
        }
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != _webtoonCanvas)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(_webtoonCanvas);
        }
        // 画布总高 = 总页数 × BaseH（基准坐标），宽度由 Relayout 设定
        PageLayer.Height = (_vm?.PhysicalPageCount ?? 1) * BaseH;
        _webtoonSeq++;
        // 仅首次进入时定位到当前逻辑页对应的物理页；后续 Relayout（尺寸变化等）保持滚动位置
        if (_webtoonImages.Count == 0 && _vm is not null)
        {
            int firstPhys = _vm.CurrentFirstPhysical;
            _ty = -(firstPhys * BaseH * _scale);
            // 水平居中
            _tx = (Viewport.Bounds.Width - ComputeBaseW() * _scale) / 2;
            ApplyTransform();
        }
        // 尺寸/缩放变化后重算画布高与边界
        PageLayer.Height = (_vm?.PhysicalPageCount ?? 1) * BaseH;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
    }

    private void ExitWebtoon()
    {
        // 释放所有虚拟化 Image 的位图引用（BUG-004 引用计数）
        if (_vm is not null && _webtoonCanvas is not null)
        {
            foreach (var ch in _webtoonCanvas.Children.OfType<Image>())
                _vm.ReleaseUiBitmap(ch.Source as Avalonia.Media.Imaging.Bitmap);
            _webtoonCanvas.Children.Clear();
        }
        _webtoonImages.Clear();
        _webtoonSeq++;
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != PagePanel)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(PagePanel);
        }
        PageLayer.Height = BaseH;
        PagePanel.Orientation = Orientation.Horizontal;
    }

    /// <summary>Webtoon：按滚动位置增量渲染可见页 ± 缓冲（无全量重建）。</summary>
    private void UpdateWebtoonPages()
    {
        if (_vm is null || _webtoonCanvas is null) return;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        double scale = Math.Max(0.001, _scale);
        double pageH = BaseH;
        double contentTop = -_ty;                       // 视口顶对应的内容坐标（基准）
        double contentBottom = contentTop + vpH;
        int n = _vm.PhysicalPageCount;
        int first = Math.Max(0, (int)(contentTop / scale / pageH) - WebtoonBufferPages);
        int last = Math.Min(n - 1, (int)Math.Ceiling(contentBottom / scale / pageH) + WebtoonBufferPages);
        if (first > last) return;
        _webtoonVisibleFirst = first;
        _webtoonVisibleLast = last;

        // 增量移除范围外
        foreach (var k in _webtoonImages.Keys.ToList())
        {
            if (k < first || k > last) RemoveWebtoonImage(k);
        }
        // 增量添加缺失（异步解码，带 seq 保护）
        for (int i = first; i <= last; i++)
        {
            if (!_webtoonImages.ContainsKey(i)) _ = LoadWebtoonImageAsync(i);
        }
        // 当前可见页号 → 状态显示
        int curPhys = Math.Clamp((int)(contentTop / scale / pageH), 0, n - 1);
        App.SLog($"WEBTOON first={first} last={last} ty={_ty:F1} scale={scale:F3} curPhys={curPhys}");
        _vm.UpdateWebtoonPageText(curPhys);
    }

    private void RemoveWebtoonImage(int physical)
    {
        if (_webtoonImages.Remove(physical, out var img))
        {
            _vm?.ReleaseUiBitmap(img.Source as Avalonia.Media.Imaging.Bitmap);
            _webtoonCanvas?.Children.Remove(img);
        }
    }

    private async Task LoadWebtoonImageAsync(int physical)
    {
        if (_vm is null || _webtoonCanvas is null) return;
        // 并发去重：同一页同时只允许一个加载任务（否则两个任务对同一缓存位图
        // 各自 Acquire，先完成者 Add 后，后者 Release 会把前者 Image 的引用也放掉 → Dispose 崩溃）
        if (!_webtoonLoading.Add(physical)) return;
        try
        {
            var mySeq = _webtoonSeq;
            var bmp = await _vm.GetPageBitmapAsync(physical);
            // 已退出 Webtoon / 已重新布局 → 丢弃（Release 引用防泄漏）
            if (mySeq != _webtoonSeq || _vm.ViewMode != ViewMode.Webtoon || _webtoonCanvas is null)
            {
                _vm.ReleaseUiBitmap(bmp);
                return;
            }
            // 该页已实例化（另一路径 Add）或已滚出目标范围 → 丢弃
            if (_webtoonImages.ContainsKey(physical)
                || physical < _webtoonVisibleFirst || physical > _webtoonVisibleLast)
            {
                _vm.ReleaseUiBitmap(bmp);
                return;
            }
            var img = new Image
            {
                Stretch = Stretch.Uniform,
                Width = Math.Max(0.05, _vm.RatioOfPage(physical)) * BaseH,
                Height = BaseH,
            };
            if (bmp is not null) img.Source = bmp;   // DecodeAsync 已 Acquire（BUG-004）
            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, physical * BaseH);
            _webtoonCanvas.Children.Add(img);
            _webtoonImages[physical] = img;
        }
        finally
        {
            _webtoonLoading.Remove(physical);
        }
    }

    /// <summary>Webtoon：按事件滚动（固定步长 × 灵敏度，方向按 Delta 符号 —— 修复 #9858 Delta 极小导致滚动缓慢）。</summary>
    private void ScrollWebtoonByEvent(PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        // 步长 = 视口高 40%（页高通常 > 视口高，固定 96px 太慢；比例步长保证滚动流畅）
        double step = Math.Max(96.0, Viewport.Bounds.Height * 0.4) * Math.Max(0.1, ZoomSensitivity);
        // 向上滚(Delta>0) → 看前面（_ty 增大，向 0 靠近）；向下滚(Delta<0) → 看后面（_ty 减小）
        double dy = e.Delta.Y > 0 ? step : -step;
        _ty += dy;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
    }

    private void ClampWebtoonScroll()
    {
        double totalH = (_vm?.PhysicalPageCount ?? 1) * BaseH * _scale;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        if (totalH <= vpH) _ty = 0;
        else _ty = Math.Clamp(_ty, -(totalH - vpH), 0);
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        LastPressSource = e.Source?.GetType().Name ?? "null";
        LastPressLeft = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        App.SLog($"PRESS source={LastPressSource} left={LastPressLeft} pos={e.GetPosition(Viewport)} vp={ViewportSize}");
        // 只在图片上拖动（看图器行为：空白处不拖）
        if (e.Source is Image && LastPressLeft)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(Viewport);
            e.Handled = true;
            App.SLog("PRESS -> dragging");
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
        if (_vm?.ViewMode == ViewMode.Webtoon)
        {
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
        }
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

    private bool _sliderDragging;
    private bool _suppressSliderEvent;

    /// <summary>进度条拖动/点击 → 跳页（拖动中实时更新，松手才真正跳转避免逐页解码）。</summary>
    private void OnJumpSliderValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_vm is null || _suppressSliderEvent || JumpSlider is null) return;
        double ratio = Math.Clamp(e.NewValue / 100.0, 0.0, 1.0);
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            int phys = Math.Clamp((int)(ratio * _vm.PhysicalPageCount), 0, _vm.PhysicalPageCount - 1);
            // 拖动中实时预览页号，松手跳转
            _vm.UpdateWebtoonPageText(phys);
            if (!_sliderDragging) _vm.JumpToPhysicalCommand.Execute(phys);
        }
        else
        {
            int logical = Math.Clamp((int)(ratio * _vm.TotalLogical), 0, _vm.TotalLogical - 1);
            if (!_sliderDragging) _vm.JumpToPageCommand.Execute(logical);
        }
    }

    /// <summary>点击页码 → 跳页对话框。</summary>
    private void OnPageTextClick(object? sender, RoutedEventArgs e)
        => ShowJumpDialog();

    /// <summary>跳页对话框：输入 1..N 回车跳转（G 键 / 点击页码）。</summary>
    private async void ShowJumpDialog()
    {
        if (_vm is null) return;
        var dialog = new Window
        {
            Title = "快速跳页",
            Width = 320,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(_vm.IsDark ? "#1C1C24" : "#FFFFFF")),
        };
        var total = _vm.ViewMode == ViewMode.Webtoon ? _vm.PhysicalPageCount : _vm.TotalLogical;
        var cur = _vm.ViewMode == ViewMode.Webtoon
            ? Math.Clamp((int)((-_ty) / Math.Max(0.001, BaseH * _scale)), 0, total - 1) + 1
            : _vm.CurrentIndex + 1;
        var text = new TextBox { Text = cur.ToString(), Margin = new Thickness(16, 18, 16, 8), FontSize = 16 };
        var info = new TextBlock
        {
            Text = $"共 {total} 页（当前 {cur}）。输入页码后回车。",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#888888")),
            Margin = new Thickness(16, 0, 16, 4),
        };
        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(info);
        dialog.Content = panel;
        text.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                if (int.TryParse(text.Text?.Trim(), out int p) && p >= 1)
                {
                    App.SLog($"JUMP_DIALOG -> {p}/{total}");
                    if (_vm.ViewMode == ViewMode.Webtoon) _vm.JumpToPhysicalCommand.Execute(p - 1);
                    else _vm.JumpToPageCommand.Execute(p - 1);
                }
                dialog.Close();
            }
            else if (ke.Key == Key.Escape) dialog.Close();
        };
        dialog.Opened += (_, _) => { text.Focus(); text.SelectAll(); };
        await dialog.ShowDialog(this);
    }

    /// <summary>进度条与当前页同步（翻页/跳页后更新位置，抑制事件回环）。</summary>
    private void SyncJumpSlider()
    {
        if (_vm is null || JumpSlider is null) return;
        double ratio = _vm.ViewMode == ViewMode.Webtoon
            ? Math.Clamp((double)(-_ty) / Math.Max(1.0, (_vm.PhysicalPageCount) * BaseH * _scale), 0.0, 1.0)
            : (double)_vm.CurrentIndex / Math.Max(1, _vm.TotalLogical - 1);
        _suppressSliderEvent = true;
        JumpSlider.Value = ratio * 100.0;
        _suppressSliderEvent = false;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        App.SLog($"KEY {e.Key} ctrl={_ctrlDown}");
        bool webtoon = _vm.ViewMode == ViewMode.Webtoon;
        switch (e.Key)
        {
            case Key.Right:
                if (webtoon) { ScrollWebtoonPages(1); e.Handled = true; break; }
                if (_vm.Direction == ReadingDirection2.Rtl) _vm.PrevPageCommand.Execute(null);
                else _vm.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                if (webtoon) { ScrollWebtoonPages(-1); e.Handled = true; break; }
                if (_vm.Direction == ReadingDirection2.Rtl) _vm.NextPageCommand.Execute(null);
                else _vm.PrevPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageDown:
            case Key.Space:
                if (webtoon) { ScrollWebtoonPages(1); e.Handled = true; break; }
                _vm.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageUp:
                if (webtoon) { ScrollWebtoonPages(-1); e.Handled = true; break; }
                _vm.PrevPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.M: _vm.ToggleModeCommand.Execute(null); e.Handled = true; break;
            case Key.D: _vm.ToggleDirectionCommand.Execute(null); e.Handled = true; break;
            case Key.T: _vm.ToggleThemeCommand.Execute(null); e.Handled = true; break;
            case Key.B: _vm.ToggleBookmarkCommand.Execute(null); e.Handled = true; break;
            case Key.G: ShowJumpDialog(); e.Handled = true; break;
        }
    }

    /// <summary>Webtoon：按页滚动（1 页 = 一屏高，-1 向上）。</summary>
    private void ScrollWebtoonPages(int dir)
    {
        if (_vm is null) return;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        _ty += dir * vpH;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
    }
}
