using Avalonia.Controls;
using Avalonia.Layout;
using MangaAgent.Core.Library;

namespace MangaAgent.App.Views;

/// <summary>属性对话框。</summary>
public static class PropertiesDialog
{
    public static void Show(BookInfo book)
    {
        long size = 0;
        try
        {
            size = Directory.Exists(book.Path)
            ? Directory.GetFiles(book.Path).Sum(f => new FileInfo(f).Length)
            : new FileInfo(book.Path).Length;
        }
        catch { }
        // v3：SelectableTextBlock —— 属性文本可选择复制（原 TextBlock 不可选）
        var info = new SelectableTextBlock
        {
            Text = $"名称：{book.Title}\n路径：{book.Path}\n类型：{(book.Kind == BookKind.Zip ? "CBZ 压缩包" : book.Kind == BookKind.Pdf ? "PDF 文档" : "文件夹")}\n页数：{book.PageCount}\n大小：{size / 1024d / 1024d:F1} MB\n修改时间：{book.ModifiedAt:yyyy-MM-dd HH:mm}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinWidth = 360,
        };
        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        var window = new Window
        {
            Title = "属性",
            Width = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Avalonia.Thickness(16), Spacing = 12, Children = { info, ok } },
        };
        ok.Click += (_, _) => window.Close();
        window.ShowDialog(GetOwner()!);
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive);
}
