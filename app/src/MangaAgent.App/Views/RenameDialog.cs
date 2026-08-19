using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace MangaAgent.App.Views;

/// <summary>简单重命名对话框。</summary>
public static class RenameDialog
{
    public static string? Show(string currentName)
    {
        string? result = null;
        var textBox = new TextBox { Text = currentName, MinWidth = 280 };
        var ok = new Button { Content = "确定" };
        var cancel = new Button { Content = "取消" };
        var window = new Window
        {
            Title = "重命名",
            Width = 340,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12, Children = { textBox, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } } } },
        };
        ok.Click += (_, _) => { result = textBox.Text; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.ShowDialog(GetOwner()!);
        return result;
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive);
}
