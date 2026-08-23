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

/// <summary>ReaderWindow partial —— Input 领域（code-audit P1 分文件，纯搬移）。</summary>
public partial class ReaderWindow : Window
{
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        if (IsOverPreviewStrip(e.Source)) return;   // 预览栏滚轮翻页，不缩放
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || _ctrlDown;
        if (_settings.DebugLog) App.SLog($"WHEEL_EVT mode={_vm.ViewMode} dy={e.Delta.Y:F1} ctrl={ctrl}");   // 高频项：DebugLog 开才记
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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (ResolveAction(e.Key, e.KeyModifiers) is not { } action) return;
        App.SLog($"KEY {SettingsStore.KeyToken(e.Key, e.KeyModifiers)} -> {action}");
        ExecuteAction(action);
        e.Handled = true;
    }

    private ReaderAction? ResolveAction(Key key, KeyModifiers modifiers)
    {
        if (_settings?.Hotkeys is not { } map) return null;
        string modeGroup = _vm?.ViewMode switch
        {
            ViewMode.Webtoon => "Webtoon",
            _ => _vm?.Direction == ReadingDirection2.Ltr ? "PagedLtr" : "PagedRtl",
        };
        if (TryMatch(map, modeGroup, key, modifiers, out var a)) return a;
        if (TryMatch(map, "General", key, modifiers, out a)) return a;
        return null;
    }

    private static bool TryMatch(Dictionary<string, Dictionary<string, List<string>>> map, string group,
        Key key, KeyModifiers modifiers, out ReaderAction? action)
    {
        action = null;
        if (!map.TryGetValue(group, out var actions)) return false;
        string token = SettingsStore.KeyToken(key, modifiers);
        foreach (var (actionName, keys) in actions)
        {
            if (keys is null) continue;
            if (keys.Contains(token)) { action = ParseAction(actionName); return true; }
        }
        return false;
    }

    private static ReaderAction? ParseAction(string name)
        => Enum.TryParse<ReaderAction>(name, out var a) ? a : null;

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

}
