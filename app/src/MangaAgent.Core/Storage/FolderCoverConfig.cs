using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MangaAgent.Core.Storage;

/// <summary>v4.20：库内封面配置 <c>covers.json</c>（每个库根一份，随库移动/改名，不再依赖 reading.db）。
/// 文件结构：
/// <code>
/// {
///   "version": 1,
///   "covers": {
///     ".": "cover.png",
///     "zhongzi/nemuke？ (似せ)": "001.jpg",
///     "[个人汉化] [暗稿] チンポマン [DL版]": "../_shared/folder-cover.jpg"
///   }
/// }
/// </code>
/// 键 = 文件夹相对库根的路径（'/' 分隔；库根自身 = "."）；值 = 封面图片相对库根的路径。
/// 行为要点：
/// - 读：内存缓存 + 文件指纹（LastWriteTimeUtc+长度）检测外部手改，变了才重读；
/// - 写：脏标记 → 原子替换（先写 .tmp 再 File.Replace，旧文件留 .bak）；
/// - 写前与磁盘内容合并（merge-on-write，内存优先、删除优先），避免多实例互相吞记录；
/// - 坏 JSON：备份为 covers.json.broken-时间戳 后从空表重建（绝不静默覆盖用户手写内容）；
/// - 库根不可写：<see cref="Flush"/> 返回 false，由上层回退 SQLite（reading.db）。</summary>
public sealed class FolderCoverConfig
{
    public const string FileName = "covers.json";
    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".avif" };
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _covers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;
    private long _fpLength = -1;
    private DateTime _fpMtime = DateTime.MinValue;

    /// <summary>所属库根（去尾分隔符）。</summary>
    public string LibraryRoot { get; }
    /// <summary>配置文件绝对路径 = 库根\covers.json。</summary>
    public string FilePath { get; }
    /// <summary>最近一次读/写失败原因（诊断/状态栏用）。</summary>
    public string? LastError { get; private set; }

    private FolderCoverConfig(string libraryRoot)
    {
        LibraryRoot = libraryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        FilePath = Path.Combine(LibraryRoot, FileName);
    }

    /// <summary>加载某库根的封面配置（文件缺失 = 空配置，不抛）。</summary>
    public static FolderCoverConfig Load(string libraryRoot)
    {
        var c = new FolderCoverConfig(libraryRoot);
        c.Reload(force: true);
        return c;
    }

    /// <summary>绝对文件夹路径 → 配置键（"." = 库根自身；不在库内返回 false）。</summary>
    public bool TryKeyFor(string folderAbsPath, out string key)
    {
        key = "";
        try
        {
            var full = Path.GetFullPath(folderAbsPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(LibraryRoot, StringComparison.OrdinalIgnoreCase)) { key = "."; return true; }
            if (!full.StartsWith(LibraryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(LibraryRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            key = NormalizeKey(Path.GetRelativePath(LibraryRoot, full));
            return key.Length > 0 && !key.StartsWith("..", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>键 → 绝对文件夹路径。</summary>
    public string KeyToFolderPath(string key)
    {
        var k = NormalizeKey(key);
        return k is "" or "." ? LibraryRoot : Path.Combine(LibraryRoot, k.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>设置封面（绝对路径；封面必须位于库内、必须是图片、必须存在）。成功后需调用 <see cref="Flush"/> 落盘。</summary>
    public bool TrySet(string folderAbsPath, string coverAbsPath, out string error)
    {
        lock (_gate)
        {
            error = "";
            if (!TryKeyFor(folderAbsPath, out var key)) { error = "文件夹不在当前库内"; return false; }
            if (string.IsNullOrWhiteSpace(coverAbsPath) || !File.Exists(coverAbsPath)) { error = "封面图片不存在"; return false; }
            var ext = Path.GetExtension(coverAbsPath).ToLowerInvariant();
            if (Array.IndexOf(ImageExts, ext) < 0) { error = "不是支持的图片格式（png/jpg/jpeg/webp/gif/bmp/avif）"; return false; }
            if (!TryRelFromRoot(coverAbsPath, out var rel)) { error = "封面图片必须位于该库目录内"; return false; }
            _covers[key] = rel;
            _removed.Remove(key);
            _dirty = true;
            return true;
        }
    }

    /// <summary>按"键 + 相对库根封面路径"写入（移动文件夹/迁移用；封面必须存在）。</summary>
    public bool TrySetByRel(string folderKey, string coverRel, out string error)
    {
        lock (_gate)
        {
            error = "";
            var key = NormalizeKey(folderKey);
            var rel = NormalizeKey(coverRel);
            if (rel.Length == 0) { error = "封面路径为空"; return false; }
            if (rel.StartsWith("..", StringComparison.Ordinal)) { error = "封面路径越界（不能使用 .. 指向库外）"; return false; }
            if (key.Length > 1 && key.StartsWith("..", StringComparison.Ordinal)) { error = "文件夹键越界"; return false; }
            string abs;
            try { abs = Path.Combine(LibraryRoot, rel.Replace('/', Path.DirectorySeparatorChar)); } catch { error = "封面路径无效"; return false; }
            if (!File.Exists(abs)) { error = "封面图片不存在"; return false; }
            var ext = Path.GetExtension(abs).ToLowerInvariant();
            if (Array.IndexOf(ImageExts, ext) < 0) { error = "不是支持的图片格式"; return false; }
            _covers[key] = rel;
            _removed.Remove(key);
            _dirty = true;
            return true;
        }
    }

    /// <summary>清除某文件夹的封面记录（成功后需 <see cref="Flush"/>）。</summary>
    public bool TryClear(string folderAbsPath)
    {
        lock (_gate)
        {
            if (!TryKeyFor(folderAbsPath, out var key)) return false;
            if (!_covers.ContainsKey(key)) return true;   // 本来就没有
            _covers.Remove(key);
            _removed.Add(key);
            _dirty = true;
            return true;
        }
    }

    /// <summary>文件夹被移动时同库内改键：封面若在源目录内则前缀随之替换，否则保持原值。需 <see cref="Flush"/>。</summary>
    public bool MoveKey(string srcFolderAbs, string dstFolderAbs)
    {
        lock (_gate)
        {
            if (!TryKeyFor(srcFolderAbs, out var srcKey)) return false;
            if (!TryKeyFor(dstFolderAbs, out var dstKey)) return false;
            if (srcKey is "" or ".") return false;             // 库根自身不可移动
            if (string.Equals(srcKey, dstKey, StringComparison.OrdinalIgnoreCase)) return false;
            if (!_covers.TryGetValue(srcKey, out var rel)) return false;
            var newRel = rel;
            if (rel.Equals(srcKey, StringComparison.OrdinalIgnoreCase) || rel.StartsWith(srcKey + "/", StringComparison.OrdinalIgnoreCase))
                newRel = dstKey + rel.Substring(srcKey.Length);   // 保留原后缀（含前导 '/'）
            _covers.Remove(srcKey);
            _removed.Add(srcKey);
            _covers[dstKey] = newRel;
            _removed.Remove(dstKey);
            _dirty = true;
            return true;
        }
    }

    /// <summary>判断某文件夹是否有记录（不校验文件存在）。</summary>
    public bool HasCover(string folderAbsPath)
    {
        lock (_gate)
        {
            if (!TryKeyFor(folderAbsPath, out var key)) return false;
            if (FileChanged()) Reload(force: true);
            return _covers.ContainsKey(key);
        }
    }

    /// <summary>批量解析（目录浏览用）：文件夹绝对路径 → 封面绝对路径（文件不存在的条目跳过）。</summary>
    public Dictionary<string, string> ResolveFor(IEnumerable<string> folderAbsPaths)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var paths = folderAbsPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count == 0) return result;
        lock (_gate)
        {
            if (FileChanged()) Reload(force: true);
            foreach (var abs in paths)
            {
                if (!TryKeyFor(abs, out var key)) continue;
                if (!_covers.TryGetValue(key, out var rel) || string.IsNullOrEmpty(rel)) continue;
                string full;
                try { full = Path.Combine(LibraryRoot, rel.Replace('/', Path.DirectorySeparatorChar)); } catch { continue; }
                if (File.Exists(full)) result[abs] = full;
            }
        }
        return result;
    }

    /// <summary>单个文件夹的封面绝对路径（无记录/文件缺失 → null）。</summary>
    public string? GetCoverFullPath(string folderAbsPath)
    {
        lock (_gate)
        {
            if (!TryKeyFor(folderAbsPath, out var key)) return null;
            if (FileChanged()) Reload(force: true);   // 外部手改（如资源管理器编辑）后自动生效
            if (!_covers.TryGetValue(key, out var rel) || string.IsNullOrEmpty(rel)) return null;
            try
            {
                var full = Path.Combine(LibraryRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(full) ? full : null;
            }
            catch { return null; }
        }
    }

    /// <summary>v4.21（P1-4）：检查本条库的封面记录健康度（键对应文件夹是否还在、封面图是否还在）。
    /// 返回 (文件夹缺失数, 图缺失数, 样例)；只读不修改，供设置页提示。</summary>
    public (int MissingFolder, int MissingImage, List<string> Samples) Inspect()
    {
        var samples = new List<string>();
        int missingFolder = 0, missingImage = 0;
        foreach (var (key, rel) in All)
        {
            var folderAbs = key == "." ? LibraryRoot : Path.Combine(LibraryRoot, key.Replace('/', Path.DirectorySeparatorChar));
            bool folderOk;
            try { folderOk = Directory.Exists(folderAbs); } catch { folderOk = false; }
            bool imageOk;
            try { imageOk = File.Exists(Path.Combine(LibraryRoot, rel.Replace('/', Path.DirectorySeparatorChar))); }
            catch { imageOk = false; }
            if (!folderOk) { missingFolder++; if (samples.Count < 5) samples.Add($"文件夹不存在：{key}"); }
            else if (!imageOk) { missingImage++; if (samples.Count < 5) samples.Add($"封面图不存在：{key} → {rel}"); }
        }
        return (missingFolder, missingImage, samples);
    }

    /// <summary>v4.21（P1-4）：清理无效封面记录（文件夹或图已不存在）。返回清理条数，需调用 <see cref="Flush"/>。</summary>
    public int RemoveInvalid()
    {
        int removed = 0;
        foreach (var (key, rel) in All)
        {
            var folderAbs = key == "." ? LibraryRoot : Path.Combine(LibraryRoot, key.Replace('/', Path.DirectorySeparatorChar));
            bool folderOk, imageOk;
            try { folderOk = Directory.Exists(folderAbs); } catch { folderOk = false; }
            try { imageOk = File.Exists(Path.Combine(LibraryRoot, rel.Replace('/', Path.DirectorySeparatorChar))); }
            catch { imageOk = false; }
            if (folderOk && imageOk) continue;
            lock (_gate)
            {
                if (_covers.Remove(key)) { _removed.Add(key); _dirty = true; removed++; }
            }
        }
        return removed;
    }

    /// <summary>快照：某文件夹当前的配置键与封面相对路径（不校验存在；移动前捕获用）。</summary>
    public bool TrySnapshot(string folderAbsPath, out string key, out string rel)
    {
        lock (_gate)
        {
            key = "";
            rel = "";
            if (!TryKeyFor(folderAbsPath, out var k)) return false;
            if (!_covers.TryGetValue(k, out var r) || string.IsNullOrEmpty(r)) return false;
            key = k;
            rel = r;
            return true;
        }
    }

    /// <summary>全部记录（迁移/导出/诊断用）。</summary>
    public IReadOnlyDictionary<string, string> All
    {
        get
        {
            lock (_gate)
            {
                if (FileChanged()) Reload(force: true);
                return new Dictionary<string, string>(_covers, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>落盘（脏才写）。返回 false = 库根不可写（上层回退 SQLite）。</summary>
    public bool Flush()
    {
        lock (_gate)
        {
            if (!_dirty) return true;
            try
            {
                // merge-on-write：先读磁盘最新内容，再覆盖本进程改过的键（删除优先）
                var disk = ReadCoversFromDisk();
                foreach (var k in _removed) disk.Remove(k);
                foreach (var kv in _covers) disk[kv.Key] = kv.Value;
                WriteCoversToDisk(disk);
                _removed.Clear();
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
        _covers.Clear();
        _removed.Clear();
        _dirty = false;
        if (!File.Exists(FilePath))
        {
            _fpLength = -1;
            _fpMtime = DateTime.MinValue;
            return;
        }
        try
        {
            foreach (var kv in ReadCoversFromDisk()) _covers[kv.Key] = kv.Value;
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = $"covers.json 解析失败：{ex.Message}";
            BackupBrokenFile();
        }
        UpdateFingerprint();
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
            var broken = $"{FilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, broken, overwrite: true);
            _fpLength = -1;
            _fpMtime = DateTime.MinValue;
        }
        catch { /* 备份失败不阻塞（下次写入会覆盖） */ }
    }

    private Dictionary<string, string> ReadCoversFromDisk()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FilePath)) return dict;
        var text = File.ReadAllText(FilePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text)) return dict;
        var root = JsonNode.Parse(text) as JsonObject ?? throw new FormatException("根节点不是 JSON 对象");
        if (root["covers"] is not JsonObject covers) return dict;
        foreach (var kv in covers)
        {
            var key = NormalizeKey(kv.Key);
            if (key.Length == 0) continue;
            if (kv.Value?.GetValue<string>() is { } rel && rel.Length > 0)
                dict[key] = NormalizeKey(rel);
        }
        return dict;
    }

    private void WriteCoversToDisk(Dictionary<string, string> covers)
    {
        // 保留用户手写的其它顶层字段（只覆盖 version/covers）
        JsonObject root;
        try
        {
            root = (File.Exists(FilePath) ? JsonNode.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as JsonObject : null)
                   ?? new JsonObject();
        }
        catch { root = new JsonObject(); }   // 坏文件：交给 Reload 备份逻辑处理

        var coversObj = new JsonObject();
        foreach (var kv in covers.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            coversObj[kv.Key] = kv.Value;
        root["version"] = 1;
        root["covers"] = coversObj;

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOpts), new UTF8Encoding(false));
        if (File.Exists(FilePath))
            File.Replace(tmp, FilePath, FilePath + ".bak");   // 旧内容留 .bak
        else
            File.Move(tmp, FilePath);
        UpdateFingerprint();
    }

    private bool TryRelFromRoot(string absPath, out string rel)
    {
        rel = "";
        try
        {
            var full = Path.GetFullPath(absPath);
            if (!full.StartsWith(LibraryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !full.StartsWith(LibraryRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            rel = NormalizeKey(Path.GetRelativePath(LibraryRoot, full));
            return rel.Length > 0 && !rel.StartsWith("..", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>统一分隔符为 '/'，去掉开头 "./"（空 = 库根自身）。</summary>
    internal static string NormalizeKey(string? value)
    {
        var s = (value ?? "").Trim().Replace('\\', '/');
        while (s.StartsWith("./", StringComparison.Ordinal)) s = s.Substring(2);
        while (s.StartsWith("/", StringComparison.Ordinal)) s = s.Substring(1);
        while (s.EndsWith("/", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1);
        return s is "" or "." ? "." : s;
    }
}
