using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Library;

namespace MangaAgent.App.Views;

/// <summary>v4.16 文件夹属性对话框：与漫画属性一致——整块文本可选中复制（SelectableTextBlock）；
/// 大小后台递归计算：计算中显示"已算 X / N 个文件"，**完成后的最终文本不会被滞留的进度回调覆盖**；关闭即取消。</summary>
public static class FolderPropertiesDialog
{
    public static void Show(FolderNode folder)
    {
        string modified;
        try { modified = Directory.GetLastWriteTime(folder.Path).ToString("yyyy-MM-dd HH:mm"); }
        catch { modified = "-"; }
        string baseInfo = $"名称：{folder.Name}\n路径：{folder.Path}\n类型：文件夹\n" +
                          $"本层：{FolderEntryItemViewModel.CountDirectBooks(folder)} 本漫画 · " +
                          $"{FolderEntryItemViewModel.CountDirectSubfolders(folder)} 个子文件夹\n" +
                          $"目录下共 {FolderEntryItemViewModel.CountBooksRecursive(folder)} 本漫画\n" +
                          $"修改时间：{modified}";
        var info = new SelectableTextBlock
        {
            Text = baseInfo + "\n大小：计算中…",
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
                Children = { info, ok },
            },
        };
        ok.Click += (_, _) => window.Close();
        var cts = new CancellationTokenSource();
        window.Closed += (_, _) => cts.Cancel();

        bool done = false;   // 仅在 UI 线程读写（完成/进度回调都经 Dispatcher）
        // v4.17：进度回调本身就在 UI 线程（Progress<T> 捕获 UI 上下文）→ **直接赋值，不要二次 Dispatcher.Post**
        // （原实现 SetText 内再 Post 一次 → 文本赋值排到完成 Post 之后 → 覆盖最终文本；小文件夹计算快、高概率复现）
        var progress = new Progress<FolderStats.Result>(r =>
        {
            if (done || cts.IsCancellationRequested) return;
            try { info.Text = baseInfo + "\n" + $"大小：计算中…（已算 {FolderStats.FormatSize(r.TotalBytes)} / {r.Files:N0} 个文件）"; }
            catch { }
        });

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
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    done = true;
                    try { info.Text = baseInfo + "\n" + text; } catch { }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    done = true;
                    try { info.Text = baseInfo + $"\n大小：计算失败（{ex.Message}）"; } catch { }
                });
            }
        });
    }

    private static Window? GetOwner()
        => (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows.FirstOrDefault(w => w.IsActive);
}
