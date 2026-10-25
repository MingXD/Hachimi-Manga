using Avalonia.Threading;

namespace MangaAgent.App.Services;

/// <summary>v4.19（P3）：只监听"当前显示的目录"一层的文件系统变化。
/// 设计取舍：
/// - 不监听整个库根（子树大、内核缓冲易溢出、句柄压力大）——切目录即重挂，开销恒定；
/// - 事件去抖 <see cref="DebounceMs"/> 毫秒合并（下载/解压会产生大量事件），只回调一次；
/// - 监听过期/溢出（<see cref="FileSystemWatcher.Error"/>）也回调一次，由上层做浅刷新兜底；
/// - 任何失败都不抛出（记日志后停用），不允许影响主流程。</summary>
public sealed class FolderWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounce;
    private Action? _onChanged;
    private string? _path;
    private bool _disposed;

    /// <summary>去抖间隔（毫秒）。测试可设为 0 关掉定时器（直接 Post 回调）。</summary>
    public int DebounceMs { get; set; } = 800;

    public string? WatchingPath => _path;

    /// <summary>切换监听目标（相同路径不重复挂）。<paramref name="onChanged"/> 在 UI 线程执行。</summary>
    public void Watch(string? path, Action onChanged)
    {
        if (_disposed) return;
        _onChanged = onChanged;
        if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase)) return;
        Stop();
        _path = path;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        try
        {
            if (DebounceMs > 0)
            {
                _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
                _debounce.Tick += (_, _) => { _debounce?.Stop(); Invoke(); };
            }
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                             | NotifyFilters.Size | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Changed += OnFsEvent;
            _watcher.Renamed += OnFsRenamed;
            _watcher.Error += (_, _) => Post();   // 内部缓冲溢出：兜底整体刷新一次
        }
        catch (Exception ex)
        {
            global::MangaAgent.App.App.SLog($"WATCH FAIL {path}: {ex.Message}");
            Stop();
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e) => Post();

    private void OnFsRenamed(object sender, RenamedEventArgs e) => Post();

    /// <summary>把事件投递到 UI 线程；<b>投递时记下当时的监听目标</b>，执行时目标已切换则丢弃
    /// （切目录后旧目录的残留事件不能触发新目录的刷新）。</summary>
    private void Post()
    {
        if (_disposed) return;
        var at = _path;
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || !string.Equals(_path, at, StringComparison.OrdinalIgnoreCase)) return;
                if (_debounce is { } d) d.Start();
                else Invoke();
            });
        }
        catch { /* 关窗过程中 Dispatcher 已停：忽略 */ }
    }

    private void Invoke()
    {
        try { _onChanged?.Invoke(); }
        catch (Exception ex) { global::MangaAgent.App.App.SLog($"WATCH CB FAIL: {ex.Message}"); }
    }

    public void Stop()
    {
        try { _watcher?.Dispose(); } catch { }
        _watcher = null;
        try { _debounce?.Stop(); } catch { }
        _debounce = null;
        _path = null;
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _onChanged = null;
    }
}
