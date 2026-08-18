using Avalonia.Controls;
using Avalonia.Input;
using MangaAgent.App.ViewModels;

namespace MangaAgent.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MainWindowViewModel vm && _vm is null)
        {
            _vm = vm;
            vm.GetOwnerWindow = () => this;
        }
    }

    private void OnBookDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is { SelectedBook: { } book })
            _vm.OpenBookCommand.Execute(book);
    }
}
