using Avalonia;
using System;

namespace MangaAgent.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    // v3：移除 WithDeveloperTools() —— Avalonia 12 未装 Avalonia.Diagnostics 时 F12 弹
    // "Developer Tools Not Installed" 提示，与项目自己的 F12 调试面板冲突
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
