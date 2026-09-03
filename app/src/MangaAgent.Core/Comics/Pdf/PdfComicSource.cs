using System.Runtime.InteropServices;

namespace MangaAgent.Core.Comics.Pdf;

/// <summary>
/// PDF 漫画来源：PDFium（FPDF_*）按需光栅化。
/// 与 Zip/Folder 源的关键差异：PDF 页面是矢量/文档指令，无固有像素 —— 本源实现
/// ComicSource.RenderPageAsync(index, targetWidthPx) 按目标宽渲染（缩放任意倍数仍清晰），
/// GetPageSize 返回"canonical 密度(300dpi) 折算像素"以驱动既有的宽高比/清晰度档位逻辑；
/// OpenPageAsync 为通用兜底（渲染 800px 宽 → PNG 流），真实调用方（解码/缩略图）优先走 RenderPageAsync。
/// 背景：渲染前把位图整体填白（FPDFBitmap_FillRect）——PDFium 不自动填背景，页面"未绘制区"
/// （纸面留白）不填会输出初始化黑值（BUG-009）。
/// 裁切模式（CropToContent）：默认"纸面视图"（Chrome 样）；开启后按页面图像对象的边界
/// （"位图本身"）裁剪输出与尺寸，展示逻辑与图片书一致（单页满幅/双页图相接/滚动按内容比例）。
/// 线程安全：PDFium 文档级调用须串行 —— 所有 FPDF 访问经 _gate（读取/渲染在 Task.Run 线程池执行并持锁，
/// 绝不在持锁时 await，沿用 Zip 源 P2-1 纪律，避免 UI 同步取数与持锁任务续体互等死锁）。
/// </summary>
public sealed class PdfComicSource : ComicSource
{
    /// <summary>GetPageSize 折算密度：300dpi（≈"扫描原图"级）。宽高比与密度无关，仅影响 Auto 档渲染上限。</summary>
    private const double CanonicalDpi = 300.0;
    private const double PtPerInch = 72.0;

    /// <summary>OpenPageAsync 兜底渲染宽（通用图片解码方降采样需求已足够；真实调用方走 RenderPageAsync）。</summary>
    private const int FallbackRenderWidth = 800;

    /// <summary>页面图像对象的边界（pt，PDF 左下原点 y 上）。</summary>
    private readonly struct ContentBox
    {
        public ContentBox(float l, float b, float r, float t)
        {
            L = l; B = b; R = r; T = t; Valid = true;
        }

        public readonly float L, B, R, T;
        public readonly bool Valid;
        public float Width => R - L;
        public float Height => T - B;
    }

    private readonly string _path;
    private readonly object _gate = new();
    private IntPtr _doc;
    private int? _pageCount;
    private bool _disposedFlag;

    private bool _cropToContent;
    private readonly Dictionary<int, ContentBox> _contentBoxes = new();

    public PdfComicSource(string path) => _path = path;

    /// <summary>裁切模式：开启后页面按"位图本身"边界裁剪（尺寸与渲染输出同步），纸面留白被去掉。</summary>
    public bool CropToContent
    {
        get { lock (_gate) return _cropToContent; }
        set
        {
            lock (_gate)
            {
                if (_cropToContent == value) return;
                _cropToContent = value;
                _contentBoxes.Clear();   // 内容边界缓存按需重算（语义切换）
            }
        }
    }

    public override string Name => Path.GetFileNameWithoutExtension(_path);
    public override string SourcePath => _path;

    public override int PageCount
    {
        get
        {
            lock (_gate)
            {
                EnsureOpen();
                if (_pageCount is null) _pageCount = Fpdf.FPDF_GetPageCount(_doc);
                return _pageCount.Value;
            }
        }
    }

    private void EnsureOpen()
    {
        if (_disposedFlag) throw new ObjectDisposedException(nameof(PdfComicSource));
        if (_doc != IntPtr.Zero) return;
        PdfiumLibrary.EnsureAvailable();
        _doc = Fpdf.FPDF_LoadDocumentUtf8(_path, null);
        if (_doc == IntPtr.Zero)
        {
            int err = Fpdf.FPDF_GetLastError();
            if (err == Fpdf.FPDF_ERR_PASSWORD)
                throw new PdfException(PdfErrorKind.Encrypted, $"PDF 已加密，暂不支持密码输入：{_path}");
            throw new PdfException(PdfErrorKind.Corrupt, $"无法打开 PDF（损坏或非 PDF 文件）：{_path}", err);
        }
    }

    /// <summary>canonical 密度像素尺寸：pts × 300/72（四舍五入）。</summary>
    private static PageSize CanonicalPixels(double widthPt, double heightPt)
    {
        double s = CanonicalDpi / PtPerInch;
        return new PageSize((int)Math.Round(widthPt * s), (int)Math.Round(heightPt * s));
    }

    /// <summary>页尺寸（同步，UI 构造期/后台补全调用；LoadPage 为轻量解析，毫秒级）。
    /// 裁切模式返回内容区（位图本身）尺寸 —— 布局/双页/滚动全链路按此比例展示。</summary>
    public override PageSize? GetPageSize(int index)
    {
        try
        {
            lock (_gate)
            {
                EnsureOpen();
                IntPtr page = Fpdf.FPDF_LoadPage(_doc, index);
                if (page == IntPtr.Zero) return null;
                try
                {
                    float pw = Fpdf.FPDF_GetPageWidthF(page);
                    float ph = Fpdf.FPDF_GetPageHeightF(page);
                    if (_cropToContent && TryGetContentBox(page, index, out var cb))
                        return CanonicalPixels(cb.Width, cb.Height);
                    return CanonicalPixels(pw, ph);
                }
                finally { Fpdf.FPDF_ClosePage(page); }
            }
        }
        catch
        {
            return null;   // 坏页/缺 dll：TryReadSize catch 哲学一致
        }
    }

    /// <summary>取（或计算并缓存）页内容边界。</summary>
    private bool TryGetContentBox(IntPtr page, int index, out ContentBox box)
    {
        if (_contentBoxes.TryGetValue(index, out box)) return box.Valid;
        box = ComputeContentBox(page);
        _contentBoxes[index] = box;
        return box.Valid;
    }

    /// <summary>枚举页面对象，统计 IMAGE/FORM（图像载体）边界并集；无图像（纯矢量页）→ 无效（不裁）。
    /// 忽略微小对象（面积 &lt; 页 1%）；TEXT/PATH 不计 —— 裁切语义 = "显示位图本身"。</summary>
    private ContentBox ComputeContentBox(IntPtr page)
    {
        float pw = Fpdf.FPDF_GetPageWidthF(page);
        float ph = Fpdf.FPDF_GetPageHeightF(page);
        float minArea = pw * ph * 0.01f;
        float l = float.MaxValue, b = float.MaxValue, r = float.MinValue, t = float.MinValue;
        int n = Fpdf.FPDFPage_CountObjects(page);
        for (int i = 0; i < n; i++)
        {
            IntPtr obj = Fpdf.FPDFPage_GetObject(page, i);
            int type = Fpdf.FPDFPageObj_GetType(obj);
            if (type != Fpdf.FPDF_PAGEOBJ_IMAGE && type != Fpdf.FPDF_PAGEOBJ_FORM) continue;
            if (!Fpdf.FPDFPageObj_GetBounds(obj, out float ol, out float ob, out float or, out float ot)) continue;
            float w = or - ol, h = ot - ob;
            if (w <= 0 || h <= 0 || w * h < minArea) continue;
            l = Math.Min(l, ol); b = Math.Min(b, ob);
            r = Math.Max(r, or); t = Math.Max(t, ot);
        }
        return r > l && t > b ? new ContentBox(l, b, r, t) : default;
    }

    public override async Task<RenderedPage?> RenderPageAsync(int index, int targetWidthPx, CancellationToken ct = default)
    {
        if (targetWidthPx <= 0) targetWidthPx = FallbackRenderWidth;
        return await Task.Run(() =>
        {
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                EnsureOpen();
                IntPtr page = Fpdf.FPDF_LoadPage(_doc, index);
                if (page == IntPtr.Zero)
                    throw new PdfException(PdfErrorKind.Corrupt, $"PDF 第 {index + 1} 页无法加载：{_path}");
                try
                {
                    float pw = Fpdf.FPDF_GetPageWidthF(page);
                    float ph = Fpdf.FPDF_GetPageHeightF(page);
                    if (pw <= 0 || ph <= 0)
                        throw new PdfException(PdfErrorKind.Corrupt, $"PDF 第 {index + 1} 页尺寸无效：{_path}");
                    ContentBox cb = default;
                    bool crop = _cropToContent && TryGetContentBox(page, index, out cb);
                    // 渲染宽：裁切模式按内容占比放大，保证裁出后 ≈ targetWidth
                    int rawW = crop
                        ? Math.Max(targetWidthPx, (int)Math.Ceiling(targetWidthPx * pw / cb.Width))
                        : targetWidthPx;
                    int rawH = Math.Max(1, (int)Math.Round(ph * rawW / pw));
                    IntPtr bmp = Fpdf.FPDFBitmap_CreateEx(rawW, rawH, Fpdf.FPDFBitmap_BGRA, IntPtr.Zero, 0);
                    if (bmp == IntPtr.Zero)
                        throw new PdfException(PdfErrorKind.Other, $"PDF 渲染缓冲创建失败：{_path}");
                    try
                    {
                        // BUG-009：预填白底 —— 页面未绘制区（纸面留白）应为白而非初始化黑
                        Fpdf.FPDFBitmap_FillRect(bmp, 0, 0, rawW, rawH, 0xFFFFFFFF);
                        Fpdf.FPDF_RenderPageBitmap(bmp, page, 0, 0, rawW, rawH, 0, Fpdf.RenderFlags);
                        IntPtr buf = Fpdf.FPDFBitmap_GetBuffer(bmp);
                        if (buf == IntPtr.Zero)
                            throw new PdfException(PdfErrorKind.Other, $"PDF 渲染失败（无输出缓冲）：{_path}");
                        if (!crop)
                        {
                            byte[] fullData = new byte[rawW * rawH * 4];
                            Marshal.Copy(buf, fullData, 0, fullData.Length);
                            return new RenderedPage(rawW, rawH, fullData);
                        }
                        // 裁切：按比例抠出内容（位图本身）子区
                        int x0 = Math.Clamp((int)Math.Round(cb.L / pw * rawW), 0, rawW);
                        int x1 = Math.Clamp((int)Math.Round(cb.R / pw * rawW), 0, rawW);
                        int yTop = Math.Clamp((int)Math.Round(rawH - cb.T / ph * rawH), 0, rawH);   // 页面顶=0
                        int yBot = Math.Clamp((int)Math.Round(rawH - cb.B / ph * rawH), 0, rawH);
                        int cw = Math.Max(1, x1 - x0);
                        int ch = Math.Max(1, yBot - yTop);
                        byte[] data = new byte[cw * ch * 4];
                        int srcStride = rawW * 4;
                        for (int row = 0; row < ch; row++)
                        {
                            Marshal.Copy(IntPtr.Add(buf, (yTop + row) * srcStride + x0 * 4),
                                data, row * cw * 4, cw * 4);
                        }
                        return new RenderedPage(cw, ch, data);
                    }
                    finally { Fpdf.FPDFBitmap_Destroy(bmp); }
                }
                finally { Fpdf.FPDF_ClosePage(page); }
            }
        }, ct);
    }

    public override Task<Stream> OpenPageAsync(int index, CancellationToken ct = default)
    {
        // 兜底路径：渲染 FallbackRenderWidth → PNG 流（可被 Bitmap.DecodeToWidth 直接消费）。
        return Task.Run<Stream>(async () =>
        {
            var rp = await RenderPageAsync(index, FallbackRenderWidth, ct);
            var png = PngEncoder.EncodeBgra(rp!.Value.Width, rp.Value.Height, rp.Value.Bgra8888);
            return new MemoryStream(png);
        }, ct);
    }

    public override Task<ComicMetadata?> ReadMetadataAsync(CancellationToken ct = default)
    {
        return Task.Run<ComicMetadata?>(() =>
        {
            lock (_gate)
            {
                EnsureOpen();
                var title = Fpdf.GetMetaText(_doc, "Title");
                return new ComicMetadata { Title = string.IsNullOrWhiteSpace(title) ? null : title };
            }
        }, ct);
    }

    public override ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposedFlag) return ValueTask.CompletedTask;
            _disposedFlag = true;
            _contentBoxes.Clear();
            if (_doc != IntPtr.Zero)
            {
                Fpdf.FPDF_CloseDocument(_doc);
                _doc = IntPtr.Zero;
            }
        }
        return ValueTask.CompletedTask;
    }
}
