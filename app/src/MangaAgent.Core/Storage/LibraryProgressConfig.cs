using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MangaAgent.Core.Storage;

/// <summary>v4.21：库内阅读进度配置 <c>progress.json</c>（每个库根一份，随库移动/改名）。
/// 结构：
/// <code>
/// {
///   "version": 1,
///   "books": {
///     "作者A/书.zip": { "page": 12, "mode": "Double", "direction": "Rtl", "updatedAt": "..." }
///   },
///   "bookmarks": {
///     "作者A/书.zip": [ { "page": 5, "note": "chapter start", "createdAt": "..." } ]
///   }
/// }
/// </code>
/// 键 = 书相对库根的路径（'/' 分隔）。
/// 写入由 <see cref="LibraryStateService"/> 统一节流（翻页高频，不能每次写盘）；
/// 本类只负责"脏数据 → 原子写"（.tmp → File.Replace，旧文件留 .bak），
/// 写前与磁盘合并（merge-on-write），坏 JSON 备份为 .broken-时间戳 后重建。</summary>
public sealed class LibraryProgressConfig
{
    public const string FileName = "progress.json";
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Dictionary<string, BookProgress> _books = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<BookmarkEntryInfo>> _bookmarks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removedBooks = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;
    private long _fpLength = -1;
    private DateTime _fpMtime = DateTime.MinValue;

    public string LibraryRoot { get; }
    public string FilePath { get; }
    public string? LastError { get; private set; }

    /// <summary>单本书的阅读状态。</summary>
    private readonly record struct BookProgress(int Page, string Mode, string Direction, string UpdatedAt);

    private LibraryProgressConfig(string root)
    {
        LibraryRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        FilePath = Path.Combine(LibraryRoot, FileName);
    }

    public static LibraryProgressConfig Load(string libraryRoot)
    {
        var c = new LibraryProgressConfig(libraryRoot);
        c.Reload(force: true);
        return c;
    }

    public bool IsDirty
    {
        get { lock (_gate) return _dirty; }
    }

    /// <summary>绝对书路径 → 配置键（相对库根，'/' 分隔）；不在库内返回 false。</summary>
    public bool TryKeyFor(string bookAbsPath, out string key)
        => TryRelativeKey(LibraryRoot, bookAbsPath, out key);

    internal static bool TryRelativeKey(string libraryRoot, string absPath, out string key)
    {
        key = "";
        try
        {
            var full = Path.GetFullPath(absPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!full.StartsWith(libraryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(libraryRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            key = Path.GetRelativePath(libraryRoot, full).Replace('\\', '/');
            return key.Length > 0 && !key.StartsWith("..", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    // ---------- 阅读状态 ----------

    public bool TrySetState(string key, int page, ReadingMode mode, ReadingDirection2 direction)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(key)) return false;
            _books[key] = new BookProgress(page, mode.ToString(), direction.ToString(), DateTime.UtcNow.ToString("O"));
            _removedBooks.Remove(key);
            _dirty = true;
            return true;
        }
    }

    public (int LogicalPage, ReadingMode Mode, ReadingDirection2 Direction)? GetState(string key)
    {
        lock (_gate)
        {
            if (FileChanged()) Reload(force: true);
            if (!_books.TryGetValue(key, out var p)) return null;
            return (
                p.Page,
                Enum.TryParse<ReadingMode>(p.Mode, out var m) ? m : ReadingMode.Single,
                Enum.TryParse<ReadingDirection2>(p.Direction, out var d) ? d : ReadingDirection2.Rtl);
        }
    }

    // ---------- 书签 ----------

    public IReadOnlyList<BookmarkEntryInfo> GetBookmarks(string key)
    {
        lock (_gate)
        {
            if (FileChanged()) Reload(force: true);
            return _bookmarks.TryGetValue(key, out var list)
                ? list.OrderBy(b => b.Page).ToList()
                : Array.Empty<BookmarkEntryInfo>();
        }
    }

    public void AddBookmark(string key, int page, string? note)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_bookmarks.TryGetValue(key, out var list)) _bookmarks[key] = list = new List<BookmarkEntryInfo>();
            list.RemoveAll(b => b.Page == page);
            list.Add(new BookmarkEntryInfo(page, note, DateTime.UtcNow.ToString("O")));
            list.Sort((a, b) => a.Page.CompareTo(b.Page));
            _dirty = true;
        }
    }

    public bool RemoveBookmark(string key, int page)
    {
        lock (_gate)
        {
            if (!_bookmarks.TryGetValue(key, out var list)) return false;
            int removed = list.RemoveAll(b => b.Page == page);
            if (removed > 0) _dirty = true;
            // 注意：保留空列表而不是移除 key —— merge-on-write 时用"空列表"覆盖磁盘旧值，
            // 否则磁盘上的旧书签会在下次合并时被写回来（书签没有 tombstone 机制）。
            return removed > 0;
        }
    }

    /// <summary>该书是否有进度记录（v4.21：用于区分"没有记录（可回退 db）"与"记录为空"）。</summary>
    public bool ContainsStateKey(string key)
    {
        lock (_gate)
        {
            if (FileChanged()) Reload(force: true);
            return _books.ContainsKey(key);
        }
    }

    /// <summary>该书是否有书签记录（可能为空列表 → 表示"已清空"，不应回退 db）。</summary>
    public bool ContainsBookKey(string key)
    {
        lock (_gate)
        {
            if (FileChanged()) Reload(force: true);
            return _bookmarks.ContainsKey(key);
        }
    }

    /// <summary>删除某书（或某文件夹前缀）的全部记录：状态 + 书签 + 前缀内所有书。</summary>
    public bool ClearBook(string key)
    {
        lock (_gate)
        {
            bool any = false;
            foreach (var k in _books.Keys.Where(k => Matches(k, key)).ToList())
            {
                _books.Remove(k);
                _removedBooks.Add(k);
                any = true;
            }
            foreach (var k in _bookmarks.Keys.Where(k => Matches(k, key)).ToList())
            {
                _bookmarks.Remove(k);
                any = true;
            }
            if (any) _dirty = true;
            return any;
        }

        static bool Matches(string k, string target)
            => k.Equals(target, StringComparison.OrdinalIgnoreCase)
               || k.StartsWith(target + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>全部状态键（迁移/诊断用）。</summary>
    public IReadOnlyList<string> StateKeys
    {
        get { lock (_gate) { if (FileChanged()) Reload(force: true); return _books.Keys.ToList(); } }
    }

    /// <summary>落盘（脏才写）。false = 库根不可写（上层回退 db 并提示）。</summary>
    public bool Flush()
    {
        lock (_gate)
        {
            if (!_dirty) return true;
            try
            {
                var (books, marks) = ReadFromDisk();
                foreach (var k in _removedBooks)
                {
                    books.Remove(k);
                    marks.Remove(k);
                }
                foreach (var kv in _books) books[kv.Key] = kv.Value;
                foreach (var kv in _bookmarks) marks[kv.Key] = kv.Value;
                WriteToDisk(books, marks);
                _removedBooks.Clear();
                _dirty = false;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }

    // ---------- 内部：读写 / 指纹 / 备份 ----------

    private void Reload(bool force)
    {
        if (!force && !FileChanged()) return;
        lock (_gate)
        {
            _books.Clear();
            _bookmarks.Clear();
            _removedBooks.Clear();
            _dirty = false;
            if (!File.Exists(FilePath))
            {
                _fpLength = -1;
                _fpMtime = DateTime.MinValue;
                return;
            }
            try
            {
                var (books, marks) = ReadFromDisk();
                foreach (var kv in books) _books[kv.Key] = kv.Value;
                foreach (var kv in marks) _bookmarks[kv.Key] = kv.Value;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastError = $"progress.json 解析失败：{ex.Message}";
                BackupBrokenFile();
            }
            UpdateFingerprint();
        }
    }

    private bool FileChanged()
    {
        try
        {
            if (!File.Exists(FilePath)) return _fpLength != -1;
            var info = new FileInfo(FilePath);
            return info.Length != _fpLength || info.LastWriteTimeUtc != _fpMtime;
        }
        catch { return false; }
    }

    private void UpdateFingerprint()
    {
        try
        {
            if (!File.Exists(FilePath)) { _fpLength = -1; _fpMtime = DateTime.MinValue; return; }
            var info = new FileInfo(FilePath);
            _fpLength = info.Length;
            _fpMtime = info.LastWriteTimeUtc;
        }
        catch { _fpLength = -1; }
    }

    private void BackupBrokenFile()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            File.Move(FilePath, $"{FilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
            _fpLength = -1;
            _fpMtime = DateTime.MinValue;
        }
        catch { /* 备份失败不阻塞 */ }
    }

    private (Dictionary<string, BookProgress> Books, Dictionary<string, List<BookmarkEntryInfo>> Marks) ReadFromDisk()
    {
        var books = new Dictionary<string, BookProgress>(StringComparer.OrdinalIgnoreCase);
        var marks = new Dictionary<string, List<BookmarkEntryInfo>>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FilePath)) return (books, marks);
        var text = File.ReadAllText(FilePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) return (books, marks);
        var root = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("根节点不是 JSON 对象");
        if (root["books"] is JsonObject b)
            foreach (var kv in b)
            {
                if (kv.Value is not JsonObject o) continue;
                books[kv.Key] = new BookProgress(
                    o["page"]?.GetValue<int>() ?? 0,
                    o["mode"]?.GetValue<string>() ?? "Single",
                    o["direction"]?.GetValue<string>() ?? "Rtl",
                    o["updatedAt"]?.GetValue<string>() ?? "");
            }
        if (root["bookmarks"] is JsonObject m)
            foreach (var kv in m)
            {
                if (kv.Value is not JsonArray arr) continue;
                var list = new List<BookmarkEntryInfo>();
                foreach (var item in arr)
                {
                    if (item is not JsonObject o) continue;
                    list.Add(new BookmarkEntryInfo(
                        o["page"]?.GetValue<int>() ?? 0,
                        o["note"]?.GetValue<string>(),
                        o["createdAt"]?.GetValue<string>()));
                }
                list.Sort((a, b) => a.Page.CompareTo(b.Page));
                marks[kv.Key] = list;
            }
        return (books, marks);
    }

    private void WriteToDisk(Dictionary<string, BookProgress> books, Dictionary<string, List<BookmarkEntryInfo>> marks)
    {
        JsonObject root;
        try
        {
            root = (File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JsonObject : null)
                   ?? new JsonObject();
        }
        catch { root = new JsonObject(); }

        var booksObj = new JsonObject();
        foreach (var kv in books.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            booksObj[kv.Key] = new JsonObject
            {
                ["page"] = kv.Value.Page,
                ["mode"] = kv.Value.Mode,
                ["direction"] = kv.Value.Direction,
                ["updatedAt"] = kv.Value.UpdatedAt,
            };
        var marksObj = new JsonObject();
        foreach (var kv in marks.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var arr = new JsonArray();
            foreach (var entry in kv.Value)
                arr.Add(new JsonObject { ["page"] = entry.Page, ["note"] = entry.Note, ["createdAt"] = entry.CreatedAt });
            marksObj[kv.Key] = arr;
        }
        root["version"] = 1;
        root["books"] = booksObj;
        root["bookmarks"] = marksObj;

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOpts), new UTF8Encoding(false));
        if (File.Exists(FilePath)) File.Replace(tmp, FilePath, FilePath + ".bak");
        else File.Move(tmp, FilePath);
        UpdateFingerprint();
    }
}

/// <summary>v4.21：一条书签（页码 + 可选备注 + 创建时间）。</summary>
public readonly record struct BookmarkEntryInfo(int Page, string? Note, string? CreatedAt);
