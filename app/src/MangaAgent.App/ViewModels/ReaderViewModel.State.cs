using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.ViewModels;

/// <summary>ReaderViewModel：构造/续读恢复/状态保存/释放（P1-2 拆分自 ReaderViewModel.cs，纯搬移，行为零变化）。</summary>
public partial class ReaderViewModel
{
    private readonly ReadingStateStore _state;

    public ReaderViewModel(BookInfo book, string stateDbPath)
    {
        Book = book;
        _state = new ReadingStateStore(stateDbPath);
        _source = ComicSourceFactory.Open(book.Path);
        var saved = _state.LoadState(book.Path);
        // v5.2：设置"续读"关 → 忽略保存状态（从第 1 页、用默认模式）
        if (!ResumePosition) saved = null;
        // v3 #4：恢复页尺寸同步加载 —— 原实现 LoadSizes 只同步读前 2 页，
        // 滚动模式续读 fit 用 0.7 占位比例 → 恢复页显示尺寸错误（"缩小但显示 100%"）
        LoadSizes(saved?.LogicalPage);
        RebuildLayout();
        // 无保存状态 → 用设置"默认阅读模式"；有 → 尊重保存的模式（BUG-A v1.9：状态存物理页）
        // v3 #2a 修复：完整映射三模式 —— 原实现把 Webtoon 降级为 Single，滚动模式续读丢失
        if (saved is null) _viewMode = DefaultMode;
        else _viewMode = saved.Value.Mode switch
        {
            ReadingMode.Double => ViewMode.Double,
            ReadingMode.Webtoon => ViewMode.Webtoon,
            _ => ViewMode.Single,
        };
        _currentPhysical = Math.Clamp(saved?.LogicalPage ?? 0, 0, Math.Max(0, _source.PageCount - 1));
        _currentIndex = _logical.Count > 0 ? LogicalIndexOfPhysical(_currentPhysical) : 0;
        _direction = saved?.Direction ?? ReadingDirection2.Rtl;
        LoadBookmarks();
        _ = LoadCurrentAsync();
    }

    /// <summary>打开书且无保存状态时的默认模式（设置注入，打开书前设置）。</summary>
    public static ViewMode DefaultMode { get; set; } = ViewMode.Single;

    /// <summary>是否恢复上次阅读位置（设置注入，打开书前设置）。</summary>
    public static bool ResumePosition { get; set; } = true;

    /// <summary>滚动模式布局/比例变化（LoadSizes 补全后等）→ 视图重建当前页 fit（v3 #4：补全后比例生效，修复续读页"缩小"）。</summary>
    public event Action? WebtoonLayoutChanged;

    private void LoadSizes(int? restorePhysical = null)
    {
        // P2-1（性能）：大书打开提速 —— 构造只同步读前 2 页尺寸（布局/封面立即可用），
        // 其余页由后台任务补全（每 64 页让出一次，防独占线程池线程）；补全完成后
        // RebuildLayout 应用真实比例（双页拼接/高度匹配依赖真实 AspectRatio，不能一直用 0.7）。
        // v3 #4：额外同步读"续读恢复页"尺寸 —— 滚动模式进入 fit 需要该页真实比例。
        // 线程安全：_sizes 为 ConcurrentDictionary（后台写、UI 读）。
        int total = _source.PageCount;
        int syncCount = Math.Min(2, total);
        for (int i = 0; i < syncCount; i++) TryReadSize(i);
        if (restorePhysical is { } rp && rp >= syncCount && rp < total) TryReadSize(rp);
        if (total <= syncCount) return;

        _ = Task.Run(async () =>
        {
            for (int i = syncCount; i < total; i++)
            {
                if (_disposed) return;
                TryReadSize(i);
                if ((i & 0x3F) == 0) await Task.Yield();
            }
            if (_disposed) return;
            // 必须回 UI 线程再 RebuildLayout：它触发 LoadCurrentAsync → Image1/Ratio1 等
            // PropertyChanged，ReaderWindow 的 code-behind 处理器会同步 Relayout()；
            // 若在后台线程触发 → 线程池线程操作 Avalonia 布局树 → 破坏布局（分页图不可见）。
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed) return;
                    try { RebuildLayout(); }   // 真实比例生效（双页配对/页高重算）
                    catch { /* 已关闭：忽略 */ }
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
            catch
            {
                // 非 Avalonia 环境（无 UI 线程）：直接应用（无布局可破坏）
                try { RebuildLayout(); } catch { }
            }
        });
    }

    private void TryReadSize(int index)
    {
        try
        {
            var sz = _source.GetPageSize(index);
            if (sz is { } s) _sizes[index] = s;
        }
        catch { /* 坏条目：忽略，RatioOf 回退 0.7 */ }
    }

    /// <summary>全部页尺寸是否已加载（测试/诊断用：等后台补全完成再断言比例敏感行为）。</summary>
    public bool SizesLoaded => _sizes.Count >= _source.PageCount;

    private void SaveState() =>
        // BUG-A v1.9：持久化物理页（跨模式稳定）——单页存物理页、双页存逻辑页首物理页
        // v3 #2a 修复：完整映射三模式（原实现 Webtoon 存成 Single → 续读丢滚动模式）
        _state.SaveState(Book.Path, CurrentPhysical,
            ViewMode switch
            {
                ViewMode.Double => ReadingMode.Double,
                ViewMode.Webtoon => ReadingMode.Webtoon,
                _ => ReadingMode.Single,
            }, Direction);

    private int _disposeGuard;
    private bool _disposed;
    public async ValueTask DisposeAsync()
    {
        // 修复（code-audit P0-1）：原守卫写反（_disposed=true 后立刻 if(_disposed) return）
        // → DisposeAsync 从不执行 → 每本书泄漏 ZIP 句柄/SQLite/位图/缩略图。
        // 现用 Interlocked 幂等：OnClosed fire-and-forget 与测试显式调用可并存。
        if (Interlocked.Exchange(ref _disposeGuard, 1) == 1) return;
        _disposed = true;
        SaveState();
        await _source.DisposeAsync();
        _state.Dispose();
        // 关闭时 UI 即将销毁：Dispose 全部位图（含 UI 引用中的 —— BUG-004 计数清零）
        foreach (var bmp in _pageCache.Values)
            bmp.Dispose();
        foreach (var bmp in _uiRefs.Keys)
        {
            if (!_pageCache.ContainsValue(bmp))
                bmp.Dispose();
        }
        _pageCache.Clear();
        _cacheOrder.Clear();
        _uiRefs.Clear();
        ClearThumbCache();
    }
}
