using Avalonia.Media.Imaging;
using MangaAgent.Core.Comics;
using MangaAgent.Core.Library;

namespace MangaAgent.App.Services;

/// <summary>封面缩略图：解码第一页并降采样，弱引用缓存。</summary>
public static class ThumbnailService
{
    private static readonly Dictionary<string, WeakReference<Bitmap>> s_cache = new();

    public static async Task<Bitmap?> GetThumbnailAsync(BookInfo book)
    {
        if (s_cache.TryGetValue(book.Path, out var wr) && wr.TryGetTarget(out var cached))
            return cached;

        Bitmap? bmp = null;
        try
        {
            await using var src = ComicSourceFactory.Open(book.Path);
            if (src.PageCount == 0) return null;
            // v4-PDF：矢量源按宽光栅化直通；图片源走流解码
            var raster = await src.RenderPageAsync(0, 260);
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
                bmp = Bitmap.DecodeToWidth(ms, 260);
            }
            s_cache[book.Path] = new WeakReference<Bitmap>(bmp);
        }
        catch
        {
            return null;
        }
        return bmp;
    }

    /// <summary>v4.3：任意本地图片路径 → 缩略图（文件夹自定义封面用；同一弱引用缓存）。</summary>
    public static async Task<Bitmap?> GetImageThumbAsync(string imagePath, int width)
    {
        if (s_cache.TryGetValue(imagePath, out var wr) && wr.TryGetTarget(out var cached))
            return cached;
        Bitmap? bmp = null;
        try
        {
            if (!File.Exists(imagePath)) return null;
            bmp = await Task.Run(() => Bitmap.DecodeToWidth(File.OpenRead(imagePath), Math.Max(32, width)));
            if (bmp is not null) s_cache[imagePath] = new WeakReference<Bitmap>(bmp);
        }
        catch
        {
            return null;
        }
        return bmp;
    }

    /// <summary>移除缓存（封面更换后强制重新加载）。</summary>
    public static void Invalidate(string path) => s_cache.Remove(path);
}
