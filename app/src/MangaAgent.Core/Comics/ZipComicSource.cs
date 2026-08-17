using System.IO.Compression;
using System.Xml.Linq;

namespace MangaAgent.Core.Comics;

/// <summary>CBZ/ZIP 漫画来源：ZipArchive 随机读取。</summary>
public sealed class ZipComicSource : ComicSource
{
    private readonly string _path;
    private ZipArchive? _archive;
    private List<ZipArchiveEntry>? _entries;

    public ZipComicSource(string path) => _path = path;

    public override string Name => Path.GetFileNameWithoutExtension(_path);
    public override string SourcePath => _path;
    public override int PageCount => EnsureOpen().Count;

    private List<ZipArchiveEntry> EnsureOpen()
    {
        if (_archive is null)
        {
            _archive = ZipFile.OpenRead(_path);
            _entries = _archive.Entries
                .Where(e => !e.FullName.EndsWith("/"))
                .Where(e => !e.FullName.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                .Where(e => IsImageExtension(e.Name))
                .ToList();
            _entries.Sort((a, b) => ImageInfo.CompareNatural(a.FullName, b.FullName));
        }
        return _entries!;
    }

    private static bool IsImageExtension(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".avif";
    }

    public override async Task<Stream> OpenPageAsync(int index, CancellationToken ct = default)
    {
        var entry = EnsureOpen()[index];
        var ms = new MemoryStream();
        await using var es = entry.Open();
        await es.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    public override async Task<PageSize?> GetPageSizeAsync(int index, CancellationToken ct = default)
    {
        var entry = EnsureOpen()[index];
        await using var es = entry.Open();
        var head = new byte[64];
        int read = await es.ReadAsync(head, ct);
        return ImageInfo.ParseHeader(head.AsSpan(0, read));
    }

    public override async Task<ComicMetadata?> ReadMetadataAsync(CancellationToken ct = default)
    {
        EnsureOpen();
        var entry = _archive!.Entries.FirstOrDefault(e =>
            e.FullName.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        await using var es = entry.Open();
        var doc = await XDocument.LoadAsync(es, LoadOptions.None, ct);
        var root = doc.Root;
        if (root is null) return null;
        string? S(string n) => root.Element(n)?.Value;
        var manga = S("Manga");
        return new ComicMetadata
        {
            Title = S("Title"),
            Series = S("Series"),
            Volume = S("Volume"),
            Writer = S("Writer"),
            PageCount = int.TryParse(S("PageCount"), out var pc) ? pc : null,
            MangaRightToLeft = manga is not null && manga.StartsWith("Yes", StringComparison.OrdinalIgnoreCase),
        };
    }

    public override ValueTask DisposeAsync()
    {
        _archive?.Dispose();
        _archive = null;
        return ValueTask.CompletedTask;
    }
}
