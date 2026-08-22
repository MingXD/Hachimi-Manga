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
    private readonly CheckBox _invert;
    private readonly ComboBox _pagedWheel;

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

        // —— 页签一：阅读（滑块 + 数值框 双向联动）——
        (_zoomBase, _zoomBaseT) = MakeSliderRow("缩放速度基准", 0.5, 2.0, _copy.ZoomSensitivity, "滚轮缩放每格幅度", false);
        (_scrollBase, _scrollBaseT) = MakeSliderRow("滚动速度基准", 0.5, 2.0, _copy.WebtoonSpeed, "滚动模式一档距离", false);
        (_friction, _frictionT) = MakeSliderRow("停止快慢", 3, 12, _copy.ScrollFriction, "摩擦系数（越大停得越快）", false);
        (_flingThreshold, _flingThresholdT) = MakeSliderRow("甩动触发阈值", 50, 400, _copy.DragFlingThreshold, "拖拽释放多快才算甩动（px/s）", false);
        (_flingMax, _flingMaxT) = MakeSliderRow("甩动限幅", 1000, 6000, _copy.DragFlingMax, "甩动最大速度（px/s）", false);
        (_decodeCap, _decodeCapT) = MakeSliderRow("解码宽度上限", 1200, 4096, _copy.MaxDecodeWidth, "大图解码分辨率上限", true);
        (_cacheCap, _cacheCapT) = MakeSliderRow("缓存页数", 4, 20, _copy.CacheCap, "位图缓存上限", true);
        _invert = new CheckBox { Content = "滚动方向反转（自然滚动）", IsChecked = _copy.InvertScroll, Margin = new Thickness(0, 4, 0, 0) };
        _pagedWheel = new ComboBox { ItemsSource = new[] { "缩放", "翻页" }, SelectedIndex = _copy.PagedWheelMode == "Page" ? 1 : 0, Width = 140 };
        var tab1 = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(14), Spacing = 8, Children =
        {
            Group("阅读基准",
                Row("缩放速度基准", _zoomBase, _zoomBaseT, "滚轮缩放每格幅度（0.5~2.0）"),
                Row("滚动速度基准", _scrollBase, _scrollBaseT, "滚动模式一档距离（0.5~2.0）")),
            Group("滚动手感（滚动模式）",
                Row("停止快慢", _friction, _frictionT, "摩擦系数（越大停得越快）"),
                Row("甩动触发阈值", _flingThreshold, _flingThresholdT, "拖拽释放多快才算甩动（px/s）"),
                Row("甩动限幅", _flingMax, _flingMaxT, "甩动最大速度（px/s）")),
            Group("性能与行为",
                Row("解码宽度上限", _decodeCap, _decodeCapT, "大图解码分辨率上限（清晰度 vs 速度/内存）"),
                Row("缓存页数", _cacheCap, _cacheCapT, "位图缓存上限（流畅 vs 内存）"),
                Row("分页滚轮行为", _pagedWheel, null, "分页模式下滚轮 = 缩放 或 翻页"),
                _invert),
        } } };

        // —— 页签二：快捷键（四组竖排卡片）——
        RebuildHotkeyCards();
        var tab2 = new ScrollViewer { Content = new StackPanel { Margin = new Thickness(14), Spacing = 8, Children =
        {
            new TextBlock { Text = "点击键块 = 替换该键；右键键块 = 删除；行尾 [+] = 新增一个键。支持组合键（如 Ctrl+Shift+P）。", Foreground = Brushes.Gray, FontSize = 11 },
            _captureHint,
            _hotkeyRoot,
        } } };

        // —— 页签三：使用介绍 ——
        var tab3 = new ScrollViewer { Content = RenderGuide() };

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
        var pathInfo = new TextBlock
        {
            Text = $"设置：{SettingsStore.SettingsPath}\n介绍：{SettingsStore.GuidePath}",
            Foreground = Brushes.Gray, FontSize = 10, TextWrapping = TextWrapping.Wrap,
        };
        var bottom = new DockPanel { Margin = new Thickness(14, 8, 14, 14) };
        DockPanel.SetDock(pathInfo, Dock.Right);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { restoreRead, restoreHot, save, cancel, openGuide } };
        bottom.Children.Add(pathInfo);
        bottom.Children.Add(buttons);

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
            var card = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1E1E26")),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.Parse("#33333E")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
            };
            var rows = new StackPanel { Spacing = 6 };
            rows.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#4A9EFF")), Margin = new Thickness(0, 0, 0, 4) });
            foreach (var action in GroupActions.First(x => x.Group == group).Actions)
            {
                if (!_hotkeyEdit.TryGetValue(group, out var actions)) continue;
                if (!actions.TryGetValue(action, out var keys)) { actions[action] = new List<string>(); keys = actions[action]; }
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                var labelTb = new TextBlock { Text = ActionLabel(action), Width = 150, VerticalAlignment = VerticalAlignment.Center };
                var wrap = new WrapPanel();
                foreach (var token in keys.ToList())
                {
                    var cell = MakeKeyCell(group, action, token);
                    wrap.Children.Add(cell);
                }
                var addBtn = new Button { Content = "＋", MinWidth = 26, MinHeight = 24, Padding = new Thickness(4, 0), Tag = new KeyRef(group, action, "") };
                addBtn.Click += (_, _) => StartCapture(group, action, "", addBtn);
                row.Children.Add(labelTb);
                row.Children.Add(wrap);
                row.Children.Add(addBtn);
                rows.Children.Add(row);
            }
            card.Child = rows;
            _hotkeyRoot.Children.Add(card);
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
        _pagedWheel.SelectedIndex = 0;
    }

    private static void SetSlider(Slider s, TextBox t, double v, bool isInt)
    {
        s.Value = v;
        t.Text = isInt ? ((int)v).ToString() : v.ToString("0.0");
    }

    /// <summary>滑块 + 数值框 双向联动。</summary>
    private static (Slider, TextBox) MakeSliderRow(string label, double min, double max, double value, string hint, bool isInt)
    {
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = value, Width = 240, VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = true, TickFrequency = isInt ? 1 : 0.1,
        };
        var box = new TextBox
        {
            Text = isInt ? ((int)value).ToString() : value.ToString("0.0"),
            Width = 64, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center,
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

    private static StackPanel Group(string title, params Control[] children)
    {
        var sp = new StackPanel { Spacing = 6 };
        sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14, Foreground = new SolidColorBrush(Color.Parse("#4A9EFF")), Margin = new Thickness(0, 4, 0, 2) });
        foreach (var c in children) sp.Children.Add(c);
        return sp;
    }

    private static StackPanel Row(string label, Control control, TextBox? valueBox, string hint)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        sp.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(control);
        if (valueBox is not null) sp.Children.Add(valueBox);
        sp.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, FontSize = 10, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return sp;
    }

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
                "Ctrl" => "Ctrl", "Shift" => "Shift", "Alt" => "Alt", _ => m,
            })) + (parts.Length > 1 ? "+" : "") + parts[^1];
        }
        return token switch
        {
            "OemPlus" => "+", "Add" => "Num+",
            "OemMinus" => "−", "Subtract" => "Num−",
            "Space" => "空格",
            "PageDown" => "PgDn", "PageUp" => "PgUp",
            "Left" => "←", "Right" => "→", "Up" => "↑", "Down" => "↓",
            _ => token,
        };
    }

    // ================= 使用介绍渲染 =================

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
                        stack.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(imgPath), Stretch = Stretch.Uniform, MaxWidth = 620, Margin = new Thickness(0, 4) });
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
