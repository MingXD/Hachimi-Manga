using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;

namespace MangaAgent.App.Views;

/// <summary>v4.15 文件夹属性对话框：基本信息立即显示；大小在后台递归计算并节流回填（"已算 X / N 文件"）；
/// 关闭窗口即取消后台计算（CancellationToken）。</summary>
public static class FolderPropertiesDialog
{
    public static void Show(FolderNode folder)
    {
        var sizeLine = new TextBlock { Text = "大小：计算中…", FontSize = 13, TextWrapping = TextWrapping.Wrap };
        string modified;
        try { modified = Directory.GetLastWriteTime(folder.Path).ToString("yyyy-MM-dd HH:mm"); }
        catch { modified = "-"; }
        var info = new SelectableTextBlock
        {
            Text = $"名称：{folder.Name}\n路径：{folder.Path}\n类型：文件夹\n" +
                   $"本层：{FolderEntryItemViewModel.CountDirectBooks(folder)} 本漫画 · " +
                   $"{FolderEntryItemViewModel.CountDirectSubfolders(folder)} 个子文件夹\n" +
                   $"目录下共 {FolderEntryItemViewModel.CountBooksRecursive(folder)} 本漫画\n" +
                   $"修改时间：{modified}",
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 380,
        };
        var ok = new Button { Content = "确定", HorizontalAlignment = HorizontalAlignment.Right };
        var window = new Window
        {
            Title = "属性",
            Width = 440,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 10,
                Children = { info, sizeLine, ok },
            },
        };
        ok.Click += (_, _) => window.Close();
        var cts = new CancellationTokenSource();
        window.Closed += (_, _) => cts.Cancel();

        void SetSizeText(string text)
        {
            if (cts.IsCancellationRequested) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { sizeLine.Text = text; } catch { }
            });
        }

        var progress = new Progress<FolderStats.Result>(r =>
            SetSizeText($"大小：计算中… 已算 {FolderStats.FormatSize(r.TotalBytes)} / {r.Files:N0} 个文件"));

        window.ShowDialog(GetOwner()!);
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await FolderStats.ComputeAsync(folder.Path, progress, cts.Token);
                if (cts.IsCancellationRequested) return;
                var text = $"大小：{FolderStats.FormatSize(result.TotalBytes)}" +
                           $"（{result.Files:N0} 个文件 / {result.Folders:N0} 个文件夹）";
                if (result.Skipped > 0) text += $" · {result.Skipped:N0} 项跳过";
                SetSizeText(text);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetSizeText($"大小：计算失败（{ex.Message}）");
            }
        });
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive);
}
