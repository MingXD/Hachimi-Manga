using Avalonia.Media.Imaging;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;

namespace MangaAgent.App.Services;

/// <summary>封面缩略图：按"显示框需求"解码并降采样，弱引用缓存（键 = 路径 + 解码宽度）。</summary>
public static class ThumbnailService
{
    private static readonly Dictionary<string, WeakReference<Bitmap>> s_cache = new();

    /// <summary>v4.4：封面解码宽度 = UniformToFill 实际需要的源像素宽 × DPI 余量。
    /// 显示框 W×H、图片宽高比 ratio 时，填满框所需显示宽 = max(W, H×ratio)；
    /// 再乘 DPI 缩放与 1.25 余量抗插值模糊（钳 160–1200 防内存失控）。
    /// 横向封面（ratio>1）自动获得远大于框宽的像素 —— 修复"横向封面糊"（此前固定 260）。</summary>
    public static int CoverDecodeWidth(int coverW, int coverH, double ratio, double dpi)
    {
        if (ratio <= 0.01) ratio = 0.7;
        double fillW = Math.Max(coverW, coverH * ratio);
        double scale = (dpi > 0.1 ? dpi : 1.0) * 1.25;
        return (int)Math.Clamp(Math.Round(fillW * scale), 160, 1200);
    }

    public static async Task<Bitmap?> GetThumbnailAsync(BookInfo book, int coverW, int coverH, double dpi)
        => await GetCoverCoreAsync(book, coverW, coverH, dpi);

    /// <summary>v4.3：任意本地图片路径 → 封面缩略图（文件夹自定义封面；与漫画封面同款按比例解码）。</summary>
    public static async Task<Bitmap?> GetImageCoverThumbAsync(string imagePath, int coverW, int coverH, double dpi)
    {
        if (!File.Exists(imagePath)) return null;
        double ratio = 0.7;
        try
        {
            using (var fs = File.OpenRead(imagePath))
            {
                var head = new byte[65536];
                int read = fs.Read(head, 0, head.Length);
                var sz = ImageInfo.ParseHeader(head.AsSpan(0, read));
                if (sz is { } s && s.Width > 0 && s.Height > 0) ratio = s.AspectRatio;
            }
        }
        catch { }
        int decodeW = CoverDecodeWidth(coverW, coverH, ratio, dpi);
        string key = imagePath + "|" + decodeW;
        if (s_cache.TryGetValue(key, out var wr) && wr.TryGetTarget(out var cached))
            return cached;
        Bitmap? bmp = null;
        try
        {
            bmp = await Task.Run(() => Bitmap.DecodeToWidth(File.OpenRead(imagePath), decodeW));
            if (bmp is not null) s_cache[key] = new WeakReference<Bitmap>(bmp);
        }
        catch
        {
            return null;
        }
        return bmp;
    }

    private static async Task<Bitmap?> GetCoverCoreAsync(BookInfo book, int coverW, int coverH, double dpi)
    {
        Bitmap? bmp = null;
        try
        {
            await using var src = ComicSourceFactory.Open(book.Path);
            if (src.PageCount == 0) return null;
            // 取首页比例（图片头 / PDF canonical；失败回退 0.7）
            double ratio = src.GetPageSize(0) is { } sz && sz.Width > 0 && sz.Height > 0
                ? sz.AspectRatio
                : 0.7;
            int decodeW = CoverDecodeWidth(coverW, coverH, ratio, dpi);
            string key = book.Path + "|" + decodeW;
            if (s_cache.TryGetValue(key, out var wr) && wr.TryGetTarget(out var cached))
                return cached;
            // v4-PDF：矢量源按宽光栅化直通；图片源走流解码
            var raster = await src.RenderPageAsync(0, decodeW);
            if (raster is { } rp)
            {
                bmp = Imaging.BgraBitmap.Create(rp.Width, rp.Height, rp.Bgra8888);
            }
            else
            {
                using var page = await src.OpenPageAsync(0);
                using var ms = new MemoryStream();
                await page.CopyToAsync(ms);
                ms.Position = 0;
                bmp = Bitmap.DecodeToWidth(ms, decodeW);
            }
            s_cache[key] = new WeakReference<Bitmap>(bmp);
        }
        catch
        {
            return null;
        }
        return bmp;
    }

    /// <summary>移除缓存（封面更换/自定义封面更新后强制重新加载）。</summary>
    public static void Invalidate(string path)
    {
        foreach (var key in s_cache.Keys
                     .Where(k => k == path || k.StartsWith(path + "|", StringComparison.Ordinal))
                     .ToList())
            s_cache.Remove(key);
    }
}
