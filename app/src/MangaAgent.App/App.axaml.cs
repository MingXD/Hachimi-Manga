using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MangaAgent.App.ViewModels;
using MangaAgent.App.Views;

namespace MangaAgent.App;

public partial class App : Application
{
    private static readonly string StartLog = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MangaAgent", "app-start.log");

    internal static void SLog(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartLog)!);
            File.AppendAllText(StartLog, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
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
