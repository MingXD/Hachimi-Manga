using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.Views;

public partial class ReaderWindow : Window
{
    private ReaderViewModel? _vm;

    public ReaderWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ReaderViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ReaderViewModel.Scale)
                    or nameof(ReaderViewModel.IsDoublePageShown)
                    or nameof(ReaderViewModel.Image1)
                    or nameof(ReaderViewModel.Image2))
                {
                    Relayout();
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

    private void OnScrollerSizeChanged(object? sender, SizeChangedEventArgs e) => Relayout();

    // ---- P0-4 鼠标拖拽：未放大时水平拖拽翻页（RTL 感知）；放大时拖拽平移 ----
    private bool _isDragging;
    private Avalonia.Point _dragStart;
    private double _dragAccumX;
    private bool _dragWasPan;

    private void OnScrollerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragAccumX = 0;
            _dragWasPan = false;
            _dragStart = e.GetPosition(Scroller);
            e.Pointer.Capture(Scroller);
            e.Handled = true;
        }
    }

    private void OnScrollerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _vm is null) return;
        var pos = e.GetPosition(Scroller);
        double dx = pos.X - _dragStart.X;
        _dragStart = pos;
        bool hOverflow = PagePanel.Bounds.Width > Scroller.Bounds.Width + 1;
        if (hOverflow)
        {
            _dragWasPan = true;
            var off = Scroller.Offset;
            Scroller.Offset = new Avalonia.Vector(Math.Max(0, off.X - dx), off.Y);
        }
        else
        {
            _dragAccumX += dx;
        }
        e.Handled = true;
    }

    private void OnScrollerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        e.Pointer.Capture(null);
        if (_vm is not null && !_dragWasPan && _dragAccumX != 0 && Math.Abs(_dragAccumX) > 60)
        {
            // RTL：向左拖 = 下一页；LTR 反之
            bool next = _vm.Direction == ReadingDirection2.Rtl ? _dragAccumX < 0 : _dragAccumX > 0;
            if (next) _vm.NextPageCommand.Execute(null);
            else _vm.PrevPageCommand.Execute(null);
        }
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // 以光标为中心缩放（YACReader 约定）：缩放前后保持光标处的内容点不动
            double px = e.GetPosition(Scroller).X / Math.Max(1, Scroller.Bounds.Width);
            double py = e.GetPosition(Scroller).Y / Math.Max(1, Scroller.Bounds.Height);
            double oldK = CurrentScaleK();
            if (e.Delta.Y > 0) _vm.ZoomInCommand.Execute(null);
            else if (e.Delta.Y < 0) _vm.ZoomOutCommand.Execute(null);
            double newK = CurrentScaleK();
            if (oldK > 0 && Math.Abs(newK - oldK) > 0.001)
            {
                double ratio = newK / oldK;
                var off = Scroller.Offset;
                double vpW = Scroller.Bounds.Width, vpH = Scroller.Bounds.Height;
                double tx = (off.X + px * vpW) * ratio - px * vpW;
                double ty = (off.Y + py * vpH) * ratio - py * vpH;
                Scroller.Offset = new Avalonia.Vector(Math.Max(0, tx), Math.Max(0, ty));
            }
            e.Handled = true;
        }
    }

    /// <summary>当前全局缩放系数（内容像素 / 原始比例基准 1）。</summary>
    private double CurrentScaleK()
    {
        if (_vm is null || PageImage1 is null) return 0;
        return PageImage1.Height > 0 ? PageImage1.Height : 0;
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

    private void Relayout()
    {
        if (_vm is null || Scroller is null) return;
        double vpW = Math.Max(100, Scroller.Bounds.Width);
        double vpH = Math.Max(100, Scroller.Bounds.Height);
        const double gutter = 10;

        // 内容基准尺寸（高度基准 1，宽度按页宽高比求和）
        double r1 = Math.Max(0.05, _vm.Ratio1);
        double baseW = _vm.IsDoublePageShown && _vm.Image2 is not null
            ? r1 + Math.Max(0.05, _vm.Ratio2)
            : r1;
        double baseH = 1.0;

        // 全局缩放系数 k（内容高度 = k 像素）
        double k;
        switch (_vm.FitMode)
        {
            case FitMode.FitHeight: k = vpH / baseH; break;
            case FitMode.FitScreen: k = Math.Min(vpW / baseW, vpH / baseH); break;
            case FitMode.Custom: k = (vpW / baseW) * Math.Max(0.25, _vm.Scale); break;
            default: k = vpW / baseW; break;   // FitWidth
        }

        double h1 = k, h2 = k;
        double w1 = r1 * k;
        double w2 = _vm.IsDoublePageShown && _vm.Image2 is not null ? Math.Max(0.05, _vm.Ratio2) * k : 0;
        PageImage1.Width = Math.Max(1, w1); PageImage1.Height = Math.Max(1, h1);
        PageImage2.Width = Math.Max(1, w2); PageImage2.Height = Math.Max(1, h2);

        // 解码目标宽度 = 显示尺寸×DPI（上限 4096）：4K 扫描页按需降采样
        double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
        double displayW = _vm.IsDoublePageShown && _vm.Image2 is not null ? w1 + w2 + gutter : w1;
        _vm.DecodeTargetWidth = (int)Math.Clamp(displayW * dpr, 1600, 4096);

        // 垂直居中：内容高度小于视口时，用 ScrollViewer 内容 margin 近似居中（水平已由 HorizontalContentAlignment 处理）
        double contentH = PagePanel.Bounds.Height;
        if (contentH < vpH && contentH > 0)
        {
            PagePanel.Margin = new Avalonia.Thickness(0, Math.Max(0, (vpH - contentH) / 2), 0, 0);
        }
        else
        {
            PagePanel.Margin = new Avalonia.Thickness(10);
        }
    }
}
