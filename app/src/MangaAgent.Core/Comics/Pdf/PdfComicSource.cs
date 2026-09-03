using System.Runtime.InteropServices;

namespace MangaAgent.Core.Comics.Pdf;

/// <summary>
/// PDF 漫画来源：PDFium（FPDF_*）按需光栅化。
/// 与 Zip/Folder 源的关键差异：PDF 页面是矢量/文档指令，无固有像素 —— 本源实现
/// ComicSource.RenderPageAsync(index, targetWidthPx) 按目标宽渲染（缩放任意倍数仍清晰），
/// GetPageSize 返回"canonical 密度(300dpi) 折算像素"以驱动既有的宽高比/清晰度档位逻辑；
/// OpenPageAsync 为通用兜底（渲染 800px 宽 → PNG 流），真实调用方（解码/缩略图）优先走 RenderPageAsync。
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

    private readonly string _path;
    private readonly object _gate = new();
    private IntPtr _doc;
    private int? _pageCount;
    private bool _disposedFlag;

    public PdfComicSource(string path) => _path = path;

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

    /// <summary>页尺寸（同步，UI 构造期/后台补全调用；LoadPage 为轻量解析，毫秒级）。</summary>
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
                    return CanonicalPixels(Fpdf.FPDF_GetPageWidthF(page), Fpdf.FPDF_GetPageHeightF(page));
                }
                finally { Fpdf.FPDF_ClosePage(page); }
            }
        }
        catch
        {
            return null;   // 坏页/缺 dll：TryReadSize catch 哲学一致
        }
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
                    int w = targetWidthPx;
                    int h = Math.Max(1, (int)Math.Round(ph * targetWidthPx / pw));
                    IntPtr bmp = Fpdf.FPDFBitmap_CreateEx(w, h, Fpdf.FPDFBitmap_BGRA, IntPtr.Zero, 0);
                    if (bmp == IntPtr.Zero)
                        throw new PdfException(PdfErrorKind.Other, $"PDF 渲染缓冲创建失败：{_path}");
                    try
                    {
                        Fpdf.FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, Fpdf.RenderFlags);
                        IntPtr buf = Fpdf.FPDFBitmap_GetBuffer(bmp);
                        if (buf == IntPtr.Zero)
                            throw new PdfException(PdfErrorKind.Other, $"PDF 渲染失败（无输出缓冲）：{_path}");
                        byte[] data = new byte[w * h * 4];
                        Marshal.Copy(buf, data, 0, data.Length);
                        return new RenderedPage(w, h, data);
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
            if (_doc != IntPtr.Zero)
            {
                Fpdf.FPDF_CloseDocument(_doc);
                _doc = IntPtr.Zero;
            }
        }
        return ValueTask.CompletedTask;
    }
}
