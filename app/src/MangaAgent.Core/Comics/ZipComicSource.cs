using System.IO.Compression;
using System.Xml.Linq;

namespace MangaAgent.Core.Comics;

/// <summary>CBZ/ZIP 漫画来源：ZipArchive 随机读取。
/// 线程安全（P2-1）：ZipArchive 非线程安全 —— 所有 entry.Open() 都在同一个 FileStream 上 Seek，
/// 并发读写会互相踩位置（解码与 LoadSizes 后台补全并发时读坏/抛异常）。所有条目访问经 _gate 串行化：
/// 读取体一律在 Task.Run（线程池、无 SynchronizationContext）中执行 + 普通 lock ——
/// 绝不在持锁时 await，避免 UI 线程同步取 PageCount 与持锁任务的 UI 上下文续体互相等待而死锁。</summary>
public sealed class ZipComicSource : ComicSource
{
    private readonly string _path;
    private ZipArchive? _archive;
    private List<ZipArchiveEntry>? _entries;
    private readonly object _gate = new();
    private bool _disposedFlag;   // P2-3：Dispose 后禁止 EnsureOpen 重开文件（防泄漏）

    public ZipComicSource(string path) => _path = path;

    public override string Name => Path.GetFileNameWithoutExtension(_path);
    public override string SourcePath => _path;
    public override int PageCount
    {
        get
        {
            lock (_gate)
            {
                EnsureOpen();
                return _entries!.Count;
            }
        }
    }

    private void EnsureOpen()
    {
        if (_disposedFlag) throw new ObjectDisposedException(nameof(ZipComicSource));
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
    }

    private static bool IsImageExtension(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" or ".avif";
    }

    public override Task<Stream> OpenPageAsync(int index, CancellationToken ct = default)
    {
        // 整个读取（含 entry.Open 的 Seek + 解压拷贝）在线程池执行：不进调用方 SynchronizationContext，
        // 持锁期间无 await → UI 线程同步调 PageCount/GetPageSize 只等毫秒级，不会死锁。
        return Task.Run<Stream>(() =>
        {
            var ms = new MemoryStream();
            lock (_gate)
            {
                EnsureOpen();
                var entry = _entries![index];
                using var es = entry.Open();
                es.CopyTo(ms);
            }
            ms.Position = 0;
            return ms;
        }, ct);
    }

    public override PageSize? GetPageSize(int index)
    {
        lock (_gate)
        {
            EnsureOpen();
            var entry = _entries![index];
            using var es = entry.Open();
            var head = new byte[65536];   // 大 JPEG 的 APP 段可能很长，SOF 靠后
            int read = es.Read(head, 0, head.Length);
            return ImageInfo.ParseHeader(head.AsSpan(0, read));
        }
    }

    public override async Task<ComicMetadata?> ReadMetadataAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            lock (_gate)
            {
                EnsureOpen();
                var entry = _archive!.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
                if (entry is null) return null;
                using var es = entry.Open();
                var doc = XDocument.Load(es);
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
        }, ct);
    }

    public override ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposedFlag = true;
            _archive?.Dispose();
            _archive = null;
        }
        return ValueTask.CompletedTask;
    }
}
