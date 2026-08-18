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
            using var page = await src.OpenPageAsync(0);
            using var ms = new MemoryStream();
            await page.CopyToAsync(ms);
            ms.Position = 0;
            bmp = Bitmap.DecodeToWidth(ms, 260);
            s_cache[book.Path] = new WeakReference<Bitmap>(bmp);
        }
        catch
        {
            return null;
        }
        return bmp;
    }
}
