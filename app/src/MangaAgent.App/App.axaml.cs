using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;

namespace MangaAgent.App;

public partial class App : Application
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HachimiManga", "logs");
    private static readonly object LogLock = new();

    /// <summary>写操作日志：按日期分文件（hachimi-YYYY-MM-DD.log），保留最近 7 天（日志轮转）。
    /// 排查"操作类 bug"时提供完整轨迹（时间戳+线程+事件）。</summary>
    /// <summary>调试面板：最近事件环形缓冲（实时显示，诊断用）。</summary>
    public static readonly System.Collections.Concurrent.ConcurrentQueue<(DateTime Time, string Msg)> RecentEvents = new();

    internal static void SLog(string msg)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(LogDir);
                var file = Path.Combine(LogDir, $"hachimi-{DateTime.Now:yyyy-MM-dd}.log");
                File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {msg}{Environment.NewLine}");
                // 轮转：删除 7 天前的日志
                foreach (var oldFile in Directory.GetFiles(LogDir, "hachimi-*.log"))
                {
                    try
                    {
                        if ((DateTime.Now - File.GetLastWriteTime(oldFile)).TotalDays > 7)
                            File.Delete(oldFile);
                    }
                    catch { }
                }
            }
            // 内存缓冲（最近 100 条，供调试面板实时显示）
            RecentEvents.Enqueue((DateTime.Now, msg));
            while (RecentEvents.Count > 100) RecentEvents.TryDequeue(out _);
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
                    // v4-PDF：文件型书类型按扩展名推断（--open xxx.pdf 不再被误当 CBZ）
                    var book = new MangaAgent.Core.Library.BookInfo(
                        path, Path.GetFileNameWithoutExtension(path.TrimEnd('\\')), null, null,
                        MangaAgent.Core.Library.BookInfo.KindForPath(path),
                        lastWrite, 0);
                    var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "HachimiManga", "reading.db");
                    // v4.1-PDF：--open 同样注入设置默认（裁切偏好等），与库内打开行为一致
                    var openSettings = global::MangaAgent.App.Services.SettingsStore.Load();
                    ViewModels.ReaderViewModel.PdfCropDefault = openSettings.PdfCropDefault;
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
