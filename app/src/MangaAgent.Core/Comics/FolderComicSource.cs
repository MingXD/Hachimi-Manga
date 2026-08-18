namespace MangaAgent.Core.Comics;

/// <summary>文件夹漫画来源：目录内图片按自然排序。</summary>
public sealed class FolderComicSource : ComicSource
{
    private readonly string _path;
    private List<string>? _files;

    public FolderComicSource(string path) => _path = path;

    public override string Name => new DirectoryInfo(_path).Name;
    public override string SourcePath => _path;
    public override int PageCount => EnsureFiles().Count;

    private List<string> EnsureFiles()
    {
        if (_files is null)
        {
            var files = Directory.GetFiles(_path)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp")
                .ToList();
            files.Sort((a, b) => ImageInfo.CompareNatural(Path.GetFileName(a), Path.GetFileName(b)));
            _files = files;
        }
        return _files;
    }

    public override Task<Stream> OpenPageAsync(int index, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(EnsureFiles()[index]));

    public override PageSize? GetPageSize(int index)
    {
        using var stream = File.OpenRead(EnsureFiles()[index]);
        var head = new byte[65536];   // 大 JPEG 的 APP 段可能很长，SOF 靠后
        int read = stream.Read(head, 0, head.Length);
        return ImageInfo.ParseHeader(head.AsSpan(0, read));
    }

    public override Task<ComicMetadata?> ReadMetadataAsync(CancellationToken ct = default)
        => Task.FromResult<ComicMetadata?>(null);

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
