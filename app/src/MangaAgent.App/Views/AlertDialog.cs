using Avalonia.Controls;
using Avalonia.Layout;

namespace MangaAgent.App.Views;

/// <summary>简单提示对话框（仅确定按钮；v3 #3 文件操作失败提示等）。</summary>
public static class AlertDialog
{
    public static void Show(string message)
    {
        var text = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MinWidth = 260, MaxWidth = 380 };
        var ok = new Button { Content = "确定" };
        var window = new Window
        {
            Title = "提示",
            Width = 380,
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
                        Children = { ok }
                    }
                }
            },
        };
        ok.Click += (_, _) => window.Close();
        window.ShowDialog(GetOwner()!);
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
