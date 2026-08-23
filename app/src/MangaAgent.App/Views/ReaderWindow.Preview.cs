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

/// <summary>ReaderWindow partial —— Preview 领域（code-audit P1 分文件，纯搬移）。</summary>
public partial class ReaderWindow : Window
{
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

    private void StopHoverTimer() => _hoverTimer?.Stop();

    private void HidePreviewStripDelayed()
    {
        StartHoverTimer(300, () => { if (!IsPointerOver(PreviewStrip)) PreviewStrip.IsVisible = false; });
    }

    private new bool IsPointerOver(Avalonia.Controls.Control? ctl)
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

    private void TogglePreviewStrip()
    {
        if (PreviewStrip is null) return;
        if (PreviewStrip.IsVisible) { PreviewStrip.IsVisible = false; StopHoverTimer(); }
        else ShowPreviewStrip();
    }

}
