using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            // v3 #4：文件管理器式快捷键（Ctrl+C/X/V、Delete、F2、Enter、Ctrl+A、Backspace/Alt+↑）
            KeyDown += OnMainKeyDown;
            // v3 #4：鼠标侧键前进/后退（历史栈）
            this.AddHandler(PointerPressedEvent, (_, pe) =>
            {
                var props = pe.GetCurrentPoint(this).Properties;
                if (props.IsXButton1Pressed) _vm?.GoBackCommand.Execute(null);
                else if (props.IsXButton2Pressed) _vm?.GoForwardCommand.Execute(null);
            }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        }
    }

    /// <summary>v3 #4：资源管理器式快捷键分发（焦点在文本框时不劫持——搜索框内 Ctrl+C/V 是文本操作）。</summary>
    private void OnMainKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || _vm is null) return;
        // 焦点在搜索框时不劫持（Ctrl+C/V 等留给文本编辑）：键盘事件源即焦点控件，沿可视树找 TextBox
        for (var v = e.Source as Visual; v is not null; v = v.GetVisualParent())
            if (v is TextBox) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (ctrl && e.Key == Key.C) { CopySelected(false); e.Handled = true; }
        else if (ctrl && e.Key == Key.X) { CopySelected(true); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { _ = _vm.PasteClipboardAsync(); e.Handled = true; }
        else if (ctrl && e.Key == Key.A) { SelectAllEntries(); e.Handled = true; }
        else if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; }
        else if (e.Key == Key.F2) { RenameSelected(); e.Handled = true; }
        else if (e.Key == Key.Enter || e.Key == Key.Return) { OpenSelected(); e.Handled = true; }
        else if (e.Key == Key.Back || (alt && e.Key == Key.Up)) { _vm.GoUpCommand.Execute(null); e.Handled = true; }
    }

    /// <summary>v3 #4 修复：列表选中变化时把键盘焦点带到列表——点击条目后 Enter 不再被搜索框拦截（用户"Enter 无效"根因）。</summary>
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox lb && lb.SelectedItems.Count > 0) lb.Focus();
    }

    /// <summary>v3 #4 补充：点击列表任意处（含已选中条目，选中不变不触发 SelectionChanged）也聚焦列表。</summary>
    private void OnListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is ListBox lb) lb.Focus();
    }

    private List<FolderEntryItemViewModel> SelectedEntries()
    {
        if (_vm is null) return new();
        var sel = _vm.IsDetailView ? DetailList.SelectedItems : BooksList.SelectedItems;
        return sel?.OfType<FolderEntryItemViewModel>().ToList() ?? new();
    }

    private void CopySelected(bool cut)
    {
        var paths = SelectedEntries().Select(x => x.SourcePath).ToList();
        if (paths.Count == 0) return;
        if (cut) _vm?.CutToClipboard(paths);
        else _vm?.CopyToClipboard(paths);
    }

    private void DeleteSelected()
    {
        var paths = SelectedEntries().Select(x => x.SourcePath).ToList();
        if (paths.Count > 0) _vm?.DeletePaths(paths);
    }

    private void SelectAllEntries()
    {
        if (_vm is null) return;
        if (_vm.IsDetailView) DetailList?.SelectAll();
        else BooksList?.SelectAll();
    }

    private void RenameSelected()
    {
        var sel = SelectedEntries();
        if (sel.Count == 1) sel[0].RenameCommand.Execute(null);
    }

    private void OpenSelected()
    {
        if (_vm is null) return;
        var sel = SelectedEntries();
        if (sel.Count != 1) return;
        var entry = sel[0];
        if (entry.IsFolder) _vm.EnterFolderCommand.Execute(entry.Folder);
        else if (entry.Book is { } book) _vm.OpenBookCommand.Execute(new BookItemViewModel(book));
    }

    private Views.DebugWindow? _mainDebugWindow;

    private void ToggleMainDebug()
    {
        if (_mainDebugWindow is null)
        {
            _mainDebugWindow = new Views.DebugWindow();
            _mainDebugWindow.StatusProvider = () =>
            {
                if (_vm is null) return "vm=null";
                return $"库:{_vm.Libraries.Count} | 扫描中:{_vm.IsScanning} | 状态:{_vm.StatusText}\n" +
                       $"当前目录:{_vm.CurrentFolder?.Path ?? "无"} | 条目:{_vm.FolderEntries.Count}\n" +
                       $"展示模式:{_vm.GalleryModeText}";
            };
            _mainDebugWindow.Start(_mainDebugWindow.StatusProvider);
        }
        if (_mainDebugWindow.IsVisible) _mainDebugWindow.Hide();
        else _mainDebugWindow.Show();
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
