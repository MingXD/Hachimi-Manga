using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
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
            // 布局完成后加载可见区缩略图（BUG-002）
            Dispatcher.UIThread.Post(LoadVisibleThumbnails);
        }
    }

    private void OnCoverScrollChanged(object? sender, Avalonia.Controls.ScrollChangedEventArgs e)
        => LoadVisibleThumbnails();

    /// <summary>滚动按需加载封面缩略图（BUG-002 修复：不再只加载前 24 本）。
    /// 用固定卡片尺寸（WrapPanel ItemWidth/ItemHeight）按索引估算可见范围，无需容器 API。</summary>
    private void LoadVisibleThumbnails()
    {
        if (_vm is null || CoverScroller is null || _vm.Books.Count == 0) return;
        const double itemW = 184, itemH = 262, buffer = 300;
        double viewW = Math.Max(1, CoverScroller.Viewport.Width);
        int perRow = Math.Max(1, (int)(viewW / itemW));
        double offset = CoverScroller.Offset.Y;
        double viewH = CoverScroller.Viewport.Height;

        int i = 0;
        foreach (var item in _vm.Books)
        {
            if (item.Thumbnail is not null) { i++; continue; }
            int row = i / perRow;
            double top = row * itemH;
            if (top <= offset + viewH + buffer && top + itemH >= offset - buffer)
                _ = item.LoadThumbnailAsync();
            i++;
        }
    }

    private void OnBookDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is { SelectedBook: { } book })
            _vm.OpenBookCommand.Execute(book);
    }
}
