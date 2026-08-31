using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MangaAgent.App.ViewModels;

/// <summary>ReaderViewModel：页面解码/位图缓存/引用计数（P1-2 拆分自 ReaderViewModel.cs，纯搬移，行为零变化）。</summary>
public partial class ReaderViewModel
{
    // 页面位图 LRU 缓存（上限 CacheCap 页，淘汰时 Dispose，控制内存峰值 —— BUG-001 修复）
    /// <summary>缓存页数上限（设置可调，默认 10）。</summary>
    public int CacheCap { get; set; } = 10;
    private readonly Dictionary<int, Bitmap> _pageCache = new();
    private readonly LinkedList<int> _cacheOrder = new();
    // BUG-004：UI 引用计数 —— 位图同时被缓存与 UI Image 控件引用（缓存共享返回同一对象），
    // 缓存淘汰/重解码直接 Dispose 会让布局时 Image.MeasureOverride → Bitmap.get_Size() 抛
    // ObjectDisposedException（模式切换大量解码触发淘汰时闪退）。
    // 修复：被 UI 引用的位图延迟 Dispose，直到引用归零且不在缓存。
    private readonly Dictionary<Bitmap, int> _uiRefs = new();

    /// <summary>解码目标宽度（按显示尺寸降采样，4K 扫描页从 104MB 降到 ~10MB）。
    /// v3：由视图按 ComputeTargetWidth 计算（档位化，上限=原图宽/限幅/原图全尺寸）。</summary>
    [ObservableProperty]
    private int _decodeTargetWidth = 2048;

    /// <summary>v3：清晰度档位（设置注入）：Auto / Limit / Original。</summary>
    public string DecodeQuality { get; set; } = "Auto";
    /// <summary>v3：Limit 档上限（设置注入）。</summary>
    public int DecodeLimit { get; set; } = 4096;
    /// <summary>v3：预取范围 ±N 页（设置注入，默认 2）。</summary>
    public int PrefetchRange { get; set; } = 2;

    /// <summary>按档位计算某物理页的有效解码上限（v3）：
    /// Auto = min(原图宽, 4096)（放大到原图级才解大图）；Limit = DecodeLimit；Original = 原图宽；原图宽未知回退 4096。</summary>
    public int EffectiveDecodeLimit(int physical)
    {
        int origW = _sizes.TryGetValue(physical, out var sz) && sz.Width > 0 ? sz.Width : 0;
        return DecodeQuality switch
        {
            "Original" when origW > 0 => origW,
            "Original" => 4096,
            "Limit" => Math.Max(1200, DecodeLimit),
            _ => origW > 0 ? Math.Min(origW, 4096) : 4096,
        };
    }

    /// <summary>目标解码宽度：显示需求 clamp 到 [minWidth, 档位上限]（v3）。分页默认 1200；滚动小屏 fit 传 400。
    /// 边界：原图宽度小于 minWidth（小占位图）时 Math.Clamp(min&gt;max) 会抛异常——此时直接返回原图宽（解码不会超过原图）。</summary>
    public int ComputeTargetWidth(int displayWidth, int physical, int minWidth = 1200)
    {
        int limit = EffectiveDecodeLimit(physical);
        if (limit < minWidth) return Math.Min(displayWidth, limit);
        return Math.Clamp(displayWidth, minWidth, limit);
    }

    private int _loadSeq;
    private readonly Dictionary<int, int> _pageCacheWidth = new();

    // 预览栏缩略图：独立小尺寸解码缓存（~150px），与正文大图缓存分离，避免解码风暴
    private readonly Dictionary<int, Bitmap> _thumbCache = new();
    private const int ThumbWidth = 150;

    // ---------- 预解码（翻页延迟优化） ----------
    private int _prefetchSeq;   // 预解码防堆积（只保留最新请求）

    /// <summary>测试/诊断用：当前位图缓存页数（P2-3 压力断言缓存有界/关闭后归零）。</summary>
    public int CacheCount => _pageCache.Count;

    /// <summary>测试/诊断用：UI 引用中的位图数（P2-3 压力断言关闭后归零）。</summary>
    public int UiRefCount => _uiRefs.Count;

    private async Task LoadCurrentAsync()
    {
        // 滚动模式（Webtoon）：分页解码链完全跳过 —— 页面由视图的 UpdateWebtoonPages 虚拟化渲染，
        // PageText 由 UpdateWebtoonPageText 维护；此处解码的 Image1/Image2 不显示，
        // 反而触发 Relayout→EnterWebtoon→seq++ 作废在途 Webtoon 解码任务 → 黑屏（scroll-mode-fix A）。
        if (ViewMode == ViewMode.Webtoon) return;
        if (_logical.Count == 0) return;
        var mySeq = ++_loadSeq;
        var myLayout = _layoutVersion;
        // BUG-A v1.9：单页模式显示当前物理页；双页模式显示配对逻辑页
        var showDouble = ViewMode == ViewMode.Double;
        int firstPhys = ViewMode == ViewMode.Single ? _currentPhysical : LogicalFirstPhysical(CurrentIndex);
        int secondPhys = -1;
        if (showDouble)
        {
            var lp = _logical[CurrentIndex];
            showDouble = lp.IsDouble;
            secondPhys = lp.SecondIndex;
        }

        var img1 = await DecodeAsync(firstPhys);
        Bitmap? img2 = null;
        if (showDouble && secondPhys >= 0)
            img2 = await DecodeAsync(secondPhys);

        // 竞态防护：期间用户已翻页/切换模式/重布局，则丢弃本次结果；关闭后同样丢弃（防泄漏计数）
        if (mySeq != _loadSeq || myLayout != _layoutVersion || CurrentIndex >= _logical.Count || _disposed)
        {
            ReleaseUiBitmap(img1);
            ReleaseUiBitmap(img2);
            return;
        }

        HasStatus = false;
        IsDoublePageShown = showDouble;
        var old1 = Image1;
        var old2 = Image2;
        // 双页高度匹配（BUG）：Ratio 先于 Image 设置，Relayout 用正确的宽高比布局两图
        Ratio1 = RatioOf(firstPhys);
        Image1 = img1;
        if (img2 is not null) Ratio2 = RatioOf(secondPhys);
        Image2 = img2;
        ReleaseUiBitmap(old1);
        ReleaseUiBitmap(old2);
        App.SLog($"SHOW phys={firstPhys} w={(_pageCacheWidth.TryGetValue(firstPhys, out var sw) ? sw : 0)} mode={ViewMode}");
        // BUG-A v1.9：单页显示物理页号；双页显示逻辑页号
        PageText = ViewMode == ViewMode.Single
            ? $"{_currentPhysical + 1} / {_source.PageCount}"
            : $"{CurrentIndex + 1} / {TotalLogical}";
        // 翻页延迟优化（v2.3.15）：预解码相邻页入缓存，连续翻页即时显示
        _ = PrefetchNeighborsAsync();
    }

    /// <summary>预取相邻页：单页 ±PrefetchRange；双页 后 N 个逻辑页。_prefetchSeq 只保留最新一次请求（防堆积）。
    /// v3：范围可设置（默认 ±2）、并发限流 2。</summary>
    private async Task PrefetchNeighborsAsync()
    {
        if (_logical.Count == 0 || _disposed) return;
        int mySeq = ++_prefetchSeq;
        var targets = new List<int>();
        if (ViewMode == ViewMode.Single)
        {
            for (int d = 1; d <= PrefetchRange; d++)
            {
                if (CurrentPhysical - d >= 0) targets.Add(CurrentPhysical - d);
                if (CurrentPhysical + d < _source.PageCount) targets.Add(CurrentPhysical + d);
            }
        }
        else
        {
            for (int d = 1; d <= PrefetchRange; d++)
            {
                int idx = CurrentIndex + d;
                if (idx < _logical.Count)
                {
                    targets.Add(_logical[idx].FirstIndex);
                    if (_logical[idx].IsDouble) targets.Add(_logical[idx].SecondIndex);
                }
            }
        }
        foreach (var p in targets)
        {
            if (mySeq != _prefetchSeq || _disposed) return;
            await PrefetchDecodeAsync(p);
        }
    }

    /// <summary>v3 缩放联动：缩放/适应模式后按新宽度重预取邻页（延迟 300ms 防滚轮连发风暴）。
    /// 现状短板：缩放只重解当前页，相邻页缓存旧宽度 → 放大后翻页又糊又卡。</summary>
    private int _prefetchAfterZoomSeq;
    public void SchedulePrefetchAfterZoom()
    {
        if (ViewMode == ViewMode.Webtoon) return;   // 滚动模式由视图虚拟化按需解码
        int mySeq = ++_prefetchAfterZoomSeq;
        _ = Task.Delay(300).ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (mySeq == _prefetchAfterZoomSeq && !_disposed)
                _ = PrefetchNeighborsAsync();
        }));
    }

    /// <summary>预解码单页入缓存但不 Acquire（缓存持有；UI 取用时 LoadCurrentAsync 再 Acquire）。
    /// v3：并发限流 SemaphoreSlim(2)，防连翻/大范围预取解码风暴。</summary>
    private readonly System.Threading.SemaphoreSlim _prefetchGate = new(2, 2);

    private async Task PrefetchDecodeAsync(int physical)
    {
        if (_disposed) return;   // P2-3：关闭后不再解码/入缓存
        // v3：预取宽度 = 实时显示需求（不再用上次缩放的 DecodeTargetWidth）
        int want = ComputeTargetWidth(DisplayWidth, physical);
        if (_pageCache.TryGetValue(physical, out var c) &&
            _pageCacheWidth.TryGetValue(physical, out var w) && w >= want) return;
        await _prefetchGate.WaitAsync();
        try
        {
            using var stream = await _source.OpenPageAsync(physical);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            var bmp = await Task.Run(() => Bitmap.DecodeToWidth(ms, want));
            if (bmp is null) return;
            if (_disposed) { TryDispose(bmp); return; }   // P2-3：关闭后不再入缓存（防清除后再污染）
            if (_pageCache.TryGetValue(physical, out var old) && !_uiRefs.ContainsKey(old))
            {
                TryDispose(old);
                _pageCache.Remove(physical);
                _pageCacheWidth.Remove(physical);
                _cacheOrder.Remove(physical);
            }
            _pageCache[physical] = bmp;
            _pageCacheWidth[physical] = want;
            _cacheOrder.Remove(physical);
            _cacheOrder.AddLast(physical);
            EvictCache();
        }
        catch { }
        finally { _prefetchGate.Release(); }
    }

    private double RatioOf(int physical) => _sizes.TryGetValue(physical, out var sz) ? sz.AspectRatio : 0.7;

    /// <summary>Webtoon：获取指定物理页位图（带缓存，默认分页解码宽度）。</summary>
    public Task<Bitmap?> GetPageBitmapAsync(int physical) => DecodeAsync(physical, 0);

    /// <summary>Webtoon：按指定宽度获取位图（scroll-mode-fix v2：滚动模式按显示宽度解码，大图提速数倍）。</summary>
    public Task<Bitmap?> GetPageBitmapAsync(int physical, int wantWidth) => DecodeAsync(physical, wantWidth);

    /// <summary>缓存位图宽度是否满足需求（v3 放大清晰度检查用）。</summary>
    public bool HasCachedWidth(int physical, int wantWidth)
        => _pageCache.TryGetValue(physical, out _)
           && _pageCacheWidth.TryGetValue(physical, out var w) && w >= wantWidth;

    /// <summary>测试用：指定页当前缓存解码宽度（0=未缓存）。</summary>
    public int CachedDecodeWidth(int physical)
        => _pageCacheWidth.TryGetValue(physical, out var w) ? w : 0;

    /// <summary>分页当前页缓存宽度是否不足需求（v3 放大后立即重解码防模糊；0.85 迟滞防缩放风暴）。</summary>
    public bool CurrentPageNeedsRedecode(int wantWidth)
    {
        int p = CurrentPhysical;
        if (!_pageCache.TryGetValue(p, out _)) return false;
        return _pageCacheWidth.TryGetValue(p, out var w) && w < wantWidth * 0.85;
    }

    /// <summary>分页模式：按当前 DecodeTargetWidth 重载当前页（放大清晰度；滚动模式由画布刷新负责）。</summary>
    public void RefreshCurrentPage()
    {
        if (ViewMode != ViewMode.Webtoon) _ = LoadCurrentAsync();
    }

    /// <summary>预览栏缩略图（小尺寸，独立缓存，线程安全队列解码）。</summary>
    public async Task<Bitmap?> GetThumbnailAsync(int physical)
    {
        if (_disposed) return null;   // P2-3：关闭后不再解码/入缓存
        if (_thumbCache.TryGetValue(physical, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            using var stream = await _source.OpenPageAsync(physical);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            bmp = await Task.Run(() => Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, ThumbWidth));
            if (bmp is not null)
            {
                if (_disposed) bmp.Dispose();   // P2-3：关闭后不入缩略图缓存
                else _thumbCache[physical] = bmp;
            }
        }
        catch { }
        return bmp;
    }

    /// <summary>清空缩略图缓存（书关闭时）。</summary>
    private void ClearThumbCache()
    {
        foreach (var b in _thumbCache.Values) b.Dispose();
        _thumbCache.Clear();
    }

    /// <summary>Webtoon：页数。</summary>
    public int PhysicalPageCount => _source.PageCount;

    /// <summary>Webtoon：指定页宽高比。</summary>
    public double RatioOfPage(int physical) => RatioOf(physical);

    /// <summary>Webtoon：当前逻辑页对应的首个物理页索引（进入 Webtoon 时定位用）。</summary>
    public int CurrentFirstPhysical
    {
        get
        {
            if (_logical.Count == 0) return 0;
            if (ViewMode == ViewMode.Single) return _currentPhysical;
            var lp = _logical[Math.Clamp(CurrentIndex, 0, _logical.Count - 1)];
            return Math.Max(0, lp.FirstIndex);
        }
    }

    /// <summary>Webtoon：滚动时更新状态显示（只改 PageText，不动 CurrentIndex —— 避免每滚动一页触发重解码）。</summary>
    public void UpdateWebtoonPageText(int physical)
    {
        PageText = $"{Math.Clamp(physical + 1, 1, _source.PageCount)} / {_source.PageCount}";
    }

    /// <summary>v3：当前显示内容宽 × DPI（视图实时维护 = 解码需求输入；翻页/缩放/窗口变化都更新，
    /// 根治"翻页用旧解码宽度 → 放大/高分屏下模糊且不升级"）。</summary>
    public int DisplayWidth { get; set; } = 1200;

    private async Task<Bitmap?> DecodeAsync(int physical, int requestedWidth = 0)
    {
        if (_disposed) return null;   // P2-3：关闭后不再解码/入缓存
        // v3 修复：分页路径不再用"上次缩放的 DecodeTargetWidth"，而是用实时 DisplayWidth 计算目标宽度
        // （翻页/窗口变化后立刻按当前显示需求解码；Webtoon 走 requestedWidth 显式宽度）
        int want = requestedWidth > 0 ? Math.Max(400, requestedWidth) : ComputeTargetWidth(DisplayWidth, physical);
        int origW = _sizes.TryGetValue(physical, out var sz0) ? sz0.Width : 0;
        App.SLog($"DECODE p={physical} want={want} displayW={DisplayWidth} origW={origW}");
        if (_pageCache.TryGetValue(physical, out var cached))
        {
            // 按需高清：放大超过当前解码宽度时重新解码更大（C 阶段）
            if (_pageCacheWidth.TryGetValue(physical, out var w) && w >= want)
            {
                _cacheOrder.Remove(physical);
                _cacheOrder.AddLast(physical);
                AcquireUiBitmap(cached);   // 返回前 Acquire：位图从缓存取出即受 UI 保护（BUG-004）
                return cached;
            }
            // v3（防放大模糊）：不再因"仍被 UI 引用"拒绝重解码 —— 调用方负责原位替换 Source 后
            // 再 Release 旧引用（无崩溃窗口）。TryDispose 本就跳过 UI 引用，旧图在替换前不会 Dispose。
            TryDispose(cached);
            _pageCache.Remove(physical);
            _pageCacheWidth.Remove(physical);
            _cacheOrder.Remove(physical);
        }

        Bitmap? decoded = null;
        string? error = null;
        try
        {
            using var stream = await _source.OpenPageAsync(physical);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            // 2E 修复：JPEG 解码 + 降采样是同步 CPU 密集操作，移到线程池执行，
            // 避免在 UI 线程解码大图（4299x6071 → 数百 ms）导致翻页/滚动卡死
            decoded = await Task.Run(() => Bitmap.DecodeToWidth(ms, want));
            // v3 诊断：实际解码位图宽 vs 请求宽 vs 解析的原图宽 —— 区分"原图分辨率低(物理限制)"
            // 与"ParseHeader 解析错误导致 Auto 档主动降采样(程序 bug)"
            if (decoded is not null)
                App.SLog($"DECODE p={physical} want={want} actualW={decoded.PixelSize.Width} origW={origW} displayW={DisplayWidth}");
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (decoded is null)
        {
            StatusText = $"无法解码第 {physical + 1} 页：{error}";
            HasStatus = true;
            return null;
        }
        if (_disposed) { TryDispose(decoded); return null; }   // P2-3：关闭后丢弃新解码（防清除后再污染）

        HasStatus = false;
        _pageCache[physical] = decoded;
        _pageCacheWidth[physical] = want;   // v3 修复：DecodeAsync 也须记录解码宽度
                                            // （原缺失 → 缓存宽度检查永远失败 → 放大清晰度/重解码逻辑失效）
        AcquireUiBitmap(decoded);           // v3 顺序：先 Acquire 再入队淘汰 —— 防"刚解码未引用即被淘汰 Dispose"
        _cacheOrder.AddLast(physical);
        EvictCache();
        return decoded;
    }

    private void EvictCache()
    {
        // v3 修复：仍被 UI 引用的条目（滚动模式显示中的页）不淘汰 —— 否则条目消失 → HasCachedWidth 变 false
        // → 放大刷新循环反复重解码（风暴）→ 配合"先淘汰后 Acquire"会 Dispose 刚解码未引用的位图 → 渲染崩溃。
        int rotated = 0;
        while (_cacheOrder.Count > CacheCap)
        {
            var oldest = _cacheOrder.First!.Value;
            _cacheOrder.RemoveFirst();
            if (_pageCache.TryGetValue(oldest, out var bmp) && _uiRefs.ContainsKey(bmp))
            {
                _cacheOrder.AddLast(oldest);        // 显示中：保留并移到队尾，继续找可淘汰项
                if (++rotated >= _cacheOrder.Count) break;   // 整轮都是显示中 → 无法淘汰，停止
                continue;
            }
            rotated = 0;
            if (_pageCache.Remove(oldest, out bmp))
            {
                _pageCacheWidth.Remove(oldest);     // 同步清理宽度记录（原缺失 → 脏数据）
                TryDispose(bmp);
            }
        }
    }

    /// <summary>UI 取得位图引用（Image.Source 赋值前调用；缓存共享对象，计数防止被淘汰 Dispose）。</summary>
    public void AcquireUiBitmap(Bitmap? bmp)
    {
        if (bmp is null) return;
        _uiRefs.TryGetValue(bmp, out var n);
        _uiRefs[bmp] = n + 1;
    }

    /// <summary>UI 释放位图引用（Image.Source 替换/移除时调用）。引用归零且不在缓存 → Dispose。</summary>
    public void ReleaseUiBitmap(Bitmap? bmp)
    {
        if (bmp is null) return;
        if (!_uiRefs.TryGetValue(bmp, out var n)) return;
        if (n <= 1)
        {
            _uiRefs.Remove(bmp);
            TryDispose(bmp);
        }
        else _uiRefs[bmp] = n - 1;
    }

    /// <summary>安全释放：仍被 UI 引用或仍在缓存 → 不 Dispose（缓存淘汰/重解码用）。</summary>
    private void TryDispose(Bitmap bmp)
    {
        if (_uiRefs.ContainsKey(bmp)) return;
        if (_pageCache.ContainsValue(bmp)) return;
        bmp.Dispose();
    }
}
