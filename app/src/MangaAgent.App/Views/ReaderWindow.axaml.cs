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

    // ---- 鼠标拖拽 = 画面平移（YACReader/Perfect Viewer 标准；翻页交给滚轮/键盘）----
    private bool _isDragging;
    private Avalonia.Point _dragStart;

    private void OnScrollerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(Scroller);
            e.Handled = true;
        }
    }

    private void OnScrollerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _vm is null) return;
        var pos = e.GetPosition(Scroller);
        double dx = pos.X - _dragStart.X;
        double dy = pos.Y - _dragStart.Y;
        _dragStart = pos;
        var off = Scroller.Offset;
        // 用 Extent（内容总尺寸）而非 Panel.Bounds（ScrollViewer 内被 Arrange 成视口宽）
        double maxX = Math.Max(0, Scroller.Extent.Width - Scroller.Viewport.Width);
        double maxY = Math.Max(0, Scroller.Extent.Height - Scroller.Viewport.Height);
        Scroller.Offset = PanOffset(off, dx, dy, maxX, maxY);
        e.Handled = true;
    }

    private void OnScrollerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
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
        else
        {
            // 滚轮 = 翻页（分页模式，CDisplayEx/YACReader 标准）：下滚=下一页，上滚=上一页
            if (e.Delta.Y != 0)
            {
                if (e.Delta.Y > 0) _vm.PrevPageCommand.Execute(null);
                else _vm.NextPageCommand.Execute(null);
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

    /// <summary>拖拽平移计算（纯函数，可单测）：offset 跟随拖动位移，限制在内容范围内。</summary>
    public static Avalonia.Vector PanOffset(Avalonia.Vector off, double dx, double dy, double maxX, double maxY)
        => new(
            Math.Clamp(off.X - dx, 0, Math.Max(0, maxX)),
            Math.Clamp(off.Y - dy, 0, Math.Max(0, maxY)));

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

    }
}
