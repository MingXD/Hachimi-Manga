using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;

namespace MangaAgent.App;

public partial class App : Application
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MangaAgent", "logs");
    private static readonly object LogLock = new();

    /// <summary>写操作日志：按日期分文件（app-YYYY-MM-DD.log），保留最近 7 天（日志轮转）。
    /// 排查"操作类 bug"时提供完整轨迹（时间戳+线程+事件）。</summary>
    internal static void SLog(string msg)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {msg}{Environment.NewLine}");
                // 轮转：删除 7 天前的日志
                foreach (var oldFile in Directory.GetFiles(LogDir, "app-*.log"))
                {
                    try
                    {
                        if ((DateTime.Now - File.GetLastWriteTime(oldFile)).TotalDays > 7)
                            File.Delete(oldFile);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 全局异常记录（闪退排查）：UI 线程未处理异常 → 写 crash 日志
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            SLog("CRASH AppDomain: " + e.ExceptionObject);
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                SLog("CRASH Dispatcher: " + e.Exception);
                e.Handled = true; // 记录后继续（避免窗口直接关闭，便于观察）
            };
        }
        catch (Exception ex) { SLog("CRASH hook fail: " + ex.Message); }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            var openIdx = Array.IndexOf(args, "--open");
            if (openIdx >= 0 && openIdx + 1 < args.Length)
            {
                var path = args[openIdx + 1];
                bool isDir = Directory.Exists(path);
                if (File.Exists(path) || isDir)
                {
                    var lastWrite = isDir ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
                    var book = new MangaAgent.Core.Library.BookInfo(
                        path, Path.GetFileNameWithoutExtension(path.TrimEnd('\\')), null, null,
                        isDir ? MangaAgent.Core.Library.BookKind.Folder : MangaAgent.Core.Library.BookKind.Zip,
                        lastWrite, 0);
                    var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MangaAgent", "reading.db");
                    desktop.MainWindow = new Views.ReaderWindow
                    {
                        DataContext = new ViewModels.ReaderViewModel(book, dbPath),
                    };
                    desktop.MainWindow.Show();
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
            }

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
