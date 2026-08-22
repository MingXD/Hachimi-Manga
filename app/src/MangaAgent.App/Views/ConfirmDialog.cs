using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MangaAgent.App.Views;

/// <summary>简单确认对话框（确定/取消）。</summary>
public static class ConfirmDialog
{
    /// <summary>异步确认（tcs 模式，不依赖 ShowDialog 阻塞）：窗口显示后等待按钮点击，点击确定返回 true。</summary>
    public static async Task<bool> ShowAsync(string message)
    {
        App.SLog($"CONFIRM_SHOW: {message}");
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinWidth = 260, MaxWidth = 380 };
        var ok = new Button { Content = "确定" };
        var cancel = new Button { Content = "取消" };
        var window = new Window
        {
            Title = "确认",
            Width = 360,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 14,
                Children =
                {
                    text,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, ok }
                    }
                }
            },
        };
        var tcs = new TaskCompletionSource<bool>();
        ok.Click += (_, _) => { App.SLog("CONFIRM_OK_CLICK"); tcs.TrySetResult(true); window.Close(); };
        cancel.Click += (_, _) => { App.SLog("CONFIRM_CANCEL_CLICK"); tcs.TrySetResult(false); window.Close(); };
        window.Closed += (_, _) => tcs.TrySetResult(false);   // 窗口被 X 关闭 → false
        await window.ShowDialog(GetOwner()!);
        return await tcs.Task;
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
