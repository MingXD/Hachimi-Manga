using System.ComponentModel;
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
    private PropertyChangedEventHandler? _vmPropertyChanged;   // P1-4：OnClosed 退订
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

    /// <summary>测试用：占位块数量（防黑屏断言）。</summary>
    public int WebtoonPlaceholderCount => _webtoonPlaceholders.Count;
    /// <summary>测试用：滚动模式基准宽（画布无旧宽度残留断言）。</summary>
    public double WebtoonWidthValue => _webtoonWidth;

    /// <summary>调试面板（F12 开关，诊断"点击无反应"等交互问题）。</summary>
    private Views.DebugWindow? _debugWindow;






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
            // P1-4：委托存字段，OnClosed 退订（窗口与 VM 同生命周期，成环可 GC 回收；退订更干净）
            _vmPropertyChanged = (_, args) =>
            {
                // 注意：PageText 不在此列表 —— 它只是显示文本，触发 Relayout 会导致
                // 按钮样式每帧重评估（闪烁）并浪费布局。
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
            vm.PropertyChanged += _vmPropertyChanged;
            // v3 #4：滚动模式布局/比例补全后重建当前页 fit（续读恢复页用真实比例，不再 0.7 占位"缩小"）
            vm.WebtoonLayoutChanged += OnWebtoonLayoutChanged;
            Relayout();
        }
    }

    private void OnWebtoonLayoutChanged()
    {
        if (_vm?.ViewMode == ViewMode.Webtoon)
            FitWebtoonToPage(_vm.CurrentFirstPhysical);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // v5.2：分页默认适应模式（设置；每次打开应用，适应模式不持久化）
        if (_vm is not null)
            _vm.FitMode = _settings.DefaultFitMode == "FitWidth" ? FitMode.FitWidth : FitMode.FitScreen;
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
            // P1-4：退订 VM PropertyChanged（与订阅对称，防窗口/VM 互相引用）
            if (_vm is not null && _vmPropertyChanged is not null)
                _vm.PropertyChanged -= _vmPropertyChanged;
            if (_vm is not null) _vm.WebtoonLayoutChanged -= OnWebtoonLayoutChanged;
            _vmPropertyChanged = null;
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

    // ---------- 布局：基准内容 + Fit 计算 + 变换应用 ----------

    /// <summary>缩放灵敏度（1.0 默认；>1 更快，<1 更慢）。设置 UI 后续提供。</summary>
    public double ZoomSensitivity { get; set; } = 1.0;
    /// <summary>Webtoon 滚动速度（1.0 默认；0.5/1/2 三档，工具栏按钮）。</summary>
    public double WebtoonSpeed { get; set; } = 1.0;

    /// <summary>适应屏幕的缩放值（百分比基准）。</summary>

    /// <summary>更新缩放百分比显示。分页基准 = 适应屏幕；滚动模式 fit=100%（与单页一致，v3）。</summary>




    // ---------- 缩放（精细、鼠标焦点锚定） ----------

    /// <summary>看图器规则：内容小于视口时自动居中（缩放后）；放大时保持当前位置。</summary>

    /// <summary>测试/按钮用：以内容中心为焦点缩放（factor=1.25/0.8）。Webtoon 走独立路径（BUG-G/I）。</summary>

    /// <summary>内容局部点 → 屏幕坐标（与 MatrixTransform 同一数学模型：p' = scale·p + t）。</summary>

    /// <summary>内容渲染区域（视口坐标）。</summary>

    /// <summary>焦点：指针位置 clamp 到内容区域（指针在画面外时取最近边界点）。</summary>

    /// <summary>标准焦点锚定缩放（zoooom 等 pan/zoom 库算法）。</summary>

    // ---------- 输入 ----------


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


    /// <summary>滚动模式"适应高度"（v3.2）：与单页 FitHeight 一致 —— 当前页高 = 视口高
    /// （条宽 = 视口高×当前页比例；横版页宽可超视口），当前页顶对齐视口顶。</summary>

    /// <summary>滚动模式"适应宽度"（v3.1）：条宽 = 视口宽（铺满，与单页 FitWidth 一致），
    /// 当前可见页顶对齐视口顶。</summary>

    /// <summary>滚动模式默认视图：当前页适应屏幕（完全可见）+ 水平垂直居中，与单页 FitScreen 语义一致。
    /// 进入滚动模式与复位/适应屏幕共用（scroll-mode-fix C / v3）。
    /// v3：宽度重算后先清空旧尺寸 Image 与占位再重建（否则旧图原位残留 → 错位"两页之间"）。</summary>

    /// <summary>清空滚动模式全部实例化图（宽度/几何结构变化时调用，占位保证不黑屏）。
    /// v3：同时重置范围守卫（否则重进滚动模式时范围未变 → 加载循环被跳过 → 画布空）。</summary>


    /// <summary>清空占位块（画布结构变化/退出时调用，占位随宽高重算重建）。</summary>

    /// <summary>占位块：已入可见范围但未解码的页显示灰底+页码（scroll-mode-fix v2：任何时刻不黑屏）。
    /// v3：底色/文字提亮（#3A3A44+#CCC）更易辨识为"加载中"而非坏图。</summary>


    /// <summary>前缀和：页 i 顶部 y（基准坐标）。</summary>

    /// <summary>按当前比例重建前缀和（进入/比例变化时）。</summary>

    /// <summary>二分：给定基准 y，返回包含它的页索引。</summary>

    private int _webtoonLastFirst = -1, _webtoonLastLast = -1;   // 范围变化守卫（v3 优化）
    private readonly Dictionary<int, int> _webtoonFailCounts = new();   // 解码失败计数（有限重试）
    private const int MaxDecodeRetries = 3;                             // 失败重试上限

    /// <summary>Webtoon：按滚动位置增量渲染可见页 ± 缓冲（先加后删，防黑屏）。
    /// v3：范围未变跳过重活；可见页放大清晰度检查（重解码原位替换）始终执行。</summary>

    /// <summary>放大清晰度检查：可见页缓存宽度 < 显示需求×0.85 → 移除旧图 + 占位 + 立即按新宽度重请求。
    /// v3 采用"移除重建"而非原位替换 Source（原位替换会因渲染管线仍持旧位图引用而崩溃）；
    /// RemoveWebtoonImage 已保证"先移出画布再释放"（防淘汰后直接 Dispose 被渲染位图）。</summary>



    /// <summary>解码失败有限重试（800ms 后重请求；状态已变则放弃）。</summary>

    /// <summary>Webtoon：滚动到指定物理页（页顶对齐视口顶，BUG-F v1.9）。
    /// scroll-mode-fix v2：先作废在途解码（新范围立即解码，不排队）→ 跳页即时不黑屏。</summary>

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
    private string _doubleClickAction = "Zoom";       // 双击行为：Zoom/Reset/None（设置）

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
        _maxDecodeWidth = Math.Clamp(_settings.MaxDecodeWidth, 1200, 4096);   // 旧字段保留（不再用于 want 钳制）
        _invertScroll = _settings.InvertScroll;
        _pagedWheelMode = _settings.PagedWheelMode == "Page" ? "Page" : "Zoom";
        _doubleClickAction = _settings.DoubleClickAction is "Reset" or "None" ? _settings.DoubleClickAction : "Zoom";
        if (_vm is not null)
        {
            _vm.CacheCap = Math.Clamp(_settings.CacheCap, 4, 30);
            // v3：清晰度档位 + 预取范围注入
            _vm.DecodeQuality = _settings.DecodeQuality is "Limit" or "Original" ? _settings.DecodeQuality : "Auto";
            _vm.DecodeLimit = Math.Clamp(_settings.DecodeLimit, 2048, 8192);
            _vm.PrefetchRange = Math.Clamp(_settings.PrefetchRange, 1, 3);
        }
        if (SensitivityButton is not null) SensitivityButton.Content = $"缩放速度×{SensLevels[_sensIdx]:0.#}";
        if (WebtoonSpeedButton is not null) WebtoonSpeedButton.Content = $"滚动速度×{SensLevels[_wsIdx]:0.#}";
    }


    /// <summary>Webtoon：滚轮事件 —— 自适应一档归一化（每物理档 ≈ 120% 视口高，事件进 F12 日志）。</summary>

    /// <summary>施加滚动速度（拖拽甩动用：速度已是 px/s）。</summary>

    /// <summary>取消残余滚动（拖拽开始/跳页/切出时调用，防惯性互搏"往回拉"）。</summary>

    /// <summary>启动帧循环动量滚动（已在跑则无操作）。</summary>


    /// <summary>Webtoon：Ctrl+滚轮缩放（锚定鼠标原始位置 —— W-2 修复，不 clamp 到单页）。</summary>




    /// <summary>事件源是否落在预览栏覆盖层内（覆盖层方案：PreviewStrip 在 Viewport 内部，
    /// 冒泡事件会到达 Viewport 处理器——滚轮/双击需跳过，避免"翻页+缩放"同时发生）。</summary>




    private static readonly double[] SensLevels = { 0.5, 1.0, 2.0 };
    private int _sensIdx = 1;
    private int _wsIdx = 1;





    /// <summary>分页模式复位：适应屏幕 + 居中（拖动/缩放后一键恢复）。</summary>

    /// <summary>Webtoon 复位（scroll-mode-fix C）：当前可见页适应屏幕 + 居中 + 回默认缩放
    /// （= 进入滚动模式的默认视图，与单页复位语义一致）。</summary>



    /// <summary>按阅读方向排列双页左右顺序（RTL：右=前页；LTR：左=前页）。</summary>



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




    private Action? _hoverAction;



    /// <summary>鼠标是否悬停在指定控件上（预览栏隐藏判断；new 隐藏基类同名属性，code-audit P0-3）。</summary>


    /// <summary>加载 7 张缩略图（小图解码，seq 防并发，ObservableCollection 自动刷新）。</summary>

    /// <summary>点击缩略图 → 跳转（三模式映射）。</summary>




    /// <summary>滚动模式双向联动：正文滚动时更新预览栏高亮。</summary>





    /// <summary>阅读模式下拉菜单。</summary>









    /// <summary>跳页对话框：输入 1..N 回车跳转（G 键 / 点击页码）。</summary>

    /// <summary>键盘处理：按"当前模式组 + 通用组"查快捷键表分发（设置可重绑；支持组合键 Ctrl+Shift+P）。</summary>

    /// <summary>按键 → 动作：先查当前模式组（分页按方向选组），未命中查通用组。</summary>



    /// <summary>执行动作（各动作按模式与状态分派）。</summary>

    /// <summary>滚动模式整页跳：目标页顶对齐视口顶，并同步当前页状态（书签/续读）。
    /// dir=+1 下一页，dir=-1 上一页。</summary>

    /// <summary>预览栏开关（P）。</summary>

    /// <summary>Webtoon：按页滚动（1 页 = 一屏高；dir&lt;0 = 向下 = ty 减小）—— 动量滑行。
    /// scroll-mode-fix B：改纯动量（原实现 Space/PageDown 方向相反 + 目标双轨）。</summary>
}
