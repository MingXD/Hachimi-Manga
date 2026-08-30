using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using MangaAgent.App.Services;
using MangaAgent.App.ViewModels;
using MangaAgent.Core.Storage;

namespace MangaAgent.App.Views;

/// <summary>ReaderWindow partial —— Webtoon 领域（code-audit P1 分文件，纯搬移）。</summary>
public partial class ReaderWindow : Window
{
    private void EnterWebtoon()
    {
        if (_webtoonCanvas is null)
            _webtoonCanvas = new Canvas { Background = null, Name = "WebtoonCanvas" };
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != _webtoonCanvas)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(_webtoonCanvas);
        }
        if (_vm is null) return;
        double newW = Math.Max(100, Viewport.Bounds.Width);
        if (!_webtoonActive)
        {
            // 任何进入（含首次/再次/切回）：恢复默认视图 = 当前页适应屏幕 + 水平垂直居中 + scale=1
            // （scroll-mode-fix v2：根治"再次进入携带分页残留缩放/平移 → 放大+贴左"）
            _webtoonActive = true;
            _webtoonSeq++;   // 结构变化：作废可能在途的任务
            FitWebtoonToPage(_vm.CurrentFirstPhysical);
            return;
        }
        if (Math.Abs(newW - _webtoonBaseViewportW) > 20)
        {
            // 视口尺寸变化：保持页面显示尺寸不变（不改 _webtoonWidth/_scale），
            // 按新视口重建布局 + clamp + 刷新虚拟化；结构变化 → seq 作废在途任务
            _webtoonSeq++;
            _webtoonBaseViewportW = newW;
            BuildWebtoonOffsets();
            ClearWebtoonPlaceholders();
            PageLayer.Width = _webtoonWidth;
            PageLayer.Height = WebtoonTotalHeight;
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
            return;
        }
        // 无结构变化：幂等保持现状（不作废在途任务 —— 防黑屏，scroll-mode-fix A）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
    }

    private void FitWebtoonHeight()
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, (-_ty + vpH / 2) / Math.Max(0.001, _scale))),
            0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();
        _webtoonSeq++;                       // 结构变化：作废在途解码
        _webtoonWidth = Math.Max(50, vpH * Math.Max(0.05, _vm.RatioOfPage(curPhys)));   // 页高 = 视口高
        _webtoonBaseViewportW = vpW;
        BuildWebtoonOffsets();
        ClearWebtoonImages();                // 旧宽度图清除重建（防错位）
        ClearWebtoonPlaceholders();
        _scale = 1.0;
        _tx = 0;                             // 窄于视口由 ClampWebtoonScroll 水平居中
        _ty = -(PageOffset(curPhys));        // 当前页顶对齐视口顶（单页 FitHeight 语义）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();
        App.SLog($"WEBTOON FITHEIGHT page={curPhys} width={_webtoonWidth:F0} ty={_ty:F1}");
    }

    private void FitWebtoonWidth()
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, (-_ty + vpH / 2) / Math.Max(0.001, _scale))),
            0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();
        _webtoonSeq++;                       // 结构变化：作废在途解码
        _webtoonWidth = vpW;                 // 铺满视口宽
        _webtoonBaseViewportW = vpW;
        BuildWebtoonOffsets();
        ClearWebtoonImages();                // 旧宽度图清除重建（防错位）
        ClearWebtoonPlaceholders();
        _scale = 1.0;
        _tx = 0;                             // 铺满 → 无需水平居中
        _ty = -(PageOffset(curPhys));        // 当前页顶对齐视口顶（单页 FitWidth 语义）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();
        App.SLog($"WEBTOON FITWIDTH page={curPhys} width={_webtoonWidth:F0} ty={_ty:F1}");
    }

    private void FitWebtoonToPage(int physical)
    {
        if (_vm is null) return;
        physical = Math.Clamp(physical, 0, _vm.PhysicalPageCount - 1);
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double ratio = Math.Max(0.05, _vm.RatioOfPage(physical));
        // 当前页完全可见：宽 = min(视口宽, 页高×ratio)，高 = 宽/ratio ≤ 视口高
        _webtoonWidth = Math.Min(vpW, vpH * ratio);
        _webtoonBaseViewportW = vpW;
        BuildWebtoonOffsets();
        ClearWebtoonImages();          // 旧宽度图全部释放移除（防几何错位）
        ClearWebtoonPlaceholders();    // 旧尺寸占位清理，随 UpdateWebtoonPages 重建
        _scale = 1.0;
        double pageTop = PageOffset(physical);
        double pageH = _webtoonWidth / ratio;
        _tx = (vpW - _webtoonWidth) / 2;                 // 水平居中
        _ty = -(pageTop + pageH / 2 - vpH / 2);          // 垂直居中（首尾页由 clamp 兜底）
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();
    }

    private void ClearWebtoonImages()
    {
        if (_vm is not null && _webtoonCanvas is not null)
        {
            foreach (var ch in _webtoonCanvas.Children.OfType<Image>())
            {
                var bmp = ch.Source as Avalonia.Media.Imaging.Bitmap;
                ch.Source = null;
                _vm.ReleaseUiBitmap(bmp);
            }
            _webtoonCanvas.Children.Clear();
        }
        _webtoonImages.Clear();
        _webtoonLastFirst = -1;
        _webtoonLastLast = -1;
    }

    private void ExitWebtoon()
    {
        // 2A-2 修复：退出 Webtoon 必须停止平滑滚动 timer（防空转卡 UI）
        _scrolling = false;
        _scrollVelocity = 0;
        _scrollTimer?.Stop();
        App.SLog($"EXIT_WEBTOON children={_webtoonCanvas?.Children.Count} images={_webtoonImages.Count}");
        if (_vm is not null && _webtoonCanvas is not null)
        {
            // 先置空 Source 再释放引用：确保布局 pass 不再访问将被 Dispose 的位图（Cleanup 崩溃修复）
            foreach (var ch in _webtoonCanvas.Children.OfType<Image>())
            {
                var bmp = ch.Source as Avalonia.Media.Imaging.Bitmap;
                ch.Source = null;
                _vm.ReleaseUiBitmap(bmp);
            }
            _webtoonCanvas.Children.Clear();
        }
        _webtoonImages.Clear();
        ClearWebtoonPlaceholders();
        _webtoonActive = false;
        _webtoonSeq++;
        if (PageLayer.Children.Count == 0 || PageLayer.Children[0] != PagePanel)
        {
            PageLayer.Children.Clear();
            PageLayer.Children.Add(PagePanel);
        }
        PageLayer.Height = BaseH;
        PagePanel.Orientation = Orientation.Horizontal;
    }

    private void ClearWebtoonPlaceholders()
    {
        if (_webtoonCanvas is not null)
            foreach (var b in _webtoonPlaceholders.Values)
                _webtoonCanvas.Children.Remove(b);
        _webtoonPlaceholders.Clear();
    }

    private void EnsureWebtoonPlaceholder(int physical)
    {
        if (_webtoonPlaceholders.ContainsKey(physical) || _webtoonCanvas is null || _vm is null) return;
        double h = _webtoonWidth / Math.Max(0.05, _vm.RatioOfPage(physical));
        var border = new Border
        {
            Width = _webtoonWidth,
            Height = h,
            Background = new SolidColorBrush(Avalonia.Media.Color.Parse("#3A3A44")),
            Child = new TextBlock
            {
                Text = $"{physical + 1}",
                Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse("#CCCCCC")),
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(border, 0);
        Canvas.SetTop(border, PageOffset(physical));
        _webtoonCanvas.Children.Add(border);
        _webtoonPlaceholders[physical] = border;
    }

    private void RemoveWebtoonPlaceholder(int physical)
    {
        if (_webtoonPlaceholders.Remove(physical, out var b))
            _webtoonCanvas?.Children.Remove(b);
    }

    private double PageOffset(int physical)
    {
        if (physical <= 0) return 0;
        if (physical < _webtoonOffsets.Length) return _webtoonOffsets[physical];
        return _webtoonOffsets.Length > 0 ? _webtoonOffsets[^1] : 0;
    }

    private void BuildWebtoonOffsets()
    {
        if (_vm is null) return;
        int n = _vm.PhysicalPageCount;
        var offs = new double[n];
        double acc = 0;
        for (int i = 0; i < n; i++)
        {
            offs[i] = acc;
            acc += _webtoonWidth / Math.Max(0.05, _vm.RatioOfPage(i));
        }
        _webtoonOffsets = offs;
    }

    private int PhysicalAt(double baseY)
    {
        int lo = 0, hi = _webtoonOffsets.Length - 1;
        if (hi < 0) return 0;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_webtoonOffsets[mid] <= baseY) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    private void UpdateWebtoonPages()
    {
        if (_vm is null || _webtoonCanvas is null || _webtoonOffsets.Length == 0) return;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        double scale = Math.Max(0.001, _scale);
        double baseTop = -_ty / scale;
        double baseBottom = (-_ty + vpH) / scale;
        int n = _vm.PhysicalPageCount;
        int first = Math.Max(0, PhysicalAt(Math.Max(0, baseTop)) - WebtoonBufferPages);
        int last = Math.Min(n - 1, PhysicalAt(Math.Max(0, baseBottom)) + WebtoonBufferPages);
        _webtoonVisibleFirst = first;
        _webtoonVisibleLast = last;

        bool rangeChanged = first != _webtoonLastFirst || last != _webtoonLastLast;
        if (rangeChanged)
        {
            _webtoonLastFirst = first;
            _webtoonLastLast = last;
            // 先添加缺失（异步解码；旧页保留作回退显示）
            for (int i = first; i <= last; i++)
            {
                if (!_webtoonImages.ContainsKey(i)) _ = LoadWebtoonImageAsync(i);
            }
            // 占位块：可见范围内未解码的页立即显示灰底+页码（任何时刻不黑屏，scroll-mode-fix v2）
            for (int i = first; i <= last; i++)
                if (!_webtoonImages.ContainsKey(i)) EnsureWebtoonPlaceholder(i);
            // 移除已离开可见范围的占位 + 重置失败计数（翻远重来可重新重试）
            foreach (var k in _webtoonPlaceholders.Keys.ToList())
                if (k < first || k > last) RemoveWebtoonPlaceholder(k);
            foreach (var k in _webtoonFailCounts.Keys.ToList())
                if (k < first || k > last) _webtoonFailCounts.Remove(k);
            // 再移除"远离可见范围"的旧页（距离 > 缓冲×2 → 已无回退价值）
            foreach (var k in _webtoonImages.Keys.ToList())
            {
                if (k < first - WebtoonBufferPages * 2 || k > last + WebtoonBufferPages * 2)
                    RemoveWebtoonImage(k);
            }
        }
        // 放大清晰度（v3）：可见页缓存宽度不足显示需求 → 移除旧图并按新宽度重请求（防放大模糊）
        for (int i = first; i <= last; i++)
            if (_webtoonImages.ContainsKey(i)) TryRefreshWebtoonImage(i);
        // 当前可见页号 → 状态显示（v3.3：页未变不更新 PageText —— 防每帧按钮重评估/布局，提升滑动帧率）
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, baseTop)), 0, n - 1);
        if (curPhys != _lastWebtoonPageText)
        {
            _lastWebtoonPageText = curPhys;
            _vm.UpdateWebtoonPageText(curPhys);
            // v3 #17：滚动位置变化即同步阅读位置（续读/书签存当前可见页，节流写库）
            _vm.SyncWebtoonPosition(curPhys);
        }
        SyncPreviewStripToScroll();
    }

    private void TryRefreshWebtoonImage(int physical)
    {
        if (_vm is null) return;
        double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
        int want = (int)Math.Clamp(Math.Ceiling(_webtoonWidth * _scale * dpr), 400, Math.Max(1200, _maxDecodeWidth));
        if (_vm.HasCachedWidth(physical, want)) return;   // 已够清晰
        RemoveWebtoonImage(physical);            // 先移出画布再释放旧位图
        EnsureWebtoonPlaceholder(physical);      // 占位防黑
        _ = LoadWebtoonImageAsync(physical);     // 按当前 want 重解码
    }

    private void RemoveWebtoonImage(int physical)
    {
        if (_webtoonImages.Remove(physical, out var img))
        {
            // v3 顺序修复：先移出画布（脱离渲染树）再释放位图 —— 缓存淘汰可能已移除该页缓存条目，
            // 若先 Release 会直接 Dispose 仍被渲染树引用的位图 → 下一布局 Image.MeasureOverride 崩溃
            _webtoonCanvas?.Children.Remove(img);
            _vm?.ReleaseUiBitmap(img.Source as Avalonia.Media.Imaging.Bitmap);
        }
        RemoveWebtoonPlaceholder(physical);
        _webtoonFailCounts.Remove(physical);
    }

    private async Task LoadWebtoonImageAsync(int physical)
    {
        if (_vm is null || _webtoonCanvas is null) return;
        if (!_webtoonLoading.Add(physical)) return;   // 并发去重
        bool rescheduled = false;
        try
        {
            var mySeq = _webtoonSeq;
            // 解码限流（W-1 卡顿：同一时刻最多 2 个解码任务）
            await _webtoonDecodeGate.WaitAsync();
            try
            {
                // 按显示宽度解码（scroll-mode-fix v2）：fit 时 4299px 大图只解 ~540px → 提速 4-8 倍
                double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
                int want = (int)Math.Clamp(Math.Ceiling(_webtoonWidth * _scale * dpr), 400, Math.Max(1200, _maxDecodeWidth));
                var bmp = await _vm.GetPageBitmapAsync(physical, want);
                if (bmp is null)
                {
                    // v3 黑页根治：解码失败 → 不创建空 Image（否则登记后永不重请求 → 永久黑块），
                    // 保留占位块 + 有限重试（≤3 次，800ms 间隔；翻远重来重置计数）
                    if (mySeq == _webtoonSeq && !_windowClosed && _vm.ViewMode == ViewMode.Webtoon
                        && physical >= _webtoonVisibleFirst && physical <= _webtoonVisibleLast
                        && !_webtoonImages.ContainsKey(physical))
                    {
                        int fails = _webtoonFailCounts.TryGetValue(physical, out var f) ? f : 0;
                        if (fails < MaxDecodeRetries)
                        {
                            _webtoonFailCounts[physical] = fails + 1;
                            _ = RetryWebtoonImageAsync(physical);
                        }
                    }
                    return;
                }
                if (_windowClosed || _vm.ViewMode != ViewMode.Webtoon || _webtoonCanvas is null)
                {
                    _vm.ReleaseUiBitmap(bmp);
                    return;
                }
                if (mySeq != _webtoonSeq)
                {
                    // 作废补偿（scroll-mode-fix A）：解码期间结构已变化（seq 作废）→ 位图丢弃；
                    // 若该页仍应在可见范围，让出 loading 标记并重新请求 —— 防止"请求被丢 → 画布空 → 黑屏"。
                    _vm.ReleaseUiBitmap(bmp);
                    if (physical >= _webtoonVisibleFirst && physical <= _webtoonVisibleLast)
                    {
                        rescheduled = true;
                        _webtoonLoading.Remove(physical);
                        _ = LoadWebtoonImageAsync(physical);
                    }
                    return;
                }
                if (_webtoonImages.ContainsKey(physical)
                    || physical < _webtoonVisibleFirst || physical > _webtoonVisibleLast)
                {
                    _vm.ReleaseUiBitmap(bmp);
                    return;
                }
                // 等宽：Width = 基准宽，Height = 宽/ratio（前缀和定位）
                double h = _webtoonWidth / Math.Max(0.05, _vm.RatioOfPage(physical));
                var img = new Image
                {
                    Stretch = Stretch.Uniform,
                    Width = _webtoonWidth,
                    Height = h,
                };
                img.Source = bmp;
                Canvas.SetLeft(img, 0);
                Canvas.SetTop(img, PageOffset(physical));
                _webtoonCanvas.Children.Add(img);
                _webtoonImages[physical] = img;
                _webtoonFailCounts.Remove(physical);
                RemoveWebtoonPlaceholder(physical);   // 图到位，撤占位
            }
            finally { _webtoonDecodeGate.Release(); }
        }
        finally { if (!rescheduled) _webtoonLoading.Remove(physical); }
    }

    private async Task RetryWebtoonImageAsync(int physical)
    {
        try { await Task.Delay(800); } catch { return; }
        if (_windowClosed || _vm is null || _vm.ViewMode != ViewMode.Webtoon || _webtoonCanvas is null) return;
        if (physical < _webtoonVisibleFirst || physical > _webtoonVisibleLast) return;
        if (_webtoonImages.ContainsKey(physical)) return;
        _ = LoadWebtoonImageAsync(physical);
    }

    private void ScrollWebtoonToPhysical(int physical)
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        int target = Math.Clamp(physical, 0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();   // 跳页：取消残余惯性，直接定位
        _webtoonSeq++;          // 作废旧范围在途解码（作废补偿只补可见范围页）
        _ty = -(PageOffset(target) * _scale);
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        _vm.UpdateWebtoonPageText(target);
    }

    private void ScrollWebtoonByEvent(PointerWheelEventArgs e)
    {
        if (_vm is null) return;
        double dy = e.Delta.Y * (_invertScroll ? -1.0 : 1.0);
        if (dy == 0) { e.Handled = true; return; }
        long now = Environment.TickCount64;
        // 物理档切分：与上一事件间隔 > 100ms → 结束上一档并更新该设备的"一档 Delta"估计
        if (_wheelBurstAccum != 0 && now - _wheelBurstLastMs > 100)
        {
            double burst = Math.Abs(_wheelBurstAccum);
            if (burst > 5 && burst < 4000)   // 合理范围过滤异常
                _wheelNotchDelta = _wheelNotchDelta * 0.7 + burst * 0.3;
            _wheelBurstAccum = 0;
        }
        _wheelBurstLastMs = now;
        _wheelBurstAccum += dy;
        double notch = Math.Clamp(_wheelNotchDelta, 20, 480);
        // v = dy/一档Delta × 视口高 × ratio × 摩擦 → 滑行距离 = v/摩擦 = dy/一档Delta × 视口高 × ratio
        double v = dy / notch * Viewport.Bounds.Height * WheelNotchScreenRatio
                   * _scrollFriction * Math.Max(0.1, WebtoonSpeed);
        AddScrollVelocity(Math.Clamp(v, -MaxScrollVelocity, MaxScrollVelocity));
        if (_settings.DebugLog) App.SLog($"WHEEL dy={dy:F0} notch≈{notch:F0} v={v:F0} ty={_ty:F0}");   // 高频项：DebugLog 开才记
        e.Handled = true;
    }

    private void AddScrollVelocity(double v)
    {
        _scrollVelocity = Math.Clamp(_scrollVelocity + v, -MaxScrollVelocity, MaxScrollVelocity);
        StartSmoothScroll();
    }

    private void CancelSmoothScroll()
    {
        _scrolling = false;
        _scrollVelocity = 0;
        _scrollTimer?.Stop();
    }

    private void StartSmoothScroll()
    {
        if (_scrolling) return;
        _scrolling = true;
        _scrollWatch.Restart();
        if (_scrollTimer is null)
        {
            _scrollTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _scrollTimer.Tick += (_, _) => SmoothScrollTick();
        }
        _scrollTimer.Start();
    }

    private void SmoothScrollTick()
    {
        if (_vm is null || _vm.ViewMode != ViewMode.Webtoon || _windowClosed)
        {
            // 2A-1 修复：切出 Webtoon/窗口关闭 → 必须停止 timer，否则每 16ms 空转
            // ClampWebtoonScroll+ApplyTransform+UpdateWebtoonPages（含改 PageText→Relayout 连锁）→ UI 卡死
            CancelSmoothScroll();
            return;
        }
        // 本帧时间差：每帧重置（数学正确；clamp 防卡顿跳变）
        double dt = Math.Clamp(_scrollWatch.Elapsed.TotalSeconds, 0.001, 0.05);
        _scrollWatch.Restart();
        // 指数摩擦：v' = v·e^(-c·dt) → 缓慢滑行停住（c = 设置"停止快慢"）
        _scrollVelocity *= Math.Exp(-_scrollFriction * dt);
        if (Math.Abs(_scrollVelocity) < ScrollStopEps)
        {
            _scrollVelocity = 0;
            CancelSmoothScroll();
            return;
        }
        _ty += _scrollVelocity * dt;
        ClampWebtoonScroll();   // 边界：到边即停（内部清速度，不反弹）
        ApplyTransform();
        UpdateWebtoonPages();
        // 周期性滚动日志（F12 诊断，~2次/秒）—— 高频项：DebugLog 开才记
        long now = Environment.TickCount64;
        if (_settings.DebugLog && now - _lastTickLog > 500)
        {
            _lastTickLog = now;
            App.SLog($"SCROLL ty={_ty:F0} v={_scrollVelocity:F0}");
        }
    }

    private void ZoomWebtoon(double factor, Avalonia.Point mouse)
    {
        double old = Math.Max(0.001, _scale);
        double target = Math.Clamp(old * factor, 0.2, 8.0);
        double r = target / old;
        // 标准锚定：鼠标处内容不动（鼠标在内容上的局部位置随缩放保持）
        _tx = mouse.X - (mouse.X - _tx) * r;
        _ty = mouse.Y - (mouse.Y - _ty) * r;
        _scale = target;
        PageLayer.Width = _webtoonWidth;
        PageLayer.Height = WebtoonTotalHeight;
        ClampWebtoonScroll();
        ApplyTransform();
        UpdateWebtoonPages();
        UpdateZoomPercent();   // v3：缩放后百分比 = scale×100（fit=100%）
    }

    private void ClampWebtoonScroll()
    {
        double totalH = WebtoonTotalHeight * _scale;
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        if (totalH <= vpH) { _ty = 0; _scrollVelocity = 0; }
        else
        {
            double min = -(totalH - vpH);
            if (_ty < min) { _ty = min; _scrollVelocity = 0; }
            else if (_ty > 0) { _ty = 0; _scrollVelocity = 0; }
        }
        // 内容窄于视口 → 水平居中（看图器规则，与分页 ClampToViewportIfSmaller 一致）
        double dispW = _webtoonWidth * _scale;
        double vpW = Math.Max(1, Viewport.Bounds.Width);
        if (dispW <= vpW) _tx = (vpW - dispW) / 2;
    }

    private void ResetWebtoonView()
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        // 记录当前可见页（滚动位置中央）
        double baseCenter = (-_ty + Viewport.Bounds.Height / 2) / Math.Max(0.001, _scale);
        int curPhys = Math.Clamp(PhysicalAt(Math.Max(0, baseCenter)), 0, _vm.PhysicalPageCount - 1);
        CancelSmoothScroll();
        _webtoonSeq++;   // 宽度重算 = 结构变化：作废在途任务
        FitWebtoonToPage(curPhys);
        UpdateZoomPercent();
        App.SLog($"WEBTOON RESET page={curPhys} width={_webtoonWidth:F0} scale={_scale:F3} ty={_ty:F1}");
    }

    private void WebtoonJumpPage(int dir)
    {
        if (_vm is null || _webtoonOffsets.Length == 0) return;
        int top = Math.Clamp(PhysicalAt(Math.Max(0, -_ty / Math.Max(0.001, _scale))), 0, _vm.PhysicalPageCount - 1);
        int target = Math.Clamp(top + dir, 0, _vm.PhysicalPageCount - 1);
        _vm.JumpToPhysicalCommand.Execute(target);   // 同步当前页状态
        ScrollWebtoonToPhysical(target);             // 页顶对齐视口顶
    }

    private void ScrollWebtoonPages(int dir)
    {
        if (_vm is null) return;
        AddScrollVelocity(Math.Clamp(dir * Math.Max(1, Viewport.Bounds.Height) * 6.0, -MaxScrollVelocity, MaxScrollVelocity));
    }

}
