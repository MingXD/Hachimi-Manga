using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MangaAgent.App.Services;
using MangaAgent.Core.Library;

namespace MangaAgent.App.Views;

/// <summary>设置窗口 v2：阅读（滑块+数值框双向）+ 快捷键（四组竖排卡片，一方块一键/组合键）+ 使用介绍。
/// 底部：恢复默认·阅读 / 恢复默认·快捷键 / 保存 / 取消 / 打开介绍文件。</summary>
public class SettingsDialog : Window
{
    private sealed record KeyRef(string Group, string Action, string Token);

    private readonly AppSettings _copy;                          // 编辑副本（确定才保存）
    private readonly Dictionary<string, Dictionary<string, List<string>>> _hotkeyEdit = new();
    private readonly StackPanel _hotkeyRoot = new() { Spacing = 10 };
    private readonly TextBlock _captureHint = new()
    {
        Foreground = new SolidColorBrush(Color.Parse("#FFB54A")),
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 18,
    };
    private readonly Border _focusSink = new() { Focusable = true, IsVisible = false, Width = 1, Height = 1 };
    private (string Group, string Action, string ReplaceToken, bool AddMode)? _capturing;
    private Button? _captureCell;

    // 阅读页控件
    private readonly Slider _zoomBase, _scrollBase, _friction, _flingThreshold, _flingMax, _decodeCap, _cacheCap;
    private readonly TextBox _zoomBaseT, _scrollBaseT, _frictionT, _flingThresholdT, _flingMaxT, _decodeCapT, _cacheCapT;
    private readonly CheckBox _invert, _resume;
    private readonly ComboBox _pagedWheel, _defaultMode, _doubleClick, _defaultFit;

    private static readonly (string Group, string Label)[] Groups =
    {
        ("General", "通用（所有模式）"),
        ("PagedLtr", "分页 · 左到右"),
        ("PagedRtl", "分页 · 右到左"),
        ("Webtoon", "滚动"),
    };

    private static readonly (string Group, string[] Actions)[] GroupActions =
    {
        ("General", new[]
        {
            nameof(ReaderAction.ToggleMode), nameof(ReaderAction.ToggleDirection), nameof(ReaderAction.ToggleTheme),
            nameof(ReaderAction.JumpDialog), nameof(ReaderAction.DebugPanel), nameof(ReaderAction.CycleFit),
            nameof(ReaderAction.ResetView), nameof(ReaderAction.ZoomIn), nameof(ReaderAction.ZoomOut),
            nameof(ReaderAction.TogglePreview),
        }),
        ("PagedLtr", new[] { nameof(ReaderAction.NextPage), nameof(ReaderAction.PrevPage) }),
        ("PagedRtl", new[] { nameof(ReaderAction.NextPage), nameof(ReaderAction.PrevPage) }),
        ("Webtoon", new[]
        {
            nameof(ReaderAction.WebtoonNextPage), nameof(ReaderAction.WebtoonPrevPage),
            nameof(ReaderAction.WebtoonScrollDown), nameof(ReaderAction.WebtoonScrollUp),
        }),
    };

    public SettingsDialog(AppSettings current)
    {
        Title = "设置";
        Width = 720;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _copy = Clone(current);
        foreach (var (g, actions) in _copy.Hotkeys)
        {
            var gCopy = new Dictionary<string, List<string>>();
            foreach (var (a, keys) in actions) gCopy[a] = new List<string>(keys ?? new List<string>());
            _hotkeyEdit[g] = gCopy;
        }

        // —— 页签一：阅读（卡片样式 + 滑块/数值框双向 + 响应式拉伸 + 打开书行为）——
        (_zoomBase, _zoomBaseT) = MakeSliderRow(0.2, 4.0, _copy.ZoomSensitivity, false);   // 基准可调幅度更大更细
        (_scrollBase, _scrollBaseT) = MakeSliderRow(0.2, 4.0, _copy.WebtoonSpeed, false);
        (_friction, _frictionT) = MakeSliderRow(3, 12, _copy.ScrollFriction, false);
        (_flingThreshold, _flingThresholdT) = MakeSliderRow(50, 400, _copy.DragFlingThreshold, false);
        (_flingMax, _flingMaxT) = MakeSliderRow(1000, 6000, _copy.DragFlingMax, false);
        (_decodeCap, _decodeCapT) = MakeSliderRow(1200, 4096, _copy.MaxDecodeWidth, true);
        (_cacheCap, _cacheCapT) = MakeSliderRow(4, 20, _copy.CacheCap, true);
        _invert = new CheckBox { IsChecked = _copy.InvertScroll, VerticalAlignment = VerticalAlignment.Center };
        _resume = new CheckBox { IsChecked = _copy.ResumePosition, VerticalAlignment = VerticalAlignment.Center };
        _defaultMode = new ComboBox { ItemsSource = new[] { "单页", "双页", "滚动" }, SelectedIndex = _copy.DefaultMode switch { "Double" => 1, "Webtoon" => 2, _ => 0 }, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        _doubleClick = new ComboBox { ItemsSource = new[] { "放大", "复位", "无动作" }, SelectedIndex = _copy.DoubleClickAction switch { "Reset" => 1, "None" => 2, _ => 0 }, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        _defaultFit = new ComboBox { ItemsSource = new[] { "适应屏幕", "适应宽度" }, SelectedIndex = _copy.DefaultFitMode == "FitWidth" ? 1 : 0, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        _pagedWheel = new ComboBox { ItemsSource = new[] { "缩放", "翻页" }, SelectedIndex = _copy.PagedWheelMode == "Page" ? 1 : 0, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var tab1 = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 10,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
        {
            Card("阅读基准",
                SettingRow("缩放速度基准", _zoomBase, _zoomBaseT, "滚轮缩放每格幅度（0.2~4.0）"),
                SettingRow("滚动速度基准", _scrollBase, _scrollBaseT, "滚动模式一档距离（0.2~4.0）")),
            Card("打开书行为",
                SettingRow("默认阅读模式", _defaultMode, null, "打开书且无保存状态时进入的模式"),
                SettingRow("恢复上次位置", _resume, null, "打开书是否跳到上次阅读页；关 = 总是从第 1 页"),
                SettingRow("双击行为", _doubleClick, null, "双击图片：放大 / 复位 / 无动作"),
                SettingRow("默认适应模式", _defaultFit, null, "分页打开默认视图：适应屏幕 / 适应宽度")),
            Card("滚动手感（滚动模式）",
                SettingRow("停止快慢", _friction, _frictionT, "摩擦系数（越大停得越快）"),
                SettingRow("甩动触发阈值", _flingThreshold, _flingThresholdT, "拖拽释放多快才算甩动（px/s）"),
                SettingRow("甩动限幅", _flingMax, _flingMaxT, "甩动最大速度（px/s）")),
            Card("性能与行为",
                SettingRow("解码宽度上限", _decodeCap, _decodeCapT, "大图解码分辨率上限（清晰度 vs 速度/内存）"),
                SettingRow("缓存页数", _cacheCap, _cacheCapT, "位图缓存上限（流畅 vs 内存）"),
                SettingRow("分页滚轮行为", _pagedWheel, null, "分页模式下滚轮 = 缩放 或 翻页"),
                SettingRow("滚动方向反转", _invert, null, "自然滚动（滚轮方向反转）")),
        }
            }
        };

        // —— 页签二：快捷键（四组竖排卡片）——
        RebuildHotkeyCards();
        var tab2 = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Children =
        {
            new TextBlock { Text = "点击键块 = 替换该键；右键键块 = 删除；行尾 [+] = 新增一个键。支持组合键（如 Ctrl+Shift+P）。", Foreground = Brushes.Gray, FontSize = 11 },
            _captureHint,
            _hotkeyRoot,
        }
            }
        };

        // —— 页签三：使用介绍（卡片包裹，内容/图片随窗口宽度自适应）——
        var (guideStack, guideImages) = RenderGuideContent();
        var guideScroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Content = Card("使用介绍", guideStack) };
        guideScroll.SizeChanged += (_, e) =>
        {
            double w = e.NewSize.Width - 52;   // 外边距 14×2 + 卡片内边距 12×2
            foreach (var img in guideImages) img.MaxWidth = Math.Max(120, w);
        };
        var tab3 = guideScroll;

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "阅读", Content = tab1 });
        tabs.Items.Add(new TabItem { Header = "快捷键", Content = tab2 });
        tabs.Items.Add(new TabItem { Header = "使用介绍", Content = tab3 });

        var restoreRead = new Button { Content = "恢复默认·阅读", MinWidth = 110 };
        var restoreHot = new Button { Content = "恢复默认·快捷键", MinWidth = 120 };
        var save = new Button { Content = "保存", MinWidth = 70 };
        var cancel = new Button { Content = "取消", MinWidth = 70 };
        var openGuide = new Button { Content = "打开介绍文件", MinWidth = 110 };
        openGuide.Click += (_, _) => FileOps.ShowInExplorer(SettingsStore.GuidePath);
        restoreRead.Click += (_, _) => RestoreReadingDefaults();
        restoreHot.Click += (_, _) =>
        {
            _hotkeyEdit.Clear();
            foreach (var (g, actions) in AppSettings.DefaultHotkeys())
            {
                var gCopy = new Dictionary<string, List<string>>();
                foreach (var (a, keys) in actions) gCopy[a] = new List<string>(keys);
                _hotkeyEdit[g] = gCopy;
            }
            CancelCapture();
            RebuildHotkeyCards();
            _captureHint.Text = "快捷键已恢复为默认（尚未保存）";
        };
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(14, 8, 14, 14), Children = { restoreRead, restoreHot, save, cancel, openGuide } };

        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(tabs);
        root.Children.Add(_focusSink);
        Content = root;

        save.Click += (_, _) => { ApplyControlsToCopy(); SettingsStore.Save(_copy); Close(); };
        cancel.Click += (_, _) => Close();

        // 捕获走 Tunnel：先于按钮/滚动条处理，Space/箭头/PgDn 不再误触发
        AddHandler(KeyDownEvent, OnCaptureKeyDown, RoutingStrategies.Tunnel);
    }

    // ================= 快捷键卡片 =================

    private void RebuildHotkeyCards()
    {
        _hotkeyRoot.Children.Clear();
        foreach (var (group, label) in Groups)
        {
            var rows = new StackPanel { Spacing = 6 };
            foreach (var action in GroupActions.First(x => x.Group == group).Actions)
            {
                if (!_hotkeyEdit.TryGetValue(group, out var actions)) continue;
                if (!actions.TryGetValue(action, out var keys)) { actions[action] = new List<string>(); keys = actions[action]; }
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                var labelTb = new TextBlock { Text = ActionLabel(action), Width = 150, VerticalAlignment = VerticalAlignment.Center };
                var wrap = new WrapPanel();
                foreach (var token in keys.ToList())
                {
                    wrap.Children.Add(MakeKeyCell(group, action, token));
                }
                var addBtn = new Button { Content = "＋", MinWidth = 26, MinHeight = 24, Padding = new Thickness(4, 0), Tag = new KeyRef(group, action, "") };
                addBtn.Click += (_, _) => StartCapture(group, action, "", addBtn);
                row.Children.Add(labelTb);
                row.Children.Add(wrap);
                row.Children.Add(addBtn);
                rows.Children.Add(row);
            }
            _hotkeyRoot.Children.Add(Card(label, rows));
        }
    }

    private Button MakeKeyCell(string group, string action, string token)
    {
        var cell = new Button
        {
            Content = DisplayKey(token),
            Tag = new KeyRef(group, action, token),
            MinHeight = 24,
            Padding = new Thickness(8, 2),
            Background = new SolidColorBrush(Color.Parse("#2A2A34")),
            BorderBrush = new SolidColorBrush(Color.Parse("#4A4A58")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Foreground = Brushes.White,
            FontSize = 11,
        };
        cell.Click += (_, _) => StartCapture(group, action, token, cell);
        cell.ContextRequested += (_, _) =>
        {
            if (_hotkeyEdit.TryGetValue(group, out var actions) && actions.TryGetValue(action, out var keys))
                keys.Remove(token);
            RebuildHotkeyCards();
        };
        return cell;
    }

    private void StartCapture(string group, string action, string replaceToken, Button cell)
    {
        CancelCapture();
        _capturing = (group, action, replaceToken, replaceToken == "");
        _captureCell = cell;
        cell.Content = replaceToken == "" ? "新增键…" : "按新键…";
        _focusSink.Focus();   // 焦点移到中性元素：无按钮持焦，键不会被控件吞
        _captureHint.Text = $"正在为 [{ActionLabel(action)}]（{Groups.First(g => g.Group == group).Label}）设置按键：请按新键（Esc 取消）";
    }

    private void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        if (_capturing is not { } cap) return;
        e.Handled = true;
        if (e.Key == Key.Escape) { CancelCapture(); return; }
        // 纯修饰键不结束捕获，等主键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) return;
        var token = SettingsStore.KeyToken(e.Key, e.KeyModifiers);
        // 冲突检测：通用键查全表；模式组键查通用+本组
        var checkGroups = cap.Group == "General"
            ? _hotkeyEdit.Keys.ToList()
            : new List<string> { "General", cap.Group };
        foreach (var g in checkGroups)
        {
            if (!_hotkeyEdit.TryGetValue(g, out var actions)) continue;
            foreach (var (otherAction, keys) in actions)
            {
                if (otherAction == cap.Action && g == cap.Group) continue;
                if (keys.Contains(token))
                {
                    _captureHint.Text = $"按键 [{DisplayKey(token)}] 已绑定到 [{ActionLabel(otherAction)}]，请换一个键";
                    return;
                }
            }
        }
        if (!_hotkeyEdit.TryGetValue(cap.Group, out var map)) { CancelCapture(); return; }
        if (!map.TryGetValue(cap.Action, out var keyList)) { map[cap.Action] = new List<string>(); keyList = map[cap.Action]; }
        if (!cap.AddMode) keyList.Remove(cap.ReplaceToken);
        if (!keyList.Contains(token)) keyList.Add(token);
        _capturing = null;
        _captureCell = null;
        _captureHint.Text = "";
        RebuildHotkeyCards();
    }

    private void CancelCapture()
    {
        _capturing = null;
        _captureCell = null;
        _captureHint.Text = "";
    }

    // ================= 阅读页 =================

    private void RestoreReadingDefaults()
    {
        var d = new AppSettings();
        SetSlider(_zoomBase, _zoomBaseT, d.ZoomSensitivity, false);
        SetSlider(_scrollBase, _scrollBaseT, d.WebtoonSpeed, false);
        SetSlider(_friction, _frictionT, d.ScrollFriction, false);
        SetSlider(_flingThreshold, _flingThresholdT, d.DragFlingThreshold, false);
        SetSlider(_flingMax, _flingMaxT, d.DragFlingMax, false);
        SetSlider(_decodeCap, _decodeCapT, d.MaxDecodeWidth, true);
        SetSlider(_cacheCap, _cacheCapT, d.CacheCap, true);
        _invert.IsChecked = d.InvertScroll;
        _resume.IsChecked = d.ResumePosition;
        _defaultMode.SelectedIndex = 0;
        _doubleClick.SelectedIndex = 0;
        _defaultFit.SelectedIndex = 0;
        _pagedWheel.SelectedIndex = 0;
    }

    private static void SetSlider(Slider s, TextBox t, double v, bool isInt)
    {
        s.Value = v;
        t.Text = isInt ? ((int)v).ToString() : v.ToString("0.0");
    }

    /// <summary>滑块 + 数值框 双向联动（滑块拉伸填满行宽）。</summary>
    private static (Slider, TextBox) MakeSliderRow(double min, double max, double value, bool isInt)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = true,
            TickFrequency = isInt ? 1 : 0.1,
        };
        var box = new TextBox
        {
            Text = isInt ? ((int)value).ToString() : value.ToString("0.0"),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        slider.ValueChanged += (_, _) => { if (!box.IsFocused) box.Text = isInt ? ((int)slider.Value).ToString() : slider.Value.ToString("0.0"); };
        void Commit()
        {
            if (double.TryParse(box.Text, out var v))
                slider.Value = Math.Clamp(v, min, max);
            box.Text = isInt ? ((int)slider.Value).ToString() : slider.Value.ToString("0.0");
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        box.LostFocus += (_, _) => Commit();
        return (slider, box);
    }

    // ================= 通用辅助 =================

    /// <summary>分组卡片（圆角 Border + 标题 + 内容），随宽度拉伸 —— 阅读/快捷键/介绍三页统一样式。</summary>
    private static Border Card(string title, params Control[] children)
    {
        var sp = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#4A9EFF")) });
        foreach (var c in children) sp.Children.Add(c);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E26")),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.Parse("#33333E")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = sp,
        };
    }

    /// <summary>设置行：Grid [名称+说明 190 | 控件 * | 数值框 72]，名称两行（名称+说明）、间距舒展，随宽度拉伸。</summary>
    private static Control SettingRow(string label, Control control, TextBox? valueBox, string hint)
    {
        var g = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(190)));
        g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(72)));
        var labelStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        labelStack.Children.Add(new TextBlock { Text = label });
        if (!string.IsNullOrEmpty(hint))
            labelStack.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap });
        g.Children.Add(labelStack);
        Grid.SetColumn(labelStack, 0);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = control is Slider ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (control is Slider) control.Margin = new Thickness(0, 8, 0, 8);
        g.Children.Add(control);
        Grid.SetColumn(control, 1);
        if (valueBox is not null)
        {
            valueBox.VerticalAlignment = VerticalAlignment.Center;
            valueBox.Margin = new Thickness(8, 0, 0, 0);
            g.Children.Add(valueBox);
            Grid.SetColumn(valueBox, 2);
        }
        return g;
    }

    private void ApplyControlsToCopy()
    {
        _copy.ZoomSensitivity = _zoomBase.Value;
        _copy.WebtoonSpeed = _scrollBase.Value;
        _copy.DefaultMode = _defaultMode.SelectedIndex switch { 1 => "Double", 2 => "Webtoon", _ => "Single" };
        _copy.ResumePosition = _resume.IsChecked == true;
        _copy.DoubleClickAction = _doubleClick.SelectedIndex switch { 1 => "Reset", 2 => "None", _ => "Zoom" };
        _copy.DefaultFitMode = _defaultFit.SelectedIndex == 1 ? "FitWidth" : "FitScreen";
        _copy.ScrollFriction = _friction.Value;
        _copy.DragFlingThreshold = _flingThreshold.Value;
        _copy.DragFlingMax = _flingMax.Value;
        _copy.MaxDecodeWidth = (int)_decodeCap.Value;
        _copy.CacheCap = (int)_cacheCap.Value;
        _copy.InvertScroll = _invert.IsChecked == true;
        _copy.PagedWheelMode = _pagedWheel.SelectedIndex == 1 ? "Page" : "Zoom";
        _copy.Hotkeys = _hotkeyEdit;
    }

    private static AppSettings Clone(AppSettings s) => new()
    {
        ZoomSensitivity = s.ZoomSensitivity,
        WebtoonSpeed = s.WebtoonSpeed,
        DefaultMode = s.DefaultMode,
        ResumePosition = s.ResumePosition,
        DoubleClickAction = s.DoubleClickAction,
        DefaultFitMode = s.DefaultFitMode,
        ScrollFriction = s.ScrollFriction,
        DragFlingThreshold = s.DragFlingThreshold,
        DragFlingMax = s.DragFlingMax,
        MaxDecodeWidth = s.MaxDecodeWidth,
        CacheCap = s.CacheCap,
        InvertScroll = s.InvertScroll,
        PagedWheelMode = s.PagedWheelMode,
        Hotkeys = s.Hotkeys,
    };

    private static string ActionLabel(string action) => action switch
    {
        nameof(ReaderAction.NextPage) => "下一页",
        nameof(ReaderAction.PrevPage) => "上一页",
        nameof(ReaderAction.WebtoonNextPage) => "下一页整页跳",
        nameof(ReaderAction.WebtoonPrevPage) => "上一页整页跳",
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

    private static string DisplayKey(string token)
    {
        if (token.Contains('+'))
        {
            var parts = token.Split('+');
            return string.Join("+", parts.Take(parts.Length - 1).Select(m => m switch
            {
                "Ctrl" => "Ctrl",
                "Shift" => "Shift",
                "Alt" => "Alt",
                _ => m,
            })) + (parts.Length > 1 ? "+" : "") + parts[^1];
        }
        return token switch
        {
            "OemPlus" => "+",
            "Add" => "Num+",
            "OemMinus" => "−",
            "Subtract" => "Num−",
            "Space" => "空格",
            "PageDown" => "PgDn",
            "PageUp" => "PgUp",
            "Left" => "←",
            "Right" => "→",
            "Up" => "↑",
            "Down" => "↓",
            _ => token,
        };
    }

    // ================= 使用介绍渲染 =================

    /// <summary>解析 usage.md（# 标题 / ## 小节 / !img:相对路径 / 正文），返回内容与图片列表（图片宽随面板联动）。</summary>
    private static (Control Stack, List<Image> Images) RenderGuideContent()
    {
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var images = new List<Image>();
        if (!File.Exists(SettingsStore.GuidePath))
        {
            stack.Children.Add(new TextBlock { Text = "使用介绍文件不存在：" + SettingsStore.GuidePath, Foreground = Brushes.Red });
            return (stack, images);
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
                        var img = new Image
                        {
                            Source = new Avalonia.Media.Imaging.Bitmap(imgPath),
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Margin = new Thickness(0, 4),
                        };
                        images.Add(img);
                        stack.Children.Add(img);
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
        return (stack, images);
    }
}
