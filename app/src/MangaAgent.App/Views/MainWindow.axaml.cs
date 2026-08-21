using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;

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
            // F12 调试面板
            KeyDown += (_, ke) => { if (ke.Key == Avalonia.Input.Key.F12) ToggleMainDebug(); };
        }
    }

    private Views.DebugPanel? _mainDebug;
    private Avalonia.Controls.Panel? _mainOverlay;

    private void ToggleMainDebug()
    {
        if (_mainDebug is null)
        {
            // 覆盖层（同 ReaderWindow）：避免 DebugPanel 进 DockPanel 布局被 LastChildFill 铺满吞点击
            if (Content is DockPanel mdoc && _mainOverlay is null)
            {
                var overlay = new Grid();
                overlay.Children.Add(mdoc);
                Content = overlay;
                _mainOverlay = overlay;
            }
            _mainDebug = new Views.DebugPanel
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 8, 8),
                Width = 420,
            };
            _mainDebug.StatusProvider = () =>
            {
                if (_vm is null) return "vm=null";
                return $"库:{_vm.Libraries.Count} | 扫描中:{_vm.IsScanning} | 状态:{_vm.StatusText}\n" +
                       $"当前目录:{_vm.CurrentFolder?.Path ?? "无"} | 条目:{_vm.FolderEntries.Count}\n" +
                       $"展示模式:{_vm.GalleryModeText}";
            };
            _mainDebug.Start();
            (_mainOverlay ?? (Avalonia.Controls.Panel)Content).Children.Add(_mainDebug);
        }
        _mainDebug.IsVisible = !_mainDebug.IsVisible;
    }

    private void OnCoverScrollChanged(object? sender, Avalonia.Controls.ScrollChangedEventArgs e)
        => LoadVisibleThumbnails();

    /// <summary>滚动按需加载封面缩略图（BUG-002 修复：不再只加载前 24 本）。
    /// 用固定卡片尺寸（WrapPanel ItemWidth/ItemHeight）按索引估算可见范围，无需容器 API。</summary>
    private void LoadVisibleThumbnails()
    {
        if (_vm is null || CoverScroller is null) return;
        var items = _vm.FolderEntries.Count > 0 ? (System.Collections.Generic.IReadOnlyList<FolderEntryItemViewModel>)_vm.FolderEntries
                                                : System.Array.Empty<FolderEntryItemViewModel>();
        if (items.Count == 0) return;
        const double itemW = 184, itemH = 262, buffer = 300;
        double viewW = Math.Max(1, CoverScroller.Viewport.Width);
        int perRow = Math.Max(1, (int)(viewW / itemW));
        double offset = CoverScroller.Offset.Y;
        double viewH = CoverScroller.Viewport.Height;

        int i = 0;
        foreach (var item in items)
        {
            if (item.IsFolder || item.Thumbnail is not null) { i++; continue; }
            int row = i / perRow;
            double top = row * itemH;
            if (top <= offset + viewH + buffer && top + itemH >= offset - buffer)
                _ = item.LoadThumbnailAsync();
            i++;
        }
    }

    private void OnBookDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        // 目录浏览器：双击文件夹 → 进入下一层；双击漫画 → 打开
        if (e.Source is Avalonia.Controls.Control ctl && ctl.DataContext is FolderEntryItemViewModel entry)
        {
            if (entry.IsFolder) _vm.EnterFolderCommand.Execute(entry.Folder);
            else if (entry.Book is { } book) _vm.OpenBookCommand.Execute(new BookItemViewModel(book));
        }
    }

    /// <summary>面包屑点击 → 跳转到对应层级。</summary>
    private void OnBreadcrumbClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FolderNode node } && _vm is not null)
            _vm.ShowFolderContents(node);
    }

    private void RefreshLibrary()
    {
        if (_vm?.SelectedLibrary is { } lib)
            _vm.RescanCommand.Execute(null);
    }
}
