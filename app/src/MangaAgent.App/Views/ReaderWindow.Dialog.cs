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

/// <summary>ReaderWindow partial —— Dialog 领域（code-audit P1 分文件，纯搬移）。</summary>
public partial class ReaderWindow : Window
{
    private void ToggleDebugPanel()
    {
        // 调试面板独立窗口（v1.9.6）：不占阅读器空间、不干扰按钮点击
        if (_debugWindow is null)
        {
            _debugWindow = new Views.DebugWindow();
            _debugWindow.StatusProvider = () =>
            {
                if (_vm is null) return "vm=null";
                bool dragStuck = _isDragging;
                bool timerRunning = _scrollTimer?.IsEnabled == true;
                return $"模式:{_vm.ViewMode} | 缩放:{_scale:F2} | tx:{_tx:F0} ty:{_ty:F0}\n" +
                       $"物理页:{_vm.CurrentPhysical + 1}/{_vm.PhysicalPageCount} | {_vm.PageText}\n" +
                       $"方向:{_vm.DirectionText} | 配对:{_vm.PairText} | Fit:{_vm.FitMode}\n" +
                       $"拖拽卡死:{dragStuck} | 滚动Timer空转:{timerRunning} | 图1:{(_vm.Image1 is null ? "无" : "有")} 图2:{(_vm.Image2 is null ? "无" : "有")}";
            };
            _debugWindow.Start(_debugWindow.StatusProvider);
        }
        if (_debugWindow.IsVisible) _debugWindow.Hide();
        else _debugWindow.Show();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new Views.SettingsDialog(_settings);
        await dialog.ShowDialog(this);
        _settings = SettingsStore.Load();   // 对话框保存后重新加载
        ApplySettings();
    }

    private async void ShowJumpDialog()
    {
        if (_vm is null) return;
        var dialog = new Window
        {
            Title = "快速跳页",
            Width = 320,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(_vm.IsDark ? "#1C1C24" : "#FFFFFF")),
        };
        var total = _vm.ViewMode == ViewMode.Webtoon ? _vm.PhysicalPageCount : _vm.TotalLogical;
        var cur = _vm.ViewMode == ViewMode.Webtoon
            ? Math.Clamp(PhysicalAt(Math.Max(0, (-_ty) / Math.Max(0.001, _scale))), 0, total - 1) + 1
            : _vm.CurrentIndex + 1;
        var text = new TextBox { Text = cur.ToString(), Margin = new Thickness(16, 18, 16, 8), FontSize = 16 };
        var info = new TextBlock
        {
            Text = $"共 {total} 页（当前 {cur}）。输入页码后回车。",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#888888")),
            Margin = new Thickness(16, 0, 16, 4),
        };
        var panel = new StackPanel();
        panel.Children.Add(text);
        panel.Children.Add(info);
        dialog.Content = panel;
        text.KeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Enter)
            {
                if (int.TryParse(text.Text?.Trim(), out int p) && p >= 1)
                {
                    App.SLog($"JUMP_DIALOG -> {p}/{total}");
                    if (_vm.ViewMode == ViewMode.Webtoon)
                    {
                        _vm.JumpToPhysicalCommand.Execute(p - 1);
                        ScrollWebtoonToPhysical(p - 1);   // 视图滚动定位（原缺失 → 跳转无效，scroll-mode-fix A）
                    }
                    else _vm.JumpToPageCommand.Execute(p - 1);
                }
                dialog.Close();
            }
            else if (ke.Key == Key.Escape) dialog.Close();
        };
        dialog.Opened += (_, _) => { text.Focus(); text.SelectAll(); };
        await dialog.ShowDialog(this);
    }

}
