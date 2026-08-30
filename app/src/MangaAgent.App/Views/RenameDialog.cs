using Avalonia.Controls;
using Avalonia.Layout;

namespace MangaAgent.App.Views;

/// <summary>简单重命名对话框。
/// v3 #2 修复：ShowDialog 异步不阻塞，原同步写法"调用后立即返回 null"→ 重命名无效；
/// 改 await + tcs 模式（同 ConfirmDialog/NewFolderDialog）。</summary>
public static class RenameDialog
{
    public static async Task<string?> ShowAsync(string currentName)
    {
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
        var tcs = new TaskCompletionSource<string?>();
        ok.Click += (_, _) => { tcs.TrySetResult(textBox.Text); window.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(null); window.Close(); };
        window.Closed += (_, _) => tcs.TrySetResult(null);   // X 关闭 → null
        await window.ShowDialog(GetOwner()!);
        return await tcs.Task;
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive);
}
