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

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Delta.Y > 0) _vm.ZoomInCommand.Execute(null);
            else if (e.Delta.Y < 0) _vm.ZoomOutCommand.Execute(null);
            e.Handled = true;
        }
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
        double vp = Math.Max(200, Scroller.Bounds.Width - 28);
        double scale = Math.Max(0.25, _vm.Scale);
        if (_vm.IsDoublePageShown && _vm.Image2 is not null)
        {
            double total = vp * scale;
            double w = (total - 10) / 2;
            PageImage1.Width = w; PageImage1.Height = w / Math.Max(0.05, _vm.Ratio1);
            PageImage2.Width = w; PageImage2.Height = w / Math.Max(0.05, _vm.Ratio2);
        }
        else
        {
            double w = vp * scale;
            PageImage1.Width = w; PageImage1.Height = w / Math.Max(0.05, _vm.Ratio1);
            PageImage2.Width = 0; PageImage2.Height = 0;
        }
    }
}
