using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using MangaAgent.App.Services;
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

    /// <summary>测试用：滚动模式滚动到指定物理页。</summary>
    public void CallScrollWebtoonToPhysical(int physical) => ScrollWebtoonToPhysical(physical);

    /// <summary>测试用：滚动模式复位（当前页适应屏幕+居中）。</summary>
    public void CallResetWebtoonView() => ResetWebtoonView();

    /// <summary>测试用：滚动模式默认视图（指定页适应屏幕+居中）。</summary>
    public void CallFitWebtoonToPage(int physical) => FitWebtoonToPage(physical);

    /// <summary>测试用：滚动模式当前滚动速度（px/s）。</summary>
    public double WebtoonScrollVelocity => _scrollVelocity;

    /// <summary>测试用：当前可见页范围。</summary>
    public string WebtoonVisibleRange => $"{_webtoonVisibleFirst}..{_webtoonVisibleLast}";
    /// <summary>测试用：占位块数量（防黑屏断言）。</summary>
    public int WebtoonPlaceholderCount => _webtoonPlaceholders.Count;
    /// <summary>测试用：滚动模式基准宽（画布无旧宽度残留断言）。</summary>
    public double WebtoonWidthValue => _webtoonWidth;

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
    private double _dragVelocity;   // 拖拽瞬时速度（px/s，释放甩动用）
    private long _lastDragTicks;
    private double _lastDragY;

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
        // v3 健壮性：窗口失活时复位 Ctrl 状态（Ctrl 在窗外松开会漏 KeyUp → 滚轮被误判为缩放）
        Deactivated += (_, _) => _ctrlDown = false;
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
                // v3：滚动模式按"适应屏幕/适应高度/适应宽度"（CycleFit 改 FitMode）→ 与单页语义一致：
                // FitWidth=条宽铺满视口 / FitHeight=当前页高铺满视口 / FitScreen=当前页完全可见+居中
                if (args.PropertyName == nameof(ReaderViewModel.FitMode) && vm.ViewMode == ViewMode.Webtoon)
                {
                    switch (vm.FitMode)
                    {
                        case FitMode.FitWidth: FitWebtoonWidth(); break;
                        case FitMode.FitHeight: FitWebtoonHeight(); break;
                        default: ResetWebtoonView(); break;   // FitScreen
                    }
                }
            };
            Relayout();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Relayout();
        ApplySettings();   // 设置基准/手感/性能项应用到运行时
        // 方案A：打开时焦点落在阅读区（Focusable 中性控件，无白框、不消费翻页键）
        Viewport.Focus();
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
            _webtoonPlaceholders.Clear();
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

    /// <summary>更新缩放百分比显示。分页基准 = 适应屏幕；滚动模式 fit=100%（与单页一致，v3）。</summary>
    private void UpdateZoomPercent()
    {
        if (_vm is null) return;
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            _vm.ZoomPercent = _scale * 100.0;
            return;
        }
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
            _vm.DecodeTargetWidth = (int)Math.Clamp(ComputeBaseW() * _scale * dpr, 1200, _maxDecodeWidth);
            // v3 防放大模糊：当前页缓存宽度不足 → 立即重解码（分页模式）
            if (_vm.CurrentPageNeedsRedecode(_vm.DecodeTargetWidth)) _vm.RefreshCurrentPage();
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
            _vm.DecodeTargetWidth = (int)Math.Clamp(ComputeBaseW() * _scale * dpr, 1200, _maxDecodeWidth);
            // v3 防放大模糊：当前页缓存宽度不足 → 立即重解码（分页模式）
            if (_vm.CurrentPageNeedsRedecode(_vm.DecodeTargetWidth)) _vm.RefreshCurrentPage();
        }
        UpdateZoomPercent();
    }

    // ---------- 输入 ----------
    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Relayout();
        // 预览栏可见时尺寸变化 → 重新计算张数（全屏/拖窗切换后立即正确）
        if (PreviewStrip?.IsVisible == true) RefreshThumbStrip();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        if (IsOverPreviewStrip(e.Source)) return;   // 预览栏滚轮翻页，不缩放
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || _ctrlDown;
        App.SLog($"WHEEL_EVT mode={_vm.ViewMode} dy={e.Delta.Y:F1} ctrl={ctrl}");   // F12 诊断：事件是否到达
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
        // 分页模式滚轮：设置可选"缩放"（默认，按事件计数，完全不依赖 Delta 数值）或"翻页"
        if (_pagedWheelMode == "Page")
        {
            // 翻页：向下滚=下一页（阅读顺序），向上滚=上一页
            if (e.Delta.Y < 0) _vm.NextPageCommand.Execute(null);
            else if (e.Delta.Y > 0) _vm.PrevPageCommand.Execute(null);
            e.Handled = true;
            return;
        }
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
    private bool _webtoonActive;   // 是否处于滚动模式会话（进入时重设默认视图，防跨模式状态残留）
    private Canvas? _webtoonCanvas;
    private readonly Dictionary<int, Image> _webtoonImages = new();
    private readonly Dictionary<int, Border> _webtoonPlaceholders = new();   // 解码前占位块（防黑屏）
    private readonly HashSet<int> _webtoonLoading = new();
    private readonly System.Threading.SemaphoreSlim _webtoonDecodeGate = new(2, 2);   // 解码限流
    private double[] _webtoonOffsets = Array.Empty<double>();   // 前缀和（基准坐标）：页 i 顶部 y
    private double _webtoonWidth = 800;                          // 等宽基准（进入时按当前页适应屏幕计算）
    private double _webtoonBaseViewportW;                        // 布局基准视口宽（尺寸变化检测，防误判重建）
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
        if (!_webtoonActive)
        {
            // 任何进入（含首次/再次/切回）：恢复默认视图 = 当前页适应屏幕 + 水平垂直居中 + scale=1
            // （scroll-mode-fix v2：根治"再次进入携带分页残留缩放/平移 → 放大+贴左"）
            _webtoonActive = true;
            _webtoonSeq++;   // 结构变化：作废可能在途的任务
            FitWebtoonToPage(_vm.CurrentFirstPhysical);
            return;
        }
        if (Math.Abs(newW - _webtoonBaseViewportW) > 20)
        {
            // 视口尺寸变化：保持页面显示尺寸不变（不改 _webtoonWidth/_scale），
            // 按新视口重建布局 + clamp + 刷新虚拟化；结构变化 → seq 作废在途任务
            _webtoonSeq++;
            _webtoonBaseViewportW = newW;
            BuildWebtoonOffsets();
            ClearWebtoonPlaceholders();
            PageLayer.Width = _webtoonWidth;
            PageLayer.Height = WebtoonTotalHeight;
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
            return;
        }
        // 无结构变化：幂等保持现状（不作废在途任务 —— 防黑屏，scroll-mode-fix A）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
    }

    /// <summary>滚动模式"适应高度"（v3.2）：与单页 FitHeight 一致 —— 当前页高 = 视口高
    /// （条宽 = 视口高×当前页比例；横版页宽可超视口），当前页顶对齐视口顶。</summary>
    private void FitWebtoonHeight()
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, (-_ty + vpH / 2) / Math.Max(0.001, _scale))),
            0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();
        _webtoonSeq++;                       // 结构变化：作废在途解码
        _webtoonWidth = Math.Max(50, vpH * Math.Max(0.05, _vm.RatioOfPage(curPhys)));   // 页高 = 视口高
        _webtoonBaseViewportW = vpW;
        BuildWebtoonOffsets();
        ClearWebtoonImages();                // 旧宽度图清除重建（防错位）
        ClearWebtoonPlaceholders();
        _scale = 1.0;
        _tx = 0;                             // 窄于视口由 ClampWebtoonScroll 水平居中
        _ty = -(PageOffset(curPhys));        // 当前页顶对齐视口顶（单页 FitHeight 语义）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();
        App.SLog($"WEBTOON FITHEIGHT page={curPhys} width={_webtoonWidth:F0} ty={_ty:F1}");
    }

    /// <summary>滚动模式"适应宽度"（v3.1）：条宽 = 视口宽（铺满，与单页 FitWidth 一致），
    /// 当前可见页顶对齐视口顶。</summary>
    private void FitWebtoonWidth()
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, (-_ty + vpH / 2) / Math.Max(0.001, _scale))),
            0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();
        _webtoonSeq++;                       // 结构变化：作废在途解码
        _webtoonWidth = vpW;                 // 铺满视口宽
        _webtoonBaseViewportW = vpW;
        BuildWebtoonOffsets();
        ClearWebtoonImages();                // 旧宽度图清除重建（防错位）
        ClearWebtoonPlaceholders();
        _scale = 1.0;
        _tx = 0;                             // 铺满 → 无需水平居中
        _ty = -(PageOffset(curPhys));        // 当前页顶对齐视口顶（单页 FitWidth 语义）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();
        App.SLog($"WEBTOON FITWIDTH page={curPhys} width={_webtoonWidth:F0} ty={_ty:F1}");
    }

    /// <summary>滚动模式默认视图：当前页适应屏幕（完全可见）+ 水平垂直居中，与单页 FitScreen 语义一致。
    /// 进入滚动模式与复位/适应屏幕共用（scroll-mode-fix C / v3）。
    /// v3：宽度重算后先清空旧尺寸 Image 与占位再重建（否则旧图原位残留 → 错位"两页之间"）。</summary>
    private void FitWebtoonToPage(int physical)
    {
        if (_vm is null) return;
        physical = Math.Clamp(physical, 0, _vm.PhysicalPageCount - 1);
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double ratio = Math.Max(0.05, _vm.RatioOfPage(physical));
        // 当前页完全可见：宽 = min(视口宽, 页高×ratio)，高 = 宽/ratio ≤ 视口高
        _webtoonWidth = Math.Min(vpW, vpH * ratio);
        _webtoonBaseViewportW = vpW;
        BuildWebtoonOffsets();
        ClearWebtoonImages();          // 旧宽度图全部释放移除（防几何错位）
        ClearWebtoonPlaceholders();    // 旧尺寸占位清理，随 UpdateWebtoonPages 重建
        _scale = 1.0;
        double pageTop = PageOffset(physical);
        double pageH = _webtoonWidth / ratio;
        _tx = (vpW - _webtoonWidth) / 2;                 // 水平居中
        _ty = -(pageTop + pageH / 2 - vpH / 2);          // 垂直居中（首尾页由 clamp 兜底）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();
    }

    /// <summary>清空滚动模式全部实例化图（宽度/几何结构变化时调用，占位保证不黑屏）。
    /// v3：同时重置范围守卫（否则重进滚动模式时范围未变 → 加载循环被跳过 → 画布空）。</summary>
    private void ClearWebtoonImages()
    {
        if (_vm is not null && _webtoonCanvas is not null)
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
        _webtoonLastFirst = -1;
        _webtoonLastLast = -1;
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
        ClearWebtoonPlaceholders();
        _webtoonActive = false;
        _webtoonSeq++;
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != PagePanel)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(PagePanel);
        }
        PageLayer.Height = BaseH;
        PagePanel.Orientation = Orientation.Horizontal;
    }

    /// <summary>清空占位块（画布结构变化/退出时调用，占位随宽高重算重建）。</summary>
    private void ClearWebtoonPlaceholders()
    {
        if (_webtoonCanvas is not null)
            foreach (var b in _webtoonPlaceholders.Values)
                _webtoonCanvas.Children.Remove(b);
        _webtoonPlaceholders.Clear();
    }

    /// <summary>占位块：已入可见范围但未解码的页显示灰底+页码（scroll-mode-fix v2：任何时刻不黑屏）。
    /// v3：底色/文字提亮（#3A3A44+#CCC）更易辨识为"加载中"而非坏图。</summary>
    private void EnsureWebtoonPlaceholder(int physical)
    {
        if (_webtoonPlaceholders.ContainsKey(physical) || _webtoonCanvas is null || _vm is null) return;
        double h = _webtoonWidth / Math.Max(0.05, _vm.RatioOfPage(physical));
        var border = new Border
        {
            Width = _webtoonWidth,
            Height = h,
            Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A44")),
            Child = new TextBlock
            {
                Text = $"{physical + 1}",
                Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse("#CCCCCC")),
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(border, 0);
        Canvas.SetTop(border, PageOffset(physical));
        _webtoonCanvas.Children.Add(border);
        _webtoonPlaceholders[physical] = border;
    }

    private void RemoveWebtoonPlaceholder(int physical)
    {
        if (_webtoonPlaceholders.Remove(physical, out var b))
            _webtoonCanvas?.Children.Remove(b);
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

    private int _webtoonLastFirst = -1, _webtoonLastLast = -1;   // 范围变化守卫（v3 优化）
    private readonly Dictionary<int, int> _webtoonFailCounts = new();   // 解码失败计数（有限重试）
    private const int MaxDecodeRetries = 3;                             // 失败重试上限

    /// <summary>Webtoon：按滚动位置增量渲染可见页 ± 缓冲（先加后删，防黑屏）。
    /// v3：范围未变跳过重活；可见页放大清晰度检查（重解码原位替换）始终执行。</summary>
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

        bool rangeChanged = first != _webtoonLastFirst || last != _webtoonLastLast;
        if (rangeChanged)
        {
            _webtoonLastFirst = first;
            _webtoonLastLast = last;
            // 先添加缺失（异步解码；旧页保留作回退显示）
            for (int i = first; i <= last; i++)
            {
                if (!_webtoonImages.ContainsKey(i)) _ = LoadWebtoonImageAsync(i);
            }
            // 占位块：可见范围内未解码的页立即显示灰底+页码（任何时刻不黑屏，scroll-mode-fix v2）
            for (int i = first; i <= last; i++)
                if (!_webtoonImages.ContainsKey(i)) EnsureWebtoonPlaceholder(i);
            // 移除已离开可见范围的占位 + 重置失败计数（翻远重来可重新重试）
            foreach (var k in _webtoonPlaceholders.Keys.ToList())
                if (k < first || k > last) RemoveWebtoonPlaceholder(k);
            foreach (var k in _webtoonFailCounts.Keys.ToList())
                if (k < first || k > last) _webtoonFailCounts.Remove(k);
            // 再移除"远离可见范围"的旧页（距离 > 缓冲×2 → 已无回退价值）
            foreach (var k in _webtoonImages.Keys.ToList())
            {
                if (k < first - WebtoonBufferPages * 2 || k > last + WebtoonBufferPages * 2)
                    RemoveWebtoonImage(k);
            }
        }
        // 放大清晰度（v3）：可见页缓存宽度不足显示需求 → 移除旧图并按新宽度重请求（防放大模糊）
        for (int i = first; i <= last; i++)
            if (_webtoonImages.ContainsKey(i)) TryRefreshWebtoonImage(i);
        // 当前可见页号 → 状态显示（v3.3：页未变不更新 PageText —— 防每帧按钮重评估/布局，提升滑动帧率）
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, baseTop)), 0, n - 1);
        if (curPhys != _lastWebtoonPageText)
        {
            _lastWebtoonPageText = curPhys;
            _vm.UpdateWebtoonPageText(curPhys);
        }
        SyncPreviewStripToScroll();
    }

    /// <summary>放大清晰度检查：可见页缓存宽度 < 显示需求×0.85 → 移除旧图 + 占位 + 立即按新宽度重请求。
    /// v3 采用"移除重建"而非原位替换 Source（原位替换会因渲染管线仍持旧位图引用而崩溃）；
    /// RemoveWebtoonImage 已保证"先移出画布再释放"（防淘汰后直接 Dispose 被渲染位图）。</summary>
    private void TryRefreshWebtoonImage(int physical)
    {
        if (_vm is null) return;
        double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
        int want = (int)Math.Clamp(Math.Ceiling(_webtoonWidth * _scale * dpr), 400, Math.Max(1200, _maxDecodeWidth));
        if (_vm.HasCachedWidth(physical, want)) return;   // 已够清晰
        RemoveWebtoonImage(physical);            // 先移出画布再释放旧位图
        EnsureWebtoonPlaceholder(physical);      // 占位防黑
        _ = LoadWebtoonImageAsync(physical);     // 按当前 want 重解码
    }

    private void RemoveWebtoonImage(int physical)
    {
        if (_webtoonImages.Remove(physical, out var img))
        {
            // v3 顺序修复：先移出画布（脱离渲染树）再释放位图 —— 缓存淘汰可能已移除该页缓存条目，
            // 若先 Release 会直接 Dispose 仍被渲染树引用的位图 → 下一布局 Image.MeasureOverride 崩溃
            _webtoonCanvas?.Children.Remove(img);
            _vm?.ReleaseUiBitmap(img.Source as Avalonia.Media.Imaging.Bitmap);
        }
        RemoveWebtoonPlaceholder(physical);
        _webtoonFailCounts.Remove(physical);
    }

    private async Task LoadWebtoonImageAsync(int physical)
    {
        if (_vm is null || _webtoonCanvas is null) return;
        if (!_webtoonLoading.Add(physical)) return;   // 并发去重
        bool rescheduled = false;
        try
        {
            var mySeq = _webtoonSeq;
            // 解码限流（W-1 卡顿：同一时刻最多 2 个解码任务）
            await _webtoonDecodeGate.WaitAsync();
            try
            {
                // 按显示宽度解码（scroll-mode-fix v2）：fit 时 4299px 大图只解 ~540px → 提速 4-8 倍
                double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
                int want = (int)Math.Clamp(Math.Ceiling(_webtoonWidth * _scale * dpr), 400, Math.Max(1200, _maxDecodeWidth));
                var bmp = await _vm.GetPageBitmapAsync(physical, want);
                if (bmp is null)
                {
                    // v3 黑页根治：解码失败 → 不创建空 Image（否则登记后永不重请求 → 永久黑块），
                    // 保留占位块 + 有限重试（≤3 次，800ms 间隔；翻远重来重置计数）
                    if (mySeq == _webtoonSeq && !_windowClosed && _vm.ViewMode == ViewMode.Webtoon
                        && physical >= _webtoonVisibleFirst && physical <= _webtoonVisibleLast
                        && !_webtoonImages.ContainsKey(physical))
                    {
                        int fails = _webtoonFailCounts.TryGetValue(physical, out var f) ? f : 0;
                        if (fails < MaxDecodeRetries)
                        {
                            _webtoonFailCounts[physical] = fails + 1;
                            _ = RetryWebtoonImageAsync(physical);
                        }
                    }
                    return;
                }
                if (_windowClosed || _vm.ViewMode != ViewMode.Webtoon || _webtoonCanvas is null)
                {
                    _vm.ReleaseUiBitmap(bmp);
                    return;
                }
                if (mySeq != _webtoonSeq)
                {
                    // 作废补偿（scroll-mode-fix A）：解码期间结构已变化（seq 作废）→ 位图丢弃；
                    // 若该页仍应在可见范围，让出 loading 标记并重新请求 —— 防止"请求被丢 → 画布空 → 黑屏"。
                    _vm.ReleaseUiBitmap(bmp);
                    if (physical >= _webtoonVisibleFirst && physical <= _webtoonVisibleLast)
                    {
                        rescheduled = true;
                        _webtoonLoading.Remove(physical);
                        _ = LoadWebtoonImageAsync(physical);
                    }
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
                img.Source = bmp;
                Canvas.SetLeft(img, 0);
                Canvas.SetTop(img, PageOffset(physical));
                _webtoonCanvas.Children.Add(img);
                _webtoonImages[physical] = img;
                _webtoonFailCounts.Remove(physical);
                RemoveWebtoonPlaceholder(physical);   // 图到位，撤占位
            }
            finally { _webtoonDecodeGate.Release(); }
        }
        finally { if (!rescheduled) _webtoonLoading.Remove(physical); }
    }

    /// <summary>解码失败有限重试（800ms 后重请求；状态已变则放弃）。</summary>
    private async Task RetryWebtoonImageAsync(int physical)
    {
        try { await Task.Delay(800); } catch { return; }
        if (_windowClosed || _vm is null || _vm.ViewMode != ViewMode.Webtoon || _webtoonCanvas is null) return;
        if (physical < _webtoonVisibleFirst || physical > _webtoonVisibleLast) return;
        if (_webtoonImages.ContainsKey(physical)) return;
        _ = LoadWebtoonImageAsync(physical);
    }

    /// <summary>Webtoon：滚动到指定物理页（页顶对齐视口顶，BUG-F v1.9）。
    /// scroll-mode-fix v2：先作废在途解码（新范围立即解码，不排队）→ 跳页即时不黑屏。</summary>
    private void ScrollWebtoonToPhysical(int physical)
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        int target = Math.Clamp(physical, 0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();   // 跳页：取消残余惯性，直接定位
        _webtoonSeq++;          // 作废旧范围在途解码（作废补偿只补可见范围页）
        _ty = -(PageOffset(target) * _scale);
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        _vm.UpdateWebtoonPageText(target);
    }

    // 平滑滚动 + 惯性（scroll-mode-fix B：纯动量模型，手机长页面式）
    // 滚轮输入（scroll-mode-fix v3.3，自适应一档归一化）：
    //   - 设计目标：**每个物理档位（用户一次滚轮/一次触摸板手势）滚动 ≈ WheelNotchScreenRatio 视口高**；
    //   - 设备差异：不同鼠标/触摸板每物理档的 Delta 总量不同（标准 120、高分辨率 1~40 且分多事件），
    //     直接按 120 归一化会让弱 Delta 设备每档只滚一小段（"和设计不符"的根因）；
    //   - 自适应：按"事件间隔 > 100ms"切分物理档，每档累积的 |Δ| 即该设备一档的 Delta 总量，
    //     用 EMA 估计（_wheelNotchDelta），随后按估计值归一化 → 任何设备每物理档都滚 ≈ 一档距离；
    //   - 动量滑行保留：每事件施速度脉冲 → 指数摩擦衰减缓慢停住；连滚累积速度；上限防失控。
    private double _scrollVelocity;          // 当前速度（px/s，正=向上/ty 增大）
    private bool _scrolling;
    private Avalonia.Threading.DispatcherTimer? _scrollTimer;
    private readonly System.Diagnostics.Stopwatch _scrollWatch = new();
    private long _lastTickLog;               // 滚动过程周期性日志（F12 诊断）
    private double _wheelNotchDelta = 120.0; // 设备每物理档的 Delta 总量（自适应估计，初始 Windows 标准）
    private double _wheelBurstAccum;         // 当前物理档的 Delta 累积
    private long _wheelBurstLastMs;          // 上一滚轮事件时间（物理档切分）
    private int _lastWebtoonPageText = -1;   // PageText 更新节流（防每帧按钮重评估）
    private const double MaxScrollVelocity = 6000.0;  // 速度上限（防快速连滚失控）
    private const double ScrollStopEps = 8.0;         // 停止速度阈值（px/s）
    private const double WheelNotchScreenRatio = 1.2; // 每物理档滚动距离 = 120% 视口高（默认手感，可调）

    // —— 以下手感参数由设置（settings.json）驱动，默认值对应设置默认 ——
    private double _scrollFriction = 6.0;             // 停止快慢（ScrollFriction 设置）
    private double _dragFlingThreshold = 150.0;       // 甩动触发阈值（设置）
    private double _dragFlingMax = 3000.0;            // 甩动限幅（设置）
    private int _maxDecodeWidth = 2048;               // 解码宽度上限（设置）
    private bool _invertScroll;                       // 滚动方向反转（设置）
    private string _pagedWheelMode = "Zoom";          // 分页滚轮：Zoom | Page（设置）

    // —— 设置（settings.json）——
    private AppSettings _settings = SettingsStore.Load();
    private double _zoomSensitivityBase = 1.0;        // 缩放速度基准
    private double _webtoonSpeedBase = 1.0;           // 滚动速度基准

    /// <summary>应用设置到运行时参数（打开/设置保存后调用）。</summary>
    private void ApplySettings()
    {
        _zoomSensitivityBase = Math.Clamp(_settings.ZoomSensitivity, 0.1, 4.0);
        _webtoonSpeedBase = Math.Clamp(_settings.WebtoonSpeed, 0.1, 4.0);
        ZoomSensitivity = _zoomSensitivityBase * SensLevels[_sensIdx];
        WebtoonSpeed = _webtoonSpeedBase * SensLevels[_wsIdx];
        _scrollFriction = Math.Clamp(_settings.ScrollFriction, 2.0, 15.0);
        _dragFlingThreshold = Math.Clamp(_settings.DragFlingThreshold, 30, 800);
        _dragFlingMax = Math.Clamp(_settings.DragFlingMax, 500, 9000);
        _maxDecodeWidth = Math.Clamp(_settings.MaxDecodeWidth, 1200, 4096);
        _invertScroll = _settings.InvertScroll;
        _pagedWheelMode = _settings.PagedWheelMode == "Page" ? "Page" : "Zoom";
        if (_vm is not null) _vm.CacheCap = Math.Clamp(_settings.CacheCap, 4, 30);
        if (SensitivityButton is not null) SensitivityButton.Content = $"缩放速度×{SensLevels[_sensIdx]:0.#}";
        if (WebtoonSpeedButton is not null) WebtoonSpeedButton.Content = $"滚动速度×{SensLevels[_wsIdx]:0.#}";
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new Views.SettingsDialog(_settings);
        await dialog.ShowDialog(this);
        _settings = SettingsStore.Load();   // 对话框保存后重新加载
        ApplySettings();
    }

    /// <summary>Webtoon：滚轮事件 —— 自适应一档归一化（每物理档 ≈ 120% 视口高，事件进 F12 日志）。</summary>
    private void ScrollWebtoonByEvent(PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        double dy = e.Delta.Y * (_invertScroll ? -1.0 : 1.0);
        if (dy == 0) { e.Handled = true; return; }
        long now = Environment.TickCount64;
        // 物理档切分：与上一事件间隔 > 100ms → 结束上一档并更新该设备的"一档 Delta"估计
        if (_wheelBurstAccum != 0 && now - _wheelBurstLastMs > 100)
        {
            double burst = Math.Abs(_wheelBurstAccum);
            if (burst > 5 && burst < 4000)   // 合理范围过滤异常
                _wheelNotchDelta = _wheelNotchDelta * 0.7 + burst * 0.3;
            _wheelBurstAccum = 0;
        }
        _wheelBurstLastMs = now;
        _wheelBurstAccum += dy;
        double notch = Math.Clamp(_wheelNotchDelta, 20, 480);
        // v = dy/一档Delta × 视口高 × ratio × 摩擦 → 滑行距离 = v/摩擦 = dy/一档Delta × 视口高 × ratio
        double v = dy / notch * Viewport.Bounds.Height * WheelNotchScreenRatio
                   * _scrollFriction * Math.Max(0.1, WebtoonSpeed);
        AddScrollVelocity(Math.Clamp(v, -MaxScrollVelocity, MaxScrollVelocity));
        App.SLog($"WHEEL dy={dy:F0} notch≈{notch:F0} v={v:F0} ty={_ty:F0}");
        e.Handled = true;
    }

    /// <summary>施加滚动速度（拖拽甩动用：速度已是 px/s）。</summary>
    private void AddScrollVelocity(double v)
    {
        _scrollVelocity = Math.Clamp(_scrollVelocity + v, -MaxScrollVelocity, MaxScrollVelocity);
        StartSmoothScroll();
    }

    /// <summary>取消残余滚动（拖拽开始/跳页/切出时调用，防惯性互搏"往回拉"）。</summary>
    private void CancelSmoothScroll()
    {
        _scrolling = false;
        _scrollVelocity = 0;
        _scrollTimer?.Stop();
    }

    /// <summary>启动帧循环动量滚动（已在跑则无操作）。</summary>
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
            CancelSmoothScroll();
            return;
        }
        // 本帧时间差：每帧重置（数学正确；clamp 防卡顿跳变）
        double dt = Math.Clamp(_scrollWatch.Elapsed.TotalSeconds, 0.001, 0.05);
        _scrollWatch.Restart();
        // 指数摩擦：v' = v·e^(-c·dt) → 缓慢滑行停住（c = 设置"停止快慢"）
        _scrollVelocity *= Math.Exp(-_scrollFriction * dt);
        if (Math.Abs(_scrollVelocity) < ScrollStopEps)
        {
            _scrollVelocity = 0;
            CancelSmoothScroll();
            return;
        }
        _ty += _scrollVelocity * dt;
        ClampWebtoonScroll();   // 边界：到边即停（内部清速度，不反弹）
        ApplyTransform();
        UpdateWebtoonPages();
        // 周期性滚动日志（F12 诊断，~2次/秒）
        long now = Environment.TickCount64;
        if (now - _lastTickLog > 500)
        {
            _lastTickLog = now;
            App.SLog($"SCROLL ty={_ty:F0} v={_scrollVelocity:F0}");
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
        UpdateZoomPercent();   // v3：缩放后百分比 = scale×100（fit=100%）
    }

    private void ClampWebtoonScroll()
    {
        double totalH = WebtoonTotalHeight * _scale;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        if (totalH <= vpH) { _ty = 0; _scrollVelocity = 0; }
        else
        {
            double min = -(totalH - vpH);
            if (_ty < min) { _ty = min; _scrollVelocity = 0; }
            else if (_ty > 0) { _ty = 0; _scrollVelocity = 0; }
        }
        // 内容窄于视口 → 水平居中（看图器规则，与分页 ClampToViewportIfSmaller 一致）
        double dispW = _webtoonWidth * _scale;
        double vpW = Math.Max(1, Viewport.Bounds.Width);
        if (dispW <= vpW) _tx = (vpW - dispW) / 2;
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        LastPressSource = e.Source?.GetType().Name ?? "null";
        LastPressLeft = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (!LastPressLeft) return;
        bool webtoon = _vm.ViewMode == ViewMode.Webtoon;
        // 滚动模式：视口任意处可拖（含两侧空白，预览栏覆盖层除外）—— 与移动端抓取语义一致，消除拖拽死区（scroll-mode-fix v2）
        if (webtoon)
        {
            if (IsOverPreviewStrip(e.Source)) return;
            StartDrag(e);
            e.Handled = true;
            return;
        }
        // 分页模式：只在图片上拖动（看图器行为：空白处不拖）
        if (e.Source is Image)
        {
            StartDrag(e);
            e.Handled = true;
        }
    }

    private void StartDrag(PointerPressedEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(Viewport);
        _dragVelocity = 0;
        _lastDragTicks = 0;
        CancelSmoothScroll();   // 拖拽开始：取消残余惯性（防"往回拉"互搏，scroll-mode-fix B）
        App.SLog("PRESS -> dragging");
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
        // 拖拽速度估算（供释放时甩动惯性，scroll-mode-fix B）
        long now = Environment.TickCount64;
        if (_lastDragTicks != 0)
        {
            double dtSec = (now - _lastDragTicks) / 1000.0;
            if (dtSec > 0.004 && dtSec < 0.12)
            {
                double inst = (pos.Y - _lastDragY) / dtSec;
                _dragVelocity = _dragVelocity * 0.6 + inst * 0.4;   // 滑动平均抗抖
            }
        }
        _lastDragTicks = now;
        _lastDragY = pos.Y;
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
        // 甩动惯性（scroll-mode-fix B/v2）：释放瞬间的拖拽速度 → 动量滑行（方向与拖拽一致）；
        // 阈值与限幅由设置驱动 —— 慢拖松手即停，快甩自然滑行
        if (_vm?.ViewMode == ViewMode.Webtoon && Math.Abs(_dragVelocity) > _dragFlingThreshold)
            AddScrollVelocity(Math.Clamp(_dragVelocity, -_dragFlingMax, _dragFlingMax));
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
        ZoomSensitivity = _zoomSensitivityBase * SensLevels[_sensIdx];
        if (SensitivityButton is not null)
            SensitivityButton.Content = $"缩放速度×{SensLevels[_sensIdx]:0.#}";
    }

    private void OnWebtoonSpeedClick(object? sender, RoutedEventArgs e)
    {
        _wsIdx = (_wsIdx + 1) % SensLevels.Length;
        WebtoonSpeed = _webtoonSpeedBase * SensLevels[_wsIdx];
        if (WebtoonSpeedButton is not null)
            WebtoonSpeedButton.Content = $"滚动速度×{SensLevels[_wsIdx]:0.#}";
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

    /// <summary>Webtoon 复位（scroll-mode-fix C）：当前可见页适应屏幕 + 居中 + 回默认缩放
    /// （= 进入滚动模式的默认视图，与单页复位语义一致）。</summary>
    private void ResetWebtoonView()
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        // 记录当前可见页（滚动位置中央）
        double baseCenter = (-_ty + Viewport.Bounds.Height / 2) / Math.Max(0.001, _scale);
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, baseCenter)), 0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();
        _webtoonSeq++;   // 宽度重算 = 结构变化：作废在途任务
        FitWebtoonToPage(curPhys);
        UpdateZoomPercent();
        App.SLog($"WEBTOON RESET page={curPhys} width={_webtoonWidth:F0} scale={_scale:F3} ty={_ty:F1}");
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
    private int _thumbStripCount = 7;   // 当前张数（随视口高度动态，▲▼ 步长）
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

        // 固定卡片宽96 + 间距6 + 上下按钮58 → 按可用高度动态张数（1..11，奇偶皆可，不强制居中）
        // 防溢出：总高 = n×cardH + (n-1)×6 ≤ 视口高 - 58，任何窗口高度第 n 张都完整可见
        int n = Math.Clamp((int)((Viewport.Bounds.Height - 58) / 136.0), 1, 11);
        _thumbStripCount = n;
        // 卡片高填满剩余高度（消除张数死区余量），下限80 / 上限150（150px 源图缩小不糊）
        // 64 = 按钮26×2 + RowSpacing6×2（按钮与卡片间距 = 卡片间距 6，视觉一致）
        double cardH = Math.Clamp((Viewport.Bounds.Height - 64 - 6 * (n - 1)) / n, 80, 150);
        int center = Math.Clamp(_thumbCenterPhysical, 0, total - 1);
        int start = Math.Clamp(center - n / 2, 0, Math.Max(0, total - n));
        int end = Math.Min(total - 1, start + n - 1);

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
        _thumbCenterPhysical = Math.Max(0, _thumbCenterPhysical - _thumbStripCount);
        RefreshThumbStrip();
    }

    private void OnPreviewStripNext(object? sender, RoutedEventArgs e)
    {
        _thumbCenterPhysical = Math.Min((_vm?.PhysicalPageCount ?? 1) - 1, _thumbCenterPhysical + _thumbStripCount);
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
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            // 滚动模式：◀ 按方向滚动一屏（RTL：下一页=向下；LTR：上一页=向上）—— 页面导航需动画面（scroll-mode-fix A）
            ScrollWebtoonPages(_vm.Direction == ReadingDirection2.Rtl ? -1 : 1);
            return;
        }
        if (_vm.Direction == ReadingDirection2.Rtl) _vm.NextPageCommand.Execute(null);
        else _vm.PrevPageCommand.Execute(null);
    }

    private void OnNavNextClick(object? sender, RoutedEventArgs e)
    {
        App.SLog($"BTN 下一页点击");
        if (_vm is null) return;
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            ScrollWebtoonPages(_vm.Direction == ReadingDirection2.Rtl ? 1 : -1);
            return;
        }
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
            ? Math.Clamp(PhysicalAt(Math.Max(0, (-_ty) / Math.Max(0.001, _scale))), 0, total - 1) + 1
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
                    if (_vm.ViewMode == ViewMode.Webtoon)
                    {
                        _vm.JumpToPhysicalCommand.Execute(p - 1);
                        ScrollWebtoonToPhysical(p - 1);   // 视图滚动定位（原缺失 → 跳转无效，scroll-mode-fix A）
                    }
                    else _vm.JumpToPageCommand.Execute(p - 1);
                }
                dialog.Close();
            }
            else if (ke.Key == Key.Escape) dialog.Close();
        };
        dialog.Opened += (_, _) => { text.Focus(); text.SelectAll(); };
        await dialog.ShowDialog(this);
    }

    /// <summary>键盘处理：按"当前模式 + 按键"查快捷键表分发（设置可重绑，三模式分开）。
    /// DirNext/DirPrev 为方向感知语义键（分页 RTL：DirNext=←；LTR：DirNext=→）。</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (ResolveAction(e.Key) is not { } action) return;
        App.SLog($"KEY {e.Key} -> {action}");
        ExecuteAction(action);
        e.Handled = true;
    }

    /// <summary>当前模式下该按键对应的动作（无绑定返回 null；F12 调试在无 VM 时也可用）。</summary>
    private ReaderAction? ResolveAction(Key key)
    {
        if (_settings?.Hotkeys is not { } map) return null;
        string mode = _vm?.ViewMode switch
        {
            ViewMode.Webtoon => "Webtoon",
            ViewMode.Double => "Double",
            _ => "Single",
        };
        if (!map.TryGetValue(mode, out var actions)) return null;
        var dirNext = _vm?.Direction == ReadingDirection2.Ltr ? Key.Right : Key.Left;   // RTL 默认：←=下一页
        var dirPrev = _vm?.Direction == ReadingDirection2.Ltr ? Key.Left : Key.Right;
        foreach (var (actionName, keys) in actions)
        {
            if (keys is null) continue;
            foreach (var token in keys)
            {
                if (token == "DirNext" && key == dirNext) return ParseAction(actionName);
                if (token == "DirPrev" && key == dirPrev) return ParseAction(actionName);
                if (Enum.TryParse<Key>(token, out var k) && k == key) return ParseAction(actionName);
            }
        }
        return null;
    }

    private static ReaderAction? ParseAction(string name)
        => Enum.TryParse<ReaderAction>(name, out var a) ? a : null;

    /// <summary>执行动作（各动作按模式与状态分派）。</summary>
    private void ExecuteAction(ReaderAction action)
    {
        switch (action)
        {
            case ReaderAction.NextPage:
                if (_vm is not null) _vm.NextPageCommand.Execute(null);
                break;
            case ReaderAction.PrevPage:
                if (_vm is not null) _vm.PrevPageCommand.Execute(null);
                break;
            case ReaderAction.WebtoonNextPage:
                WebtoonJumpPage(1);   // 整页顶对齐跳下一页
                break;
            case ReaderAction.WebtoonPrevPage:
                WebtoonJumpPage(-1);  // 整页顶对齐跳上一页
                break;
            case ReaderAction.WebtoonScrollDown:
                ScrollWebtoonPages(-1);
                break;
            case ReaderAction.WebtoonScrollUp:
                ScrollWebtoonPages(1);
                break;
            case ReaderAction.ToggleMode:
                if (_vm is not null) _vm.ToggleModeCommand.Execute(null);
                break;
            case ReaderAction.ToggleDirection:
                if (_vm is not null) _vm.ToggleDirectionCommand.Execute(null);
                break;
            case ReaderAction.ToggleTheme:
                if (_vm is not null) _vm.ToggleThemeCommand.Execute(null);
                break;
            case ReaderAction.JumpDialog:
                ShowJumpDialog();
                break;
            case ReaderAction.DebugPanel:
                ToggleDebugPanel();   // 无 VM 也可用
                break;
            case ReaderAction.CycleFit:
                if (_vm is not null) _vm.CycleFitCommand.Execute(null);
                break;
            case ReaderAction.ResetView:
                if (_vm?.ViewMode == ViewMode.Webtoon) ResetWebtoonView();
                else ResetPagedView();
                break;
            case ReaderAction.ZoomIn:
                OnZoomInClick(this, new RoutedEventArgs());
                break;
            case ReaderAction.ZoomOut:
                OnZoomOutClick(this, new RoutedEventArgs());
                break;
            case ReaderAction.TogglePreview:
                TogglePreviewStrip();
                break;
        }
    }

    /// <summary>滚动模式整页跳：目标页顶对齐视口顶，并同步当前页状态（书签/续读）。
    /// dir=+1 下一页，dir=-1 上一页。</summary>
    private void WebtoonJumpPage(int dir)
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        int top = Math.Clamp(PhysicalAt(Math.Max(0, -_ty / Math.Max(0.001, _scale))), 0, _vm.PhysicalPageCount - 1);
        int target = Math.Clamp(top + dir, 0, _vm.PhysicalPageCount - 1);
        _vm.JumpToPhysicalCommand.Execute(target);   // 同步当前页状态
        ScrollWebtoonToPhysical(target);             // 页顶对齐视口顶
    }

    /// <summary>预览栏开关（P）。</summary>
    private void TogglePreviewStrip()
    {
        if (PreviewStrip is null) return;
        if (PreviewStrip.IsVisible) { PreviewStrip.IsVisible = false; StopHoverTimer(); }
        else ShowPreviewStrip();
    }

    /// <summary>Webtoon：按页滚动（1 页 = 一屏高；dir&lt;0 = 向下 = ty 减小）—— 动量滑行。
    /// scroll-mode-fix B：改纯动量（原实现 Space/PageDown 方向相反 + 目标双轨）。</summary>
    private void ScrollWebtoonPages(int dir)
    {
        if (_vm is null) return;
        AddScrollVelocity(Math.Clamp(dir * Math.Max(1, Viewport.Bounds.Height) * 6.0, -MaxScrollVelocity, MaxScrollVelocity));
    }
}
