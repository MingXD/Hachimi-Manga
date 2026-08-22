using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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
    /// <summary>诊断：Viewport 在窗口中的位置（测试坐标换算用）。</summary>
    public Point ViewportOrigin => Viewport?.TranslatePoint(new Point(0, 0), this) ?? default;
    /// <summary>诊断：PageLayer 当前子元素（测试确认 Webtoon 已退出）。</summary>
    public string PageLayerChildType => PageLayer?.Children.Count > 0 ? PageLayer.Children[0].GetType().Name : "empty";

    /// <summary>测试用：调用分页复位。</summary>
    public void CallReset() => ResetPagedView();

    /// <summary>调试面板（F12 开关，诊断"点击无反应"等交互问题）。</summary>
    private Views.DebugWindow? _debugWindow;


    private void ToggleDebugPanel()
    {
        // 调试面板独立窗口（v1.9.6）：不占阅读器空间、不干扰按钮点击
        if (_debugWindow is null)
        {
            _debugWindow = new Views.DebugWindow();
            _debugWindow.StatusProvider = () =>
            {
                if (_vm is null) return "vm=null";
                bool dragStuck = _isDragging;
                bool timerRunning = _scrollTimer?.IsEnabled == true;
                return $"模式:{_vm.ViewMode} | 缩放:{_scale:F2} | tx:{_tx:F0} ty:{_ty:F0}\n" +
                       $"物理页:{_vm.CurrentPhysical + 1}/{_vm.PhysicalPageCount} | {_vm.PageText}\n" +
                       $"方向:{_vm.DirectionText} | 配对:{_vm.PairText} | Fit:{_vm.FitMode}\n" +
                       $"拖拽卡死:{dragStuck} | 滚动Timer空转:{timerRunning} | 图1:{( _vm.Image1 is null ? "无" : "有")} 图2:{(_vm.Image2 is null ? "无" : "有")}";
            };
            _debugWindow.Start(_debugWindow.StatusProvider);
        }
        if (_debugWindow.IsVisible) _debugWindow.Hide();
        else _debugWindow.Show();
    }




    /// <summary>测试用：当前 FitScreen 缩放值。</summary>
    public double CurrentFitScaleValue => CurrentFitScale();
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
        // 修复"点击无反应"：拖拽状态兜底复位 —— 若拖拽中鼠标在 Viewport 外松开
        // （移到工具栏/按钮上），Viewport 收不到 Released → _isDragging 卡 true →
        // 后续每次鼠标移动都被视口处理并 Handled → 按钮 Click 被吞。
        // 窗口级监听 Released 保证任何位置松开都复位拖拽状态。
        this.AddHandler(PointerReleasedEvent, (_, e) => _isDragging = false, Avalonia.Interactivity.RoutingStrategies.Bubble);
        // 诊断兜底：记录每次鼠标按下的目标控件（排查"点击无反应"——事件是否到达、被谁吞）
        this.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Control src)
                App.SLog($"CLICK_DOWN -> {src.GetType().Name} '{src.Name ?? ""}'");
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ReaderViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += (_, args) =>
            {
                // 注意：PageText 不在此列表 —— 它只是显示文本，触发 Relayout 会导致
                // 按钮样式每帧重评估（闪烁）并浪费布局。PageText 仅用于 SyncJumpSlider 进度同步。
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
        if (Environment.GetCommandLineArgs().Contains("--diag"))
            RunZoomDiagnostics();
    }

    protected override void OnClosed(EventArgs e)
    {
        // 窗口关闭：先退出 Webtoon 清空画布位图引用，再释放 VM
        try
        {
            _windowClosed = true;
            _webtoonSeq++;   // 使所有挂起 Webtoon 任务过期
            // 释放分页 Image 的 Source（否则 Close 后布局访问已 Dispose 位图 → headless Cleanup 崩溃）
            if (PageImage1 is not null) PageImage1.Source = null;
            if (PageImage2 is not null) PageImage2.Source = null;
            if (_webtoonCanvas is not null && _vm is not null)
            {
                foreach (var ch in _webtoonCanvas.Children.OfType<Image>())
                {
                    var bmp = ch.Source as Avalonia.Media.Imaging.Bitmap;
                    ch.Source = null;
                    _vm.ReleaseUiBitmap(bmp);
                }
                _webtoonCanvas.Children.Clear();
            }
            _webtoonImages.Clear();
            _ = _vm?.DisposeAsync().AsTask();
        }
        catch { }
        base.OnClosed(e);
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

                // 按钮响应测速（问题：点击无反应）：分页模式连点缩放/翻页，测处理耗时
                _vm.SetViewMode(ViewMode.Single);
                await Task.Delay(800);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int clicks = 0;
                for (int i = 0; i < 8; i++)
                {
                    OnZoomInClick(this, new RoutedEventArgs());
                    await Task.Delay(50);
                    clicks++;
                    if (sw.ElapsedMilliseconds > 8000) break;
                }
                sw.Stop();
                sb.AppendLine($"CLICK: {clicks} clicks in {sw.ElapsedMilliseconds}ms scale={CanvasScale:F3} (avg {(double)sw.ElapsedMilliseconds / Math.Max(1, clicks):F1}ms/click)");
                App.SLog($"DIAG clicks={clicks} scale={CanvasScale:F3}");
                // 复位测试
                OnResetZoomClick(this, new RoutedEventArgs());
                await Task.Delay(200);
                sb.AppendLine($"RESET: scale={CanvasScale:F3} tx={CanvasX:F1} ty={CanvasY:F1} pct={_vm.ZoomPercentText}");
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
    /// <summary>Webtoon 滚动速度（1.0 默认；0.5/1/2 三档，工具栏按钮）。</summary>
    public double WebtoonSpeed { get; set; } = 1.0;

    /// <summary>适应屏幕的缩放值（百分比基准）。</summary>
    private double FitScreenScale()
    {
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();
        return Math.Min(vpW / baseW, vpH / BaseH);
    }

    /// <summary>更新缩放百分比显示（基准 = 适应屏幕）。</summary>
    private void UpdateZoomPercent()
    {
        if (_vm is null) return;
        double fitScreen = Math.Max(0.001, FitScreenScale());
        _vm.ZoomPercent = _scale / fitScreen * 100.0;
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
        PageLayer.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _tx, _ty));
    }

    private void Relayout()
    {
        var rsw = System.Diagnostics.Stopwatch.StartNew();
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

        if (_vm.FitMode != FitMode.Custom && _vm.ViewMode != ViewMode.Webtoon)
        {
            // Fit 模式（分页）：计算 scale + 居中 translate
            _scale = CurrentFitScale();
            _tx = (vpW - baseW * _scale) / 2;
            _ty = (vpH - BaseH * _scale) / 2;
            if (Math.Abs(_vm.Scale - 1.0) > 0.001) _vm.ResetScaleInternal();
        }
        // Webtoon 模式：scale 由 EnterWebtoon/ZoomWebtoon 管理（等宽 FitScreen 语义），Relayout 不重置
        // Custom 模式：scale/translate 由画布交互管理，Relayout 不重置
        ApplyTransform();
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            EnterWebtoon();
            if (WebtoonSpeedButton is not null) WebtoonSpeedButton.IsVisible = true;
            if (PairButton is not null) PairButton.IsVisible = false;      // 配对仅双页（BUG-E v1.9）
            if (DirectionButton is not null) DirectionButton.IsVisible = false;
        }
        else
        {
            ExitWebtoon();
            ArrangeDoublePageOrder();
            if (WebtoonSpeedButton is not null) WebtoonSpeedButton.IsVisible = false;
            // BUG-E v1.9：配对按钮仅双页模式显示（单页无配对概念）
            if (PairButton is not null) PairButton.IsVisible = _vm.ViewMode == ViewMode.Double;
            if (DirectionButton is not null) DirectionButton.IsVisible = true;
        }
        // 方向感知翻页按钮 ToolTip（v1.8）
        if (NavPrevButton is not null && NavNextButton is not null && _vm is not null)
        {
            bool rtl = _vm.Direction == ReadingDirection2.Rtl;
            ToolTip.SetTip(NavPrevButton, rtl ? "下一页 (←)" : "上一页 (←)");
            ToolTip.SetTip(NavNextButton, rtl ? "上一页 (→)" : "下一页 (→)");
        }
        UpdateZoomPercent();
        rsw.Stop();
        if (rsw.ElapsedMilliseconds > 15)
            App.SLog($"RELAYOUT SLOW {rsw.ElapsedMilliseconds}ms mode={_vm?.ViewMode}");
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
        UpdateZoomPercent();
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

    /// <summary>测试/按钮用：以内容中心为焦点缩放（factor=1.25/0.8）。Webtoon 走独立路径（BUG-G/I）。</summary>
    public void ZoomRelative(double factor)
    {
        if (_vm?.ViewMode == ViewMode.Webtoon)
        {
            ZoomWebtoon(factor, new Avalonia.Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2));
        }
        else ZoomToFocus(factor, ContentScreenRect().Center);
    }

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
            PageLayer.Width = _webtoonWidth;
            PageLayer.Height = WebtoonTotalHeight;
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
        }
        // BUG-B v1.9：分页模式缩放后不强制居中 —— 保留拖拽/焦点位置（复位按钮可恢复居中）
        if (_vm is not null)
        {
            _vm.SyncScaleFromView(_scale / CurrentFitScale());
            double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
            _vm.DecodeTargetWidth = (int)Math.Clamp(ComputeBaseW() * _scale * dpr, 1200, 4096);
        }
        UpdateZoomPercent();
    }

    // ---------- 输入 ----------
    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e) => Relayout();

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        if (IsOverPreviewStrip(e.Source)) return;   // 预览栏滚轮翻页，不缩放
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || _ctrlDown;
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            if (ctrl)
            {
                // Webtoon：Ctrl+滚轮 = 缩放（锚定鼠标，W-2 修复；不触发滚动）
                double step = 0.05 * Math.Max(0.1, ZoomSensitivity);
                ZoomWebtoon(e.Delta.Y > 0 ? 1.0 + step : 1.0 / (1.0 + step), e.GetPosition(Viewport));
            }
            else
            {
                // Webtoon：滚轮 = 滚动（按事件计数 —— 修复 #9858 Delta 极小导致滚动缓慢）
                ScrollWebtoonByEvent(e);
            }
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

    // ---------- Webtoon 虚拟化（v1.8 重构：等宽 + 前缀和 + 限流 + 先加后删） ----------
    // 参考 mihon/Tachiyomi WebtoonViewer。v1.8 修复：
    //  - W-3 等宽：所有页显示宽度一致（基准 = 进入时视口宽），高度 = 宽/ratio，页位置用前缀和
    //  - W-4 黑屏：先添加新页、再移除远离的旧页（给异步解码留时间，旧页回退显示）
    //  - W-1 卡顿：解码限流（同时最多 2 个任务）+ 范围不变不重建
    //  - W-2 缩放焦点：Webtoon 用鼠标原始位置锚定（不 clamp 到单页）
    private Canvas? _webtoonCanvas;
    private readonly Dictionary<int, Image> _webtoonImages = new();
    private readonly HashSet<int> _webtoonLoading = new();
    private readonly System.Threading.SemaphoreSlim _webtoonDecodeGate = new(2, 2);   // 解码限流
    private double[] _webtoonOffsets = Array.Empty<double>();   // 前缀和（基准坐标）：页 i 顶部 y
    private double _webtoonWidth = 800;                          // 等宽基准（进入时 = 视口宽）
    private int _webtoonVisibleFirst, _webtoonVisibleLast;
    private int _webtoonSeq;
    private const int WebtoonBufferPages = 2;
    private bool _windowClosed;   // 窗口关闭后禁止 Webtoon 异步任务继续操作画布

    /// <summary>Webtoon 总高（基准坐标）。</summary>
    private double WebtoonTotalHeight => _webtoonOffsets.Length > 0
        ? _webtoonOffsets[^1] + _webtoonWidth / Math.Max(0.05, _vm?.RatioOfPage(_webtoonOffsets.Length - 1) ?? 0.7)
        : 0;

    private void EnterWebtoon()
    {
        if (_webtoonCanvas is null)
            _webtoonCanvas = new Canvas { Background = null, Name = "WebtoonCanvas" };
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != _webtoonCanvas)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(_webtoonCanvas);
        }
        if (_vm is null) return;
        double newW = Math.Max(100, Viewport.Bounds.Width);
        bool firstEnter = _webtoonOffsets.Length == 0;
        if (firstEnter)
        {
            // 首次进入（BUG-G/H + 功能-3 v1.9）：默认宽度 = 视口 50%，定位当前逻辑页对应物理页
            _webtoonWidth = Math.Max(100, newW * 0.5);
            _scale = 1.0;
            BuildWebtoonOffsets();
            int firstPhys = _vm.CurrentFirstPhysical;
            _ty = -(PageOffset(firstPhys) * _scale);
            _tx = 0;
        }
        else if (Math.Abs(newW - _webtoonWidth * 2) > 20)
        {
            // 视口尺寸变化（非首次）：重建基准，等比缩放保持页面显示尺寸，滚动位置按比例跟随
            double ratio = newW / Math.Max(1, _webtoonWidth * 2);
            _webtoonWidth = Math.Max(100, newW * 0.5);
            BuildWebtoonOffsets();
            _scale *= ratio;
            _ty *= ratio;
        }
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        _webtoonSeq++;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
    }

    private void ExitWebtoon()
    {
        // 2A-2 修复：退出 Webtoon 必须停止平滑滚动 timer（防空转卡 UI）
        _scrolling = false;
        _scrollVelocity = 0;
        _scrollTimer?.Stop();
        App.SLog($"EXIT_WEBTOON children={_webtoonCanvas?.Children.Count} images={_webtoonImages.Count}");
        if (_vm is not null && _webtoonCanvas is not null)
        {
            // 先置空 Source 再释放引用：确保布局 pass 不再访问将被 Dispose 的位图（Cleanup 崩溃修复）
            foreach (var ch in _webtoonCanvas.Children.OfType<Image>())
            {
                var bmp = ch.Source as Avalonia.Media.Imaging.Bitmap;
                ch.Source = null;
                _vm.ReleaseUiBitmap(bmp);
            }
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

    /// <summary>前缀和：页 i 顶部 y（基准坐标）。</summary>
    private double PageOffset(int physical)
    {
        if (physical <= 0) return 0;
        if (physical < _webtoonOffsets.Length) return _webtoonOffsets[physical];
        return _webtoonOffsets.Length > 0 ? _webtoonOffsets[^1] : 0;
    }

    /// <summary>按当前比例重建前缀和（进入/比例变化时）。</summary>
    private void BuildWebtoonOffsets()
    {
        if (_vm is null) return;
        int n = _vm.PhysicalPageCount;
        var offs = new double[n];
        double acc = 0;
        for (int i = 0; i < n; i++)
        {
            offs[i] = acc;
            acc += _webtoonWidth / Math.Max(0.05, _vm.RatioOfPage(i));
        }
        _webtoonOffsets = offs;
    }

    /// <summary>二分：给定基准 y，返回包含它的页索引。</summary>
    private int PhysicalAt(double baseY)
    {
        int lo = 0, hi = _webtoonOffsets.Length - 1;
        if (hi < 0) return 0;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_webtoonOffsets[mid] <= baseY) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>Webtoon：按滚动位置增量渲染可见页 ± 缓冲（先加后删，防黑屏）。</summary>
    private void UpdateWebtoonPages()
    {
        if (_vm is null || _webtoonCanvas is null || _webtoonOffsets.Length == 0) return;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        double scale = Math.Max(0.001, _scale);
        double baseTop = -_ty / scale;
        double baseBottom = (-_ty + vpH) / scale;
        int n = _vm.PhysicalPageCount;
        int first = Math.Max(0, PhysicalAt(Math.Max(0, baseTop)) - WebtoonBufferPages);
        int last = Math.Min(n - 1, PhysicalAt(Math.Max(0, baseBottom)) + WebtoonBufferPages);
        _webtoonVisibleFirst = first;
        _webtoonVisibleLast = last;

        // 先添加缺失（异步解码；旧页保留作回退显示）
        for (int i = first; i <= last; i++)
        {
            if (!_webtoonImages.ContainsKey(i)) _ = LoadWebtoonImageAsync(i);
        }
        // 再移除"远离可见范围"的旧页（距离 > 缓冲×2 → 已无回退价值）
        foreach (var k in _webtoonImages.Keys.ToList())
        {
            if (k < first - WebtoonBufferPages * 2 || k > last + WebtoonBufferPages * 2)
                RemoveWebtoonImage(k);
        }
        // 当前可见页号 → 状态显示
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, baseTop)), 0, n - 1);
        _vm.UpdateWebtoonPageText(curPhys);
        SyncPreviewStripToScroll();
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
        if (!_webtoonLoading.Add(physical)) return;   // 并发去重
        try
        {
            var mySeq = _webtoonSeq;
            // 解码限流（W-1 卡顿：同一时刻最多 2 个解码任务）
            await _webtoonDecodeGate.WaitAsync();
            try
            {
                var bmp = await _vm.GetPageBitmapAsync(physical);
                if (_windowClosed || mySeq != _webtoonSeq || _vm.ViewMode != ViewMode.Webtoon || _webtoonCanvas is null)
                {
                    _vm.ReleaseUiBitmap(bmp);
                    return;
                }
                if (_webtoonImages.ContainsKey(physical)
                    || physical < _webtoonVisibleFirst || physical > _webtoonVisibleLast)
                {
                    _vm.ReleaseUiBitmap(bmp);
                    return;
                }
                // 等宽：Width = 基准宽，Height = 宽/ratio（前缀和定位）
                double h = _webtoonWidth / Math.Max(0.05, _vm.RatioOfPage(physical));
                var img = new Image
                {
                    Stretch = Stretch.Uniform,
                    Width = _webtoonWidth,
                    Height = h,
                };
                if (bmp is not null) img.Source = bmp;
                Canvas.SetLeft(img, 0);
                Canvas.SetTop(img, PageOffset(physical));
                _webtoonCanvas.Children.Add(img);
                _webtoonImages[physical] = img;
            }
            finally { _webtoonDecodeGate.Release(); }
        }
        finally { _webtoonLoading.Remove(physical); }
    }

    /// <summary>Webtoon：滚动到指定物理页（页顶对齐视口顶，BUG-F v1.9）。</summary>
    private void ScrollWebtoonToPhysical(int physical)
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        int target = Math.Clamp(physical, 0, _vm.PhysicalPageCount - 1);
        _ty = -(PageOffset(target) * _scale);
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        _vm.UpdateWebtoonPageText(target);
    }

    // 平滑滚动 + 惯性（功能-2 v1.9）：滚轮事件累加目标位移，帧循环平滑过渡，停止后指数衰减惯性
    private double _scrollTargetTy;          // 滚动目标位置
    private double _scrollVelocity;          // 当前速度（惯性）
    private bool _scrolling;
    private Avalonia.Threading.DispatcherTimer? _scrollTimer;
    private readonly System.Diagnostics.Stopwatch _scrollWatch = new();

    /// <summary>Webtoon：按事件滚动 —— 平滑 + 惯性（滚轮累加目标，帧循环插值；停止后速度衰减）。</summary>
    private void ScrollWebtoonByEvent(PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        double step = Math.Max(48.0, Viewport.Bounds.Height * 0.2) * Math.Max(0.1, WebtoonSpeed);
        double dy = e.Delta.Y > 0 ? step : -step;
        // 累加到目标位置（平滑滚动）
        _scrollTargetTy = Math.Clamp(_scrollTargetTy + dy, WebtoonMinTy, 0);
        _scrollVelocity = dy * 0.6;   // 记录速度供惯性
        StartSmoothScroll();
        e.Handled = true;
    }

    private double WebtoonMinTy => Math.Min(0, -(WebtoonTotalHeight * _scale - Viewport.Bounds.Height));

    /// <summary>启动帧循环平滑滚动（含惯性衰减）。</summary>
    private void StartSmoothScroll()
    {
        if (_scrolling) return;
        _scrolling = true;
        _scrollWatch.Restart();
        if (_scrollTimer is null)
        {
            _scrollTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += (_, _) => SmoothScrollTick();
        }
        _scrollTimer.Start();
    }

    private void SmoothScrollTick()
    {
        if (_vm is null || _vm.ViewMode != ViewMode.Webtoon || _windowClosed)
        {
            // 2A-1 修复：切出 Webtoon/窗口关闭 → 必须停止 timer，否则每 16ms 空转
            // ClampWebtoonScroll+ApplyTransform+UpdateWebtoonPages（含改 PageText→Relayout 连锁）→ UI 卡死
            _scrolling = false;
            _scrollVelocity = 0;
            _scrollTimer?.Stop();
            return;
        }
        double elapsed = _scrollWatch.Elapsed.TotalSeconds;
        double remaining = _scrollTargetTy - _ty;
        if (Math.Abs(remaining) > 0.5)
        {
            // 平滑插值：每次移动剩余距离的 25%（指数趋近 → 丝滑）+ 速度分量（惯性）
            double move = remaining * 0.25 + _scrollVelocity * elapsed * 10;
            _ty += move;
            _scrollVelocity *= Math.Pow(0.90, elapsed * 60);   // 指数衰减（快速停止）
            _scrollTargetTy = Math.Clamp(_scrollTargetTy, WebtoonMinTy, 0);
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
        }
        else if (Math.Abs(_scrollVelocity) > 1.0)
        {
            // 惯性滑行：速度衰减直到停止
            _ty += _scrollVelocity * elapsed * 10;
            _scrollVelocity *= Math.Pow(0.92, elapsed * 60);
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
        }
        else
        {
            // 停止：收敛到目标
            _ty = _scrollTargetTy;
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
            _scrolling = false;
            _scrollVelocity = 0;
            _scrollTimer?.Stop();
        }
    }

    /// <summary>Webtoon：Ctrl+滚轮缩放（锚定鼠标原始位置 —— W-2 修复，不 clamp 到单页）。</summary>
    private void ZoomWebtoon(double factor, Avalonia.Point mouse)
    {
        double old = Math.Max(0.001, _scale);
        double target = Math.Clamp(old * factor, 0.2, 8.0);
        double r = target / old;
        // 标准锚定：鼠标处内容不动（鼠标在内容上的局部位置随缩放保持）
        _tx = mouse.X - (mouse.X - _tx) * r;
        _ty = mouse.Y - (mouse.Y - _ty) * r;
        _scale = target;
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
    }

    private void ClampWebtoonScroll()
    {
        double totalH = WebtoonTotalHeight * _scale;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        if (totalH <= vpH) _ty = 0;
        else _ty = Math.Clamp(_ty, -(totalH - vpH), 0);
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        LastPressSource = e.Source?.GetType().Name ?? "null";
        LastPressLeft = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        // 只在图片上拖动（看图器行为：空白处不拖）
        if (e.Source is Image && LastPressLeft)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(Viewport);
            e.Handled = true;
            App.SLog("PRESS -> dragging");
        }
    }

    /// <summary>事件源是否落在预览栏覆盖层内（覆盖层方案：PreviewStrip 在 Viewport 内部，
    /// 冒泡事件会到达 Viewport 处理器——滚轮/双击需跳过，避免"翻页+缩放"同时发生）。</summary>
    private bool IsOverPreviewStrip(object? src)
    {
        if (PreviewStrip?.IsVisible != true || src is not Visual v) return false;
        for (Visual? cur = v; cur is not null; cur = cur.GetVisualParent())
            if (cur == PreviewStrip) return true;
        return false;
    }

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e)
    {
        // 右边缘检测（不占布局，画面不遮挡）：鼠标在视口右缘 48px 内 → 唤出预览栏
        var edgePos = e.GetPosition(Viewport);
        _lastMousePos = e.GetPosition(this);
        if (_vm is not null && edgePos.X > Viewport.Bounds.Width - 48 && edgePos.X < Viewport.Bounds.Width)
        {
            if (PreviewStrip?.IsVisible != true) ShowPreviewStrip();
        }
        // 覆盖层方案：PreviewStrip 在 Viewport 内部右侧（宽120），鼠标在其上时
        // 事件冒泡到此——用 PreviewStrip.Bounds.Left 判断是否已离开预览区（含左半），
        // 否则左半区域一悬停就被隐藏（闪烁）。
        else if (PreviewStrip?.IsVisible == true && edgePos.X < PreviewStrip.Bounds.Left - 10)
        {
            PreviewStrip.IsVisible = false;
        }
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

    private void OnZoomInClick(object? sender, RoutedEventArgs e)
    {
        App.SLog($"BTN 放大点击");
        if (_vm?.ViewMode == ViewMode.Webtoon)
        {
            var center = new Avalonia.Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2);
            ZoomWebtoon(1.4, center);
        }
        else ZoomRelative(1.4);
    }

    private static readonly double[] SensLevels = { 0.5, 1.0, 2.0 };
    private int _sensIdx = 1;
    private int _wsIdx = 1;

    private void OnSensitivityClick(object? sender, RoutedEventArgs e)
    {
        _sensIdx = (_sensIdx + 1) % SensLevels.Length;
        ZoomSensitivity = SensLevels[_sensIdx];
        if (SensitivityButton is not null)
            SensitivityButton.Content = $"缩放×{ZoomSensitivity:0.#}";
    }

    private void OnWebtoonSpeedClick(object? sender, RoutedEventArgs e)
    {
        _wsIdx = (_wsIdx + 1) % SensLevels.Length;
        WebtoonSpeed = SensLevels[_wsIdx];
        if (WebtoonSpeedButton is not null)
            WebtoonSpeedButton.Content = $"滚动速度×{WebtoonSpeed:0.#}";
    }

    private void OnZoomOutClick(object? sender, RoutedEventArgs e)
    {
        if (_vm?.ViewMode == ViewMode.Webtoon)
        {
            // BUG-G v1.9：Webtoon 按钮缩放以视口中心为焦点（不附带上下移动）
            var center = new Avalonia.Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2);
            ZoomWebtoon(0.7, center);
        }
        else ZoomRelative(0.7);
    }

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        App.SLog($"BTN 复位点击");
        if (_vm is null) return;
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            ResetWebtoonView();
            return;
        }
        // 复位 = 强制回到适应屏幕（问题1修复：无论当前 FitMode 是否已是 FitScreen，
        // 都要重置 scale/tx/ty 居中——之前只设 FitMode，值相同不触发 Relayout → 拖动后不归位）
        ResetPagedView();
    }

    /// <summary>分页模式复位：适应屏幕 + 居中（拖动/缩放后一键恢复）。</summary>
    private void ResetPagedView()
    {
        if (_vm is null || Viewport is null) return;
        _vm.FitMode = FitMode.FitScreen;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();
        _scale = CurrentFitScale();
        _tx = (vpW - baseW * _scale) / 2;
        _ty = (vpH - BaseH * _scale) / 2;
        ApplyTransform();
        if (Math.Abs(_vm.Scale - 1.0) > 0.001) _vm.ResetScaleInternal();
        UpdateZoomPercent();
        App.SLog($"RESET paged scale={_scale:F3} tx={_tx:F1} ty={_ty:F1}");
    }

    /// <summary>Webtoon 复位（BUG-H v1.9）：缩放回默认（宽度 50% 视口）+ 画面平移到当前可见页中央（不跳页）。</summary>
    private void ResetWebtoonView()
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        // 记录当前可见页（滚动位置中央）
        double baseCenter = (-_ty + Viewport.Bounds.Height / 2) / Math.Max(0.001, _scale);
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, baseCenter)), 0, _vm.PhysicalPageCount - 1);
        // 默认宽度 = 视口 50%
        _webtoonWidth = Math.Max(100, Viewport.Bounds.Width * 0.5);
        BuildWebtoonOffsets();
        _scale = 1.0;
        // 平移到该页中央
        double pageTop = PageOffset(curPhys);
        double pageH = _webtoonWidth / Math.Max(0.05, _vm.RatioOfPage(curPhys));
        _ty = -(pageTop + pageH / 2 - Viewport.Bounds.Height / 2);
        _tx = 0;
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();
        App.SLog($"WEBTOON RESET width={_webtoonWidth:F0} scale={_scale:F3} ty={_ty:F1} page={curPhys}");
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
        if (IsOverPreviewStrip(e.Source)) return;   // 预览栏内双击不缩放
        double fit = CurrentFitScale();
        double target = Math.Abs(_scale - fit) < 0.02 ? fit * 2.0 : fit;   // Fit ↔ 200%
        ZoomTo(target, e.GetPosition(Viewport));
        e.Handled = true;
    }


    /// <summary>进度条拖动/点击 → 跳页（拖动中实时更新，松手才真正跳转避免逐页解码）。</summary>
    // ---------- 底部悬浮预览栏（v2.1 重构：修 CPU/内存拉满 + 缩略图不显示） ----------
    /// <summary>预览栏缩略图条目（ObservableCollection 自动刷新）。</summary>
    public sealed class ThumbStripItem : ViewModelBase
    {
        public int Physical { get; }
        public string PageLabel { get; }
        public double CardWidth { get; }
        public double CardHeight { get; }
        private Avalonia.Media.Imaging.Bitmap? _bitmap;
        public Avalonia.Media.Imaging.Bitmap? Bitmap { get => _bitmap; set { _bitmap = value; OnPropertyChanged(); } }
        private bool _isCurrent;
        /// <summary>是否为当前阅读页（预览栏高亮：卡片边框 + 页码变色）。</summary>
        public bool IsCurrent { get => _isCurrent; set { _isCurrent = value; OnPropertyChanged(); } }
        public ThumbStripItem(int physical, double cardW, double cardH)
        {
            Physical = physical;
            PageLabel = $"{physical + 1}";
            CardWidth = cardW;
            CardHeight = cardH;
        }
    }

    private Avalonia.Threading.DispatcherTimer? _hoverTimer;
    private readonly System.Collections.ObjectModel.ObservableCollection<ThumbStripItem> _thumbItems = new();
    private int _thumbCenterPhysical;
    private int _thumbSeq;   // 刷新序列号：只认最新一次，防并发叠加
    private Avalonia.Point _lastMousePos;

    /// <summary>热区进入触发（PointerEntered 只触发一次，不再每帧重置 → 立即 400ms 唤出）。</summary>


    private void OnPreviewStripPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastMousePos = e.GetPosition(this);
        StopHoverTimer();
    }

    private void OnPreviewStripPointerExited(object? sender, PointerEventArgs e) => HidePreviewStripDelayed();

    private void StartHoverTimer(int ms, Action action)
    {
        if (_hoverTimer is null)
        {
            _hoverTimer = new Avalonia.Threading.DispatcherTimer();
            _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); _hoverAction?.Invoke(); };
        }
        _hoverAction = action;
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(ms);
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }
    private Action? _hoverAction;

    private void StopHoverTimer() => _hoverTimer?.Stop();

    private void HidePreviewStripDelayed()
    {
        StartHoverTimer(300, () => { if (!IsPointerOver(PreviewStrip)) PreviewStrip.IsVisible = false; });
    }

    private bool IsPointerOver(Avalonia.Controls.Control? ctl)
    {
        if (ctl is null || !ctl.IsVisible) return false;
        var origin = ctl.TranslatePoint(new Avalonia.Point(0, 0), this);
        var rel = origin is { } o ? _lastMousePos - o : default;
        return ctl.Bounds.Contains(rel);
    }

    private void ShowPreviewStrip()
    {
        if (_vm is null || PreviewStrip is null) return;
        _thumbCenterPhysical = _vm.CurrentPhysical;
        PreviewStrip.IsVisible = true;
        RefreshThumbStrip();
    }

    /// <summary>加载 7 张缩略图（小图解码，seq 防并发，ObservableCollection 自动刷新）。</summary>
    private async void RefreshThumbStrip()
    {
        if (_vm is null || ThumbStrip is null || !PreviewStrip.IsVisible) return;
        int mySeq = ++_thumbSeq;
        int total = _vm.PhysicalPageCount;
        int center = Math.Clamp(_thumbCenterPhysical, 0, total - 1);
        int start = Math.Max(0, center - 3);
        int end = Math.Min(total - 1, center + 3);
        while (end - start + 1 < 7 && start > 0) start--;
        while (end - start + 1 < 7 && end < total - 1) end++;

        // 卡片尺寸：宽 96，高 = min(窗口高/7 - 间距, 130)，下限 80
        double cardH = Math.Clamp((Viewport.Bounds.Height - 32 * 2 - 6 * 6) / 7.0, 80, 130);
        int curPhys = _vm.CurrentPhysical;
        _thumbItems.Clear();
        for (int i = start; i <= end; i++)
            _thumbItems.Add(new ThumbStripItem(i, 96, cardH) { IsCurrent = i == curPhys });
        ThumbStrip.ItemsSource = _thumbItems;

        // 小图解码（~150px，快），seq 过期则丢弃
        for (int i = start; i <= end; i++)
        {
            int phys = i;
            var bmp = await _vm.GetThumbnailAsync(phys);
            if (mySeq != _thumbSeq || !PreviewStrip.IsVisible) return;
            var item = _thumbItems.FirstOrDefault(t => t.Physical == phys);
            if (item is not null) item.Bitmap = bmp;
        }
    }

    /// <summary>点击缩略图 → 跳转（三模式映射）。</summary>
    private void OnThumbStripItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ThumbStripItem item } && _vm is not null)
        {
            _vm.JumpToPhysicalCommand.Execute(item.Physical);
            if (_vm.ViewMode == ViewMode.Webtoon) ScrollWebtoonToPhysical(item.Physical);
            PreviewStrip.IsVisible = false;
            App.SLog($"PREVIEW_JUMP -> physical {item.Physical + 1}");
        }
    }

    private void OnPreviewStripPrev(object? sender, RoutedEventArgs e)
    {
        _thumbCenterPhysical = Math.Max(0, _thumbCenterPhysical - 7);
        RefreshThumbStrip();
    }

    private void OnPreviewStripNext(object? sender, RoutedEventArgs e)
    {
        _thumbCenterPhysical = Math.Min((_vm?.PhysicalPageCount ?? 1) - 1, _thumbCenterPhysical + 7);
        RefreshThumbStrip();
    }

    private void OnPreviewStripWheel(object? sender, PointerWheelEventArgs e)
    {
        _thumbCenterPhysical = Math.Clamp(_thumbCenterPhysical + (e.Delta.Y > 0 ? -1 : 1), 0, (_vm?.PhysicalPageCount ?? 1) - 1);
        RefreshThumbStrip();
        e.Handled = true;
    }

    /// <summary>滚动模式双向联动：正文滚动时更新预览栏高亮。</summary>
    private void SyncPreviewStripToScroll()
    {
        if (PreviewStrip?.IsVisible == true && _vm?.ViewMode == ViewMode.Webtoon)
        {
            int cur = _vm.CurrentPhysical;
            if (Math.Abs(cur - _thumbCenterPhysical) >= 3)
            {
                _thumbCenterPhysical = cur;
                RefreshThumbStrip();
            }
        }
    }



    private void OnNavPrevClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (_vm.Direction == ReadingDirection2.Rtl) _vm.NextPageCommand.Execute(null);
        else _vm.PrevPageCommand.Execute(null);
    }

    private void OnNavNextClick(object? sender, RoutedEventArgs e)
    {
        App.SLog($"BTN 下一页点击");
        if (_vm is null) return;
        if (_vm.Direction == ReadingDirection2.Rtl) _vm.PrevPageCommand.Execute(null);
        else _vm.NextPageCommand.Execute(null);
    }

    /// <summary>阅读模式下拉菜单。</summary>
    private void OnModeMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b) b.ContextMenu?.Open(b);
    }

    private void OnDirectionMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b) b.ContextMenu?.Open(b);
    }

    private void OnDirectionMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not MenuItem { Tag: string tag }) return;
        var target = tag == "Rtl" ? ReadingDirection2.Rtl : ReadingDirection2.Ltr;
        _vm.SetDirection(target);
    }

    private void OnModeMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not MenuItem { Tag: string tag }) return;
        var target = tag switch
        {
            "Single" => ViewMode.Single,
            "Double" => ViewMode.Double,
            _ => ViewMode.Webtoon,
        };
        _vm.SetViewMode(target);
    }





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

    /// <summary>方向感知翻页（v1.8）：RTL 时 ◀=下一页、▶=上一页；ToolTip 随方向显示。</summary>


    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
                // F12 调试面板必须在任何状态都能打开（不受 _vm null 限制）
        if (e.Key == Key.F12) { ToggleDebugPanel(); e.Handled = true; return; }
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

    /// <summary>Webtoon：按页滚动（1 页 = 一屏高，-1 向上）—— 平滑过渡。</summary>
    private void ScrollWebtoonPages(int dir)
    {
        if (_vm is null) return;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        _scrollTargetTy = Math.Clamp(_scrollTargetTy + dir * vpH, WebtoonMinTy, 0);
        _scrollVelocity = dir * vpH * 0.4;
        StartSmoothScroll();
    }
}
