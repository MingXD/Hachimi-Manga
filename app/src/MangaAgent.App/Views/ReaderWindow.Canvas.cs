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

/// <summary>ReaderWindow partial —— Canvas 领域（code-audit P1 分文件，纯搬移）。</summary>
public partial class ReaderWindow : Window
{
    private async void RunZoomDiagnostics()
    {
        await Task.Delay(2000);
        try
        {
            var sb = new System.Text.StringBuilder();
            App.SLog("DIAG start");
            if (_vm != null)
            {
                // 复现用户场景：先切到双页（含翻页制造缓存压力），再反复切换模式（快速连续，竞态窗口最大）
                _vm.ToggleModeCommand.Execute(null);   // Single -> Double
                await Task.Delay(1500);
                for (int pg = 0; pg < 8; pg++) { _vm.NextPageCommand.Execute(null); await Task.Delay(300); }
                // 快速连续切换 3 轮（Double->Webtoon->Single->Double...）
                for (int round = 0; round < 3; round++)
                {
                    try
                    {
                        _vm.ToggleModeCommand.Execute(null);
                        await Task.Delay(250);
                        _vm.ToggleModeCommand.Execute(null);
                        await Task.Delay(250);
                        sb.AppendLine($"R{round}: vm={_vm.ViewMode} img1={(_vm.Image1 is null ? "null" : "ok")} img2={(_vm.Image2 is null ? "null" : "ok")}");
                        App.SLog($"DIAG R{round} -> {_vm.ViewMode}");
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"R{round}: EX " + ex);
                        App.SLog("DIAG R" + round + " EX: " + ex);
                    }
                }
                // 稳定在 Webtoon 等待渲染（大量页面解码 + 淘汰）
                _vm.ToggleModeCommand.Execute(null);
                await Task.Delay(3000);
                sb.AppendLine($"FINAL: vm={_vm.ViewMode} img1={(_vm.Image1 is null ? "null" : "ok")}");

                // 按钮响应测速（问题：点击无反应）：分页模式连点缩放/翻页，测处理耗时
                _vm.SetViewMode(ViewMode.Single);
                await Task.Delay(800);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int clicks = 0;
                for (int i = 0; i < 8; i++)
                {
                    OnZoomInClick(this, new RoutedEventArgs());
                    await Task.Delay(50);
                    clicks++;
                    if (sw.ElapsedMilliseconds > 8000) break;
                }
                sw.Stop();
                sb.AppendLine($"CLICK: {clicks} clicks in {sw.ElapsedMilliseconds}ms scale={CanvasScale:F3} (avg {(double)sw.ElapsedMilliseconds / Math.Max(1, clicks):F1}ms/click)");
                App.SLog($"DIAG clicks={clicks} scale={CanvasScale:F3}");
                // 复位测试
                OnResetZoomClick(this, new RoutedEventArgs());
                await Task.Delay(200);
                sb.AppendLine($"RESET: scale={CanvasScale:F3} tx={CanvasX:F1} ty={CanvasY:F1} pct={_vm.ZoomPercentText}");
            }
            File.WriteAllText("D:\\MangaAgent\\app\\diag-zoom.txt", sb.ToString());
        }
        catch (Exception ex) { try { File.WriteAllText("D:\\MangaAgent\\app\\diag-zoom.txt", "ERR: " + ex); } catch { } }
    }

    private double ComputeBaseW()
    {
        if (_vm is null) return 0.7 * BaseH;
        double r1 = Math.Max(0.05, _vm.Ratio1);
        return (_vm.IsDoublePageShown && _vm.Image2 is not null
            ? r1 + Math.Max(0.05, _vm.Ratio2)
            : r1) * BaseH;
    }

    private double FitScreenScale()
    {
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();
        return Math.Min(vpW / baseW, vpH / BaseH);
    }

    private void UpdateZoomPercent()
    {
        if (_vm is null) return;
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            _vm.ZoomPercent = _scale * 100.0;
            return;
        }
        double fitScreen = Math.Max(0.001, FitScreenScale());
        _vm.ZoomPercent = _scale / fitScreen * 100.0;
    }

    private double CurrentFitScale()
    {
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();
        return _vm?.FitMode switch
        {
            FitMode.FitHeight => vpH / BaseH,
            FitMode.FitScreen => Math.Min(vpW / baseW, vpH / BaseH),
            _ => vpW / baseW,
        };
    }

    private void ApplyTransform()
    {
        PageLayer.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _tx, _ty));
    }

    private void Relayout()
    {
        var rsw = System.Diagnostics.Stopwatch.StartNew();
        if (_vm is null || Viewport is null || PageImage1 is null) return;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();

        // 布局根因（BUG-003 残余）：StackPanel 在 Grid 中被视口压缩（Bounds 高=视口高），
        // 超高 Image 被垂直居中偏移 → 缩放后 y 方向漂移 -30×scale。
        // 修复：PageLayer/PagePanel 显式高度 = BaseH，Image 顶部对齐。
        // Webtoon 模式：画布高 = 总页数 × BaseH（虚拟化滚动），由 EnterWebtoon 维护，不在此重置。
        if (_vm.ViewMode != ViewMode.Webtoon)
        {
            PageLayer.Height = BaseH;
            PagePanel.Height = BaseH;
        }

        // 基准图片尺寸（Uniform 填满控件，控件比例 = 图片比例）
        PageImage1.Width = Math.Max(1, Math.Max(0.05, _vm.Ratio1) * BaseH);
        PageImage1.Height = BaseH;
        if (_vm.Image2 is not null)
        {
            PageImage2.Width = Math.Max(1, Math.Max(0.05, _vm.Ratio2) * BaseH);
            PageImage2.Height = BaseH;
        }

        if (_vm.FitMode != FitMode.Custom && _vm.ViewMode != ViewMode.Webtoon)
        {
            // Fit 模式（分页）：计算 scale + 居中 translate
            _scale = CurrentFitScale();
            _tx = (vpW - baseW * _scale) / 2;
            _ty = (vpH - BaseH * _scale) / 2;
            if (Math.Abs(_vm.Scale - 1.0) > 0.001) _vm.ResetScaleInternal();
        }
        // Webtoon 模式：scale 由 EnterWebtoon/ZoomWebtoon 管理（等宽 FitScreen 语义），Relayout 不重置
        // Custom 模式：scale/translate 由画布交互管理，Relayout 不重置
        ApplyTransform();
        if (_vm.ViewMode == ViewMode.Webtoon)
        {
            EnterWebtoon();
            if (WebtoonSpeedButton is not null) WebtoonSpeedButton.IsVisible = true;
            if (PairButton is not null) PairButton.IsVisible = false;      // 配对仅双页（BUG-E v1.9）
            if (DirectionButton is not null) DirectionButton.IsVisible = false;
        }
        else
        {
            ExitWebtoon();
            ArrangeDoublePageOrder();
            if (WebtoonSpeedButton is not null) WebtoonSpeedButton.IsVisible = false;
            // BUG-E v1.9：配对按钮仅双页模式显示（单页无配对概念）
            if (PairButton is not null) PairButton.IsVisible = _vm.ViewMode == ViewMode.Double;
            if (DirectionButton is not null) DirectionButton.IsVisible = true;
        }
        // 方向感知翻页按钮 ToolTip（v1.8）
        if (NavPrevButton is not null && NavNextButton is not null && _vm is not null)
        {
            bool rtl = _vm.Direction == ReadingDirection2.Rtl;
            ToolTip.SetTip(NavPrevButton, rtl ? "下一页 (←)" : "上一页 (←)");
            ToolTip.SetTip(NavNextButton, rtl ? "上一页 (→)" : "下一页 (→)");
        }
        UpdateZoomPercent();
        rsw.Stop();
        if (rsw.ElapsedMilliseconds > 15)
            App.SLog($"RELAYOUT SLOW {rsw.ElapsedMilliseconds}ms mode={_vm?.ViewMode}");
    }

    private void ZoomTo(double target, Avalonia.Point pos)
    {
        double old = _scale;
        if (_vm is not null && _vm.FitMode != FitMode.Custom)
            _vm.FitMode = FitMode.Custom;   // 进入自定义缩放（Relayout 会重算 scale，随后覆盖回目标值）
        _scale = Math.Clamp(target, 0.02, 8.0);
        double r = _scale / old;
        _tx = pos.X - (pos.X - _tx) * r;
        _ty = pos.Y - (pos.Y - _ty) * r;
        ApplyTransform();
        ClampToViewportIfSmaller();
        if (_vm is not null)
        {
            _vm.SyncScaleFromView(_scale / CurrentFitScale());
            // v3：按档位计算解码目标宽度（自动=放大到原图级才解大图，无 2048 钳制）
            double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
            _vm.DecodeTargetWidth = _vm.ComputeTargetWidth((int)(ComputeBaseW() * _scale * dpr), _vm.CurrentPhysical);
            // v3 防放大模糊：当前页缓存宽度不足 → 立即重解码（分页模式）
            if (_vm.CurrentPageNeedsRedecode(_vm.DecodeTargetWidth)) _vm.RefreshCurrentPage();
            // v3 缩放联动：放大后按新宽度重预取邻页（防"放大后翻页又糊"）
            _vm.SchedulePrefetchAfterZoom();
        }
        UpdateZoomPercent();
    }

    private void ClampToViewportIfSmaller()
    {
        double vpW = Math.Max(1, Viewport.Bounds.Width);
        double vpH = Math.Max(1, Viewport.Bounds.Height);
        double cw = ComputeBaseW() * _scale;
        double ch = BaseH * _scale;
        if (cw <= vpW) _tx = (vpW - cw) / 2;
        if (ch <= vpH) _ty = (vpH - ch) / 2;
        ApplyTransform();
    }

    public void ZoomRelative(double factor)
    {
        if (_vm?.ViewMode == ViewMode.Webtoon)
        {
            ZoomWebtoon(factor, new Avalonia.Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2));
        }
        else ZoomToFocus(factor, ContentScreenRect().Center);
    }

    public Avalonia.Point ContentToScreen(Avalonia.Point local)
        => new(_scale * local.X + _tx, _scale * local.Y + _ty);

    private Avalonia.Rect ContentScreenRect()
    {
        double cw = ComputeBaseW() * _scale;
        double ch = BaseH * _scale;
        return new Avalonia.Rect(_tx, _ty, cw, ch);
    }

    private Avalonia.Point EffectiveFocus(Avalonia.Point mouse)
    {
        var r = ContentScreenRect();
        return new Avalonia.Point(
            Math.Clamp(mouse.X, r.Left, Math.Max(r.Left, r.Right)),
            Math.Clamp(mouse.Y, r.Top, Math.Max(r.Top, r.Bottom)));
    }

    public void ZoomToFocus(double factor, Avalonia.Point mouse)
    {
        App.SLog($"ZOOM factor={factor:F3} mouse=({mouse.X:F1},{mouse.Y:F1}) scale={_scale:F3}->{_scale * factor:F3} tx={_tx:F1} ty={_ty:F1} mode={_vm?.ViewMode}");
        double old = _scale;
        if (_vm is not null && _vm.FitMode != FitMode.Custom)
            _vm.FitMode = FitMode.Custom;
        double target = Math.Clamp(_scale * factor, 0.02, 8.0);
        double r = target / old;
        var f = EffectiveFocus(mouse);
        _tx = f.X - (f.X - _tx) * r;
        _ty = f.Y - (f.Y - _ty) * r;
        _scale = target;
        ApplyTransform();
        if (_vm?.ViewMode == ViewMode.Webtoon)
        {
            // Webtoon 缩放：重算画布高 + 边界 clamp + 虚拟化刷新（页面高度随 scale 变化）
            PageLayer.Width = _webtoonWidth;
            PageLayer.Height = WebtoonTotalHeight;
            ClampWebtoonScroll();
            ApplyTransform();
            UpdateWebtoonPages();
        }
        // BUG-B v1.9：分页模式缩放后不强制居中 —— 保留拖拽/焦点位置（复位按钮可恢复居中）
        if (_vm is not null)
        {
            _vm.SyncScaleFromView(_scale / CurrentFitScale());
            double dpr = RenderScaling > 0 ? RenderScaling : 1.0;
            _vm.DecodeTargetWidth = _vm.ComputeTargetWidth((int)(ComputeBaseW() * _scale * dpr), _vm.CurrentPhysical);
            // v3 防放大模糊：当前页缓存宽度不足 → 立即重解码（分页模式）
            if (_vm.CurrentPageNeedsRedecode(_vm.DecodeTargetWidth)) _vm.RefreshCurrentPage();
            // v3 缩放联动
            _vm.SchedulePrefetchAfterZoom();
        }
        UpdateZoomPercent();
    }

    private void OnViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        Relayout();
        // 预览栏可见时尺寸变化 → 重新计算张数（全屏/拖窗切换后立即正确）
        if (PreviewStrip?.IsVisible == true) RefreshThumbStrip();
    }

    private void ResetPagedView()
    {
        if (_vm is null || Viewport is null) return;
        _vm.FitMode = FitMode.FitScreen;
        double vpW = Math.Max(100, Viewport.Bounds.Width);
        double vpH = Math.Max(100, Viewport.Bounds.Height);
        double baseW = ComputeBaseW();
        _scale = CurrentFitScale();
        _tx = (vpW - baseW * _scale) / 2;
        _ty = (vpH - BaseH * _scale) / 2;
        ApplyTransform();
        if (Math.Abs(_vm.Scale - 1.0) > 0.001) _vm.ResetScaleInternal();
        UpdateZoomPercent();
        App.SLog($"RESET paged scale={_scale:F3} tx={_tx:F1} ty={_ty:F1}");
    }

    private void ArrangeDoublePageOrder()
    {
        if (_vm is null || !_vm.IsDoublePageShown) return;
        bool rtl = _vm.Direction == ReadingDirection2.Rtl;
        // PagePanel 子顺序：[PageImage1(前), PageImage2(后)] LTR；RTL 反转
        int i1 = PagePanel.Children.IndexOf(PageImage1);
        int i2 = PagePanel.Children.IndexOf(PageImage2);
        if (i1 < 0 || i2 < 0) return;
        if (rtl && i1 < i2)
        {
            PagePanel.Children.Remove(PageImage1);
            PagePanel.Children.Insert(i2, PageImage1);
        }
        else if (!rtl && i1 > i2)
        {
            PagePanel.Children.Remove(PageImage2);
            PagePanel.Children.Insert(i1, PageImage2);
        }
    }

    private void OnViewportDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        if (IsOverPreviewStrip(e.Source)) return;   // 预览栏内双击不缩放
        switch (_doubleClickAction)
        {
            case "Reset":
                if (_vm.ViewMode == ViewMode.Webtoon) ResetWebtoonView();
                else ResetPagedView();
                break;
            case "None":
                break;
            default:   // Zoom（默认）：Fit ↔ 200%
                double fit = CurrentFitScale();
                double target = Math.Abs(_scale - fit) < 0.02 ? fit * 2.0 : fit;
                ZoomTo(target, e.GetPosition(Viewport));
                break;
        }
        e.Handled = true;
    }

}
