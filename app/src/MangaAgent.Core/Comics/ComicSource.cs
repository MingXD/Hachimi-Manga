namespace MangaAgent.Core.Comics;

public enum ReadingDirection
{
    LeftToRight,
    RightToLeft,
}

/// <summary>漫画书元数据（ComicInfo.xml 子集）。</summary>
public sealed class ComicMetadata
{
    public string? Title { get; init; }
    public string? Series { get; init; }
    public string? Volume { get; init; }
    public string? Writer { get; init; }
    public int? PageCount { get; init; }
    /// <summary>ComicInfo &lt;Manga&gt; 字段：Yes 表示日漫右到左。</summary>
    public bool? MangaRightToLeft { get; init; }
}

/// <summary>一页的尺寸信息（用于跨页识别与缩略图决策）。</summary>
public readonly record struct PageSize(int Width, int Height)
{
    public double AspectRatio => Height == 0 ? 0 : (double)Width / Height;
}

/// <summary>按目标宽度光栅化的页面像素（BGRA8888，自顶向下，无预乘；alpha 恒 255 视为不透明）。
/// PDF 等矢量源经 RenderPageAsync 产出；图片源返回 null 走流解码路径。</summary>
public readonly record struct RenderedPage(int Width, int Height, byte[] Bgra8888)
{
    public int Stride => Width * 4;
}

/// <summary>
/// 所有漫画"来源"（文件夹 / CBZ / PDF）的统一抽象。
/// 随机访问、懒解码：阅读引擎不感知具体格式。
/// </summary>
public abstract class ComicSource : IAsyncDisposable
{
    public abstract string Name { get; }
    public abstract string SourcePath { get; }
    public abstract int PageCount { get; }

    /// <summary>打开指定页的原始字节流（调用方负责 Dispose）。</summary>
    public abstract Task<Stream> OpenPageAsync(int index, CancellationToken ct = default);

    /// <summary>读取页面尺寸（同步，用于 UI 线程初始化——避免阻塞异步续体死锁）。</summary>
    public abstract PageSize? GetPageSize(int index);

    /// <summary>异步读取页面尺寸（不完整解码，仅解析文件头）。</summary>
    public virtual Task<PageSize?> GetPageSizeAsync(int index, CancellationToken ct = default)
        => Task.FromResult(GetPageSize(index));

    /// <summary>读取元数据（无则返回 null）。</summary>
    public abstract Task<ComicMetadata?> ReadMetadataAsync(CancellationToken ct = default);

    /// <summary>
    /// 按目标像素宽光栅化页面（矢量源实现；图片源默认返回 null → 调用方走 OpenPageAsync + 解码流）。
    /// 返回 null 表示本源不支持宽度感知光栅化。
    /// </summary>
    public virtual Task<RenderedPage?> RenderPageAsync(int index, int targetWidthPx, CancellationToken ct = default)
        => Task.FromResult<RenderedPage?>(null);

    public abstract ValueTask DisposeAsync();
}

public static class ComicSourceFactory
{
    /// <summary>按路径自动选择实现：.cbz/.zip → Zip，.pdf → PDF，目录 → Folder。</summary>
    public static ComicSource Open(string path)
    {
        if (Directory.Exists(path)) return new FolderComicSource(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".cbz" or ".zip") return new ZipComicSource(path);
        if (ext == ".pdf") return new Pdf.PdfComicSource(path);
        throw new NotSupportedException($"不支持的漫画来源格式: {ext}");
    }
}
