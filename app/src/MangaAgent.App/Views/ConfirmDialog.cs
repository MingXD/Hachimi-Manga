using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MangaAgent.App.Views;

/// <summary>简单确认对话框（确定/取消）。</summary>
public static class ConfirmDialog
{
    public static bool Show(string message)
    {
        bool result = false;
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
        ok.Click += (_, _) => { result = true; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.ShowDialog(GetOwner()!);
        return result;
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
