using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MangaAgent.App.Services;
using MangaAgent.Core.Library;

namespace MangaAgent.App.Views;

/// <summary>设置窗口：阅读（基准/手感/性能）+ 快捷键（三模式分开）+ 使用介绍（usage.md，可直接编辑文件）。</summary>
public class SettingsDialog : Window
{
    private sealed record BindingRef(string Mode, string Action);

    private readonly AppSettings _copy;          // 编辑副本（确定才保存）
    private readonly Dictionary<string, Dictionary<string, List<string>>> _hotkeyEdit = new();
    private (string Mode, string Action)? _capturing;   // 正在捕获按键的单元格
    private Button? _captureCell;
    private readonly TextBlock _captureHint = new() { Foreground = new SolidColorBrush(Color.Parse("#CC8800")), TextWrapping = TextWrapping.Wrap };

    // 页签一控件
    private readonly Slider _zoomBase, _scrollBase, _friction, _flingThreshold, _flingMax, _decodeCap, _cacheCap;
    private readonly CheckBox _invert;
    private readonly ComboBox _pagedWheel;

    private static readonly string[] Modes = { "Single", "Double", "Webtoon" };
    private static readonly Dictionary<string, string> ModeLabels = new() { ["Single"] = "单页", ["Double"] = "双页", ["Webtoon"] = "滚动" };

    public SettingsDialog(AppSettings current)
    {
        Title = "设置";
        Width = 660;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _copy = Clone(current);
        foreach (var (mode, actions) in _copy.Hotkeys)
        {
            var modeCopy = new Dictionary<string, List<string>>();
            foreach (var (a, keys) in actions) modeCopy[a] = new List<string>(keys ?? new List<string>());
            _hotkeyEdit[mode] = modeCopy;
        }

        // —— 页签一：阅读 ——
        _zoomBase = MakeSlider(0.5, 2.0, 0.1, _copy.ZoomSensitivity);
        _scrollBase = MakeSlider(0.5, 2.0, 0.1, _copy.WebtoonSpeed);
        _friction = MakeSlider(3, 12, 0.5, _copy.ScrollFriction);
        _flingThreshold = MakeSlider(50, 400, 10, _copy.DragFlingThreshold);
        _flingMax = MakeSlider(1000, 6000, 100, _copy.DragFlingMax);
        _decodeCap = MakeSlider(1200, 4096, 64, _copy.MaxDecodeWidth);
        _cacheCap = MakeSlider(4, 20, 1, _copy.CacheCap);
        _invert = new CheckBox { Content = "滚动方向反转（自然滚动）", IsChecked = _copy.InvertScroll };
        _pagedWheel = new ComboBox { ItemsSource = new[] { "缩放", "翻页" }, SelectedIndex = _copy.PagedWheelMode == "Page" ? 1 : 0, Width = 140 };
        var tab1 = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(14), Spacing = 10, Children =
        {
            Label("阅读基准"),
            Row("缩放速度基准", _zoomBase, "滚轮缩放每格幅度（0.5~2.0）"),
            Row("滚动速度基准", _scrollBase, "滚动模式一档距离（0.5~2.0）"),
            Label("滚动手感（滚动模式）"),
            Row("停止快慢", _friction, "摩擦系数（越大停得越快）"),
            Row("甩动触发阈值", _flingThreshold, "拖拽释放多快才算甩动（px/s）"),
            Row("甩动限幅", _flingMax, "甩动最大速度（px/s）"),
            Label("性能与行为"),
            Row("解码宽度上限", _decodeCap, "大图解码分辨率上限（清晰度 vs 速度/内存）"),
            Row("缓存页数", _cacheCap, "位图缓存上限（流畅 vs 内存）"),
            Row("分页滚轮行为", _pagedWheel, "分页模式下滚轮 = 缩放 或 翻页"),
            _invert,
        } } };

        // —— 页签二：快捷键（三模式）——
        var grid = new Grid { Margin = new Thickness(14, 8, 14, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(170)));
        foreach (var _ in Modes) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(Place(new TextBlock { Text = "动作", FontWeight = FontWeight.SemiBold }, 0, 0));
        for (int c = 0; c < Modes.Length; c++)
            grid.Children.Add(Place(new TextBlock { Text = ModeLabels[Modes[c]], FontWeight = FontWeight.SemiBold }, c + 1, 0));

        var allActions = _hotkeyEdit["Single"].Keys
            .Concat(_hotkeyEdit["Webtoon"].Keys.Where(k => !_hotkeyEdit["Single"].ContainsKey(k)))
            .ToList();
        int row = 1;
        foreach (var action in allActions)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(Place(new TextBlock { Text = ActionLabel(action), VerticalAlignment = VerticalAlignment.Center }, 0, row));
            for (int c = 0; c < Modes.Length; c++)
            {
                var mode = Modes[c];
                var cellBtn = new Button
                {
                    Content = KeyText(_hotkeyEdit[mode].TryGetValue(action, out var ks) ? ks : null),
                    Tag = new BindingRef(mode, action),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Background = new SolidColorBrush(Color.Parse("#2A2A34")),
                    Foreground = Brushes.White,
                    FontSize = 11,
                    MinHeight = 24,
                };
                cellBtn.Click += OnHotkeyCellClick;
                cellBtn.ContextRequested += (_, _) =>
                {
                    if (cellBtn.Tag is BindingRef b) { ClearBinding(b.Mode, b.Action); cellBtn.Content = KeyText(_hotkeyEdit[b.Mode][b.Action]); }
                };
                grid.Children.Add(Place(cellBtn, c + 1, row));
            }
            row++;
        }
        var tab2 = new ScrollViewer { Content = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = "点击按键单元格 → 按新键替换该动作按键；右键单元格清空绑定。", Foreground = Brushes.Gray, FontSize = 11, Margin = new Thickness(14, 10, 14, 0) },
            _captureHint,
            grid,
        } } };

        // —— 页签三：使用介绍 ——
        var tab3 = new ScrollViewer { Content = RenderGuide() };

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "阅读", Content = tab1 });
        tabs.Items.Add(new TabItem { Header = "快捷键", Content = tab2 });
        tabs.Items.Add(new TabItem { Header = "使用介绍", Content = tab3 });

        var save = new Button { Content = "保存", MinWidth = 80 };
        var cancel = new Button { Content = "取消", MinWidth = 80 };
        var openGuide = new Button { Content = "打开介绍文件", MinWidth = 110 };
        openGuide.Click += (_, _) => FileOps.ShowInExplorer(SettingsStore.GuidePath);
        var pathInfo = new TextBlock
        {
            Text = $"设置：{SettingsStore.SettingsPath}\n介绍：{SettingsStore.GuidePath}",
            Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap,
        };
        var bottom = new DockPanel { Margin = new Thickness(14, 8, 14, 14) };
        DockPanel.SetDock(pathInfo, Dock.Right);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, cancel, openGuide } };
        bottom.Children.Add(pathInfo);
        bottom.Children.Add(buttons);

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(tabs);
        Content = root;

        save.Click += (_, _) => { ApplyControlsToCopy(); SettingsStore.Save(_copy); Close(); };
        cancel.Click += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (_capturing is not null)
            {
                if (e.Key == Key.Escape) CancelCapture();
                else CaptureKey(e.Key);
                e.Handled = true;
            }
        };
    }

    // —— 页签一辅助 ——

    private static AppSettings Clone(AppSettings s) => new()
    {
        ZoomSensitivity = s.ZoomSensitivity,
        WebtoonSpeed = s.WebtoonSpeed,
        ScrollFriction = s.ScrollFriction,
        DragFlingThreshold = s.DragFlingThreshold,
        DragFlingMax = s.DragFlingMax,
        MaxDecodeWidth = s.MaxDecodeWidth,
        CacheCap = s.CacheCap,
        InvertScroll = s.InvertScroll,
        PagedWheelMode = s.PagedWheelMode,
        Hotkeys = s.Hotkeys,
    };

    private static Slider MakeSlider(double min, double max, double tick, double value) => new()
    {
        Minimum = min, Maximum = max, Value = value, Width = 180, IsSnapToTickEnabled = tick > 0, TickFrequency = tick,
    };

    private static TextBlock Label(string t) => new() { Text = t, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    private static StackPanel Row(string label, Control control, string hint)
        => new() { Orientation = Orientation.Horizontal, Spacing = 10, Children =
        {
            new TextBlock { Text = label, Width = 120, VerticalAlignment = VerticalAlignment.Center },
            control,
            new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap },
        } };

    private void ApplyControlsToCopy()
    {
        _copy.ZoomSensitivity = _zoomBase.Value;
        _copy.WebtoonSpeed = _scrollBase.Value;
        _copy.ScrollFriction = _friction.Value;
        _copy.DragFlingThreshold = _flingThreshold.Value;
        _copy.DragFlingMax = _flingMax.Value;
        _copy.MaxDecodeWidth = (int)_decodeCap.Value;
        _copy.CacheCap = (int)_cacheCap.Value;
        _copy.InvertScroll = _invert.IsChecked == true;
        _copy.PagedWheelMode = _pagedWheel.SelectedIndex == 1 ? "Page" : "Zoom";
        _copy.Hotkeys = _hotkeyEdit;
    }

    // —— 快捷键捕获 ——

    private void OnHotkeyCellClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BindingRef br }) return;
        if (_capturing is not null) CancelCapture();
        _capturing = (br.Mode, br.Action);
        _captureCell = (Button)sender;
        _captureCell.Content = "按新键…";
        _captureHint.Text = $"正在为 [{ActionLabel(br.Action)}]（{ModeLabels[br.Mode]}）设置按键：请按新键（Esc 取消）";
    }

    private void CaptureKey(Key key)
    {
        if (_capturing is not { } cap || _captureCell is null) return;
        var (mode, action) = cap;
        // 冲突检测：同模式其他动作是否已绑定该键
        foreach (var (otherAction, keys) in _hotkeyEdit[mode])
        {
            if (otherAction == action) continue;
            if (keys.Contains(key.ToString()))
            {
                _captureHint.Text = $"按键 {KeyText(new List<string> { key.ToString() })} 已绑定到 [{ActionLabel(otherAction)}]，请换一个键";
                return;
            }
        }
        _hotkeyEdit[mode][action] = new List<string> { key.ToString() };
        _capturing = null;
        _captureCell.Content = key.ToString();
        _captureCell = null;
        _captureHint.Text = "";
    }

    private void CancelCapture()
    {
        if (_capturing is { } cap && _captureCell is not null)
            _captureCell.Content = KeyText(_hotkeyEdit[cap.Mode].TryGetValue(cap.Action, out var ks) ? ks : null);
        _capturing = null;
        _captureCell = null;
        _captureHint.Text = "";
    }

    private void ClearBinding(string mode, string action)
    {
        if (_hotkeyEdit.TryGetValue(mode, out var map) && map.ContainsKey(action))
            map[action] = new List<string>();
    }

    // —— 显示辅助 ——

    private static string ActionLabel(string action) => action switch
    {
        nameof(ReaderAction.NextPage) => "下一页（分页）",
        nameof(ReaderAction.PrevPage) => "上一页（分页）",
        nameof(ReaderAction.WebtoonNextPage) => "下一页整页跳（滚动）",
        nameof(ReaderAction.WebtoonPrevPage) => "上一页整页跳（滚动）",
        nameof(ReaderAction.WebtoonScrollDown) => "向下滑动一屏",
        nameof(ReaderAction.WebtoonScrollUp) => "向上滑动一屏",
        nameof(ReaderAction.ToggleMode) => "切换阅读模式",
        nameof(ReaderAction.ToggleDirection) => "切换阅读方向",
        nameof(ReaderAction.ToggleTheme) => "切换主题",
        nameof(ReaderAction.JumpDialog) => "跳页对话框",
        nameof(ReaderAction.DebugPanel) => "调试面板",
        nameof(ReaderAction.CycleFit) => "适应模式循环",
        nameof(ReaderAction.ResetView) => "复位",
        nameof(ReaderAction.ZoomIn) => "放大",
        nameof(ReaderAction.ZoomOut) => "缩小",
        nameof(ReaderAction.TogglePreview) => "预览栏开关",
        _ => action,
    };

    private static string KeyText(List<string>? keys)
    {
        if (keys is null || keys.Count == 0) return "（无）";
        return string.Join(" ", keys.Select(k => k switch
        {
            "DirNext" => "←/→方向",
            "DirPrev" => "→/←方向",
            "OemPlus" => "+", "Add" => "Num+",
            "OemMinus" => "−", "Subtract" => "Num−",
            "Space" => "空格",
            "PageDown" => "PgDn", "PageUp" => "PgUp",
            _ => k,
        }));
    }

    private static Control Place(Control c, int col, int row)
    {
        Grid.SetColumn(c, col);
        Grid.SetRow(c, row);
        return c;
    }

    // —— 使用介绍渲染 ——

    /// <summary>渲染使用介绍（usage.md 轻量格式：# 标题 / ## 小节 / !img:相对路径 / 正文）。</summary>
    private static Control RenderGuide()
    {
        var stack = new StackPanel { Margin = new Thickness(14), Spacing = 8 };
        if (!File.Exists(SettingsStore.GuidePath))
        {
            stack.Children.Add(new TextBlock { Text = "使用介绍文件不存在：" + SettingsStore.GuidePath, Foreground = Brushes.Red });
            return stack;
        }
        var dir = Path.GetDirectoryName(SettingsStore.GuidePath)!;
        foreach (var raw in File.ReadAllLines(SettingsStore.GuidePath))
        {
            var line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("!img:", StringComparison.Ordinal))
            {
                var rel = line[5..].Trim();
                var imgPath = Path.Combine(dir, rel);
                if (File.Exists(imgPath))
                {
                    try
                    {
                        stack.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(imgPath), Stretch = Stretch.Uniform, MaxWidth = 580, Margin = new Thickness(0, 4) });
                    }
                    catch { stack.Children.Add(new TextBlock { Text = $"[图片无法加载: {rel}]", Foreground = Brushes.Gray }); }
                }
                else stack.Children.Add(new TextBlock { Text = $"[图片未找到: {rel} —— 把图片放到 {SettingsStore.GuideDir}\\images\\ 目录]", Foreground = Brushes.Gray });
            }
            else if (line.StartsWith("## ", StringComparison.Ordinal))
                stack.Children.Add(new TextBlock { Text = line[3..], FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#4A9EFF")) });
            else if (line.StartsWith("# ", StringComparison.Ordinal))
                stack.Children.Add(new TextBlock { Text = line[2..], FontSize = 20, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Color.Parse("#4A9EFF")), Margin = new Thickness(0, 10, 0, 0) });
            else
                stack.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        return stack;
    }
}
