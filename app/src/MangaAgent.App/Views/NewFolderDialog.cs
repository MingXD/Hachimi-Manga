using Avalonia.Controls;
using Avalonia.Layout;

namespace MangaAgent.App.Views;

/// <summary>新建文件夹对话框（v3 #6）：输入名称，确定返回名称（取消/X 返回 null）。
/// v3 #2 修复：ShowDialog 是异步的（返回 Task 不阻塞），原同步写法"调用后立即返回 null"
/// → 新建/重命名"完全无效"。改 await + tcs 模式（同 ConfirmDialog）。</summary>
public static class NewFolderDialog
{
    public static async Task<string?> ShowAsync()
    {
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
        var tcs = new TaskCompletionSource<string?>();
        ok.Click += (_, _) => { tcs.TrySetResult(textBox.Text); window.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(null); window.Close(); };
        window.Closed += (_, _) => tcs.TrySetResult(null);   // X 关闭 → null
        window.Opened += (_, _) => textBox.Focus();
        await window.ShowDialog(GetOwner()!);
        return await tcs.Task;
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive);
}
