using Avalonia.Controls;
using Avalonia.Layout;

namespace MangaAgent.App.Views;

/// <summary>新建文件夹对话框（v3 #6）：输入名称，确定返回名称（可为空/取消返回 null）。</summary>
public static class NewFolderDialog
{
    public static string? Show()
    {
        string? result = null;
        var textBox = new TextBox { MinWidth = 280, PlaceholderText = "文件夹名称" };
        var ok = new Button { Content = "确定" };
        var cancel = new Button { Content = "取消" };
        var window = new Window
        {
            Title = "新建文件夹",
            Width = 340,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12, Children = { textBox, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } } } },
        };
        ok.Click += (_, _) => { result = textBox.Text; window.Close(); };
        cancel.Click += (_, _) => window.Close();
        window.Opened += (_, _) => textBox.Focus();
        window.ShowDialog(GetOwner()!);
        return result;
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive);
}
