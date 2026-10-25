using Xunit;

// Avalonia Headless 需要串行执行（多测试类并发操作同一 headless 平台会死锁）
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MangaAgent.Tests;

/// <summary>v4.19：测试进程启动时把数据根切到独立临时目录。
/// 原因：<c>MainWindowViewModel</c> 构造时会异步加载 libraries.txt（本机真实资料库，约 6.7 万个文件）；
/// v4.19 起加载大幅变快（不再逐个打开压缩包），会在测试中途完成并覆盖测试自己的树/条目 → 用例互相污染。
/// 隔离后测试永不触碰用户真实库。</summary>
internal static class TestEnvironment
{
    internal static readonly string AppDataDir = Path.Combine(
        Path.GetTempPath(), "manga_test_appdata_" + Guid.NewGuid().ToString("N"));

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(AppDataDir);
        MangaAgent.App.ViewModels.MainWindowViewModel.AppData = AppDataDir;
    }
}
