namespace MangaAgent.Core.Storage;

/// <summary>v4.21：阅读进度/书签的"按库路由"服务（v4.20 第二步）。
/// 规则与封面一致：**写最具体库根的 <c>progress.json</c>；读时从最具体库根往最外层逐级回退**；
/// 不属于任何库的书（外部临时文件）回退到 reading.db；库根不可写时回退 db 并回调 <see cref="OnWarning"/>。
/// 写入节流：翻页/滚动会高频调用 <see cref="SaveState"/>，距上次落盘 &lt; <see cref="FlushIntervalMs"/> 时只写内存，
/// 由 <see cref="Flush"/>（关书/关窗/定时器）统一落盘 —— 异常退出最多丢最近一次间隔内的页码。</summary>
public sealed class LibraryStateService : ILibraryStateStore, IDisposable
{
    private readonly string _dbPath;
    private readonly object _gate = new();
    private readonly Dictionary<string, LibraryProgressConfig> _configs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _warnedRoots = new(StringComparer.OrdinalIgnoreCase);
    private ReadingStateStore? _db;
    private long _lastFlushMs;
    private bool _disposed;

    /// <summary>节流间隔（毫秒）。测试可设为 0 → 每次写入立即落盘。</summary>
    public static int FlushIntervalMs { get; set; } = 2000;

    /// <summary>写入失败（库不可写等）时的提示回调（可能在后台线程触发，UI 侧自行切线程）。</summary>
    public Action<string>? OnWarning { get; set; }

    public LibraryStateService(string stateDbPath) => _dbPath = stateDbPath;

    private ReadingStateStore Db => _db ??= new ReadingStateStore(_dbPath);

    private LibraryProgressConfig ConfigFor(string root)
    {
        lock (_gate)
        {
            if (_configs.TryGetValue(root, out var cfg)) return cfg;
            cfg = LibraryProgressConfig.Load(root);
            _configs[root] = cfg;
            return cfg;
        }
    }

    /// <summary>绝对路径的候选库根（长→短）。</summary>
    private static List<string> CandidateRoots(string absPath)
    {
        var list = new List<string>();
        string full;
        try { full = Path.GetFullPath(absPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return list; }
        foreach (var r in ReadingStateStore.LibraryRoots.OrderByDescending(x => x.Length))
        {
            if (string.IsNullOrEmpty(r)) continue;
            if (full.Equals(r, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                list.Add(r);
        }
        return list;
    }

    // ---------- 读 ----------

    public (int LogicalPage, ReadingMode Mode, ReadingDirection2 Direction)? LoadState(string bookPath)
    {
        foreach (var root in CandidateRoots(bookPath))
        {
            var cfg = ConfigFor(root);
            if (!cfg.TryKeyFor(bookPath, out var key)) continue;
            if (cfg.ContainsStateKey(key) && cfg.GetState(key) is { } state) return state;
        }
        try { return Db.LoadState(bookPath); } catch { return null; }   // 回退：迁移失败/库外路径
    }

    public IReadOnlyList<BookmarkEntryInfo> LoadBookmarkEntries(string bookPath)
    {
        foreach (var root in CandidateRoots(bookPath))
        {
            var cfg = ConfigFor(root);
            if (!cfg.TryKeyFor(bookPath, out var key)) continue;
            // 库文件里"有该书的记录"（哪怕已被清空为空列表）即视为权威，不回退 db
            if (cfg.ContainsBookKey(key)) return cfg.GetBookmarks(key);
        }
        try { return Db.LoadBookmarkEntries(bookPath); } catch { return Array.Empty<BookmarkEntryInfo>(); }
    }

    public List<int> LoadBookmarks(string bookPath)
        => LoadBookmarkEntries(bookPath).Select(b => b.Page).ToList();

    // ---------- 写 ----------

    public void SaveState(string bookPath, int logicalPage, ReadingMode mode, ReadingDirection2 direction)
    {
        foreach (var root in CandidateRoots(bookPath))
        {
            var cfg = ConfigFor(root);
            if (!cfg.TryKeyFor(bookPath, out var key)) continue;
            if (cfg.TrySetState(key, logicalPage, mode, direction)) { ScheduleFlush(); return; }
            break;
        }
        try { Db.SaveState(bookPath, logicalPage, mode, direction); } catch (Exception ex) { Warn("(db)", ex.Message); }
    }

    public void AddBookmark(string bookPath, int page, string? note = null)
    {
        foreach (var root in CandidateRoots(bookPath))
        {
            var cfg = ConfigFor(root);
            if (!cfg.TryKeyFor(bookPath, out var key)) continue;
            cfg.AddBookmark(key, page, note);
            ScheduleFlush();
            return;
        }
        try { Db.AddBookmark(bookPath, page, note); } catch (Exception ex) { Warn("(db)", ex.Message); }
    }

    public void RemoveBookmark(string bookPath, int page)
    {
        foreach (var root in CandidateRoots(bookPath))
        {
            var cfg = ConfigFor(root);
            if (!cfg.TryKeyFor(bookPath, out var key)) continue;
            if (cfg.RemoveBookmark(key, page)) ScheduleFlush();
            return;
        }
        try { Db.RemoveBookmark(bookPath, page); } catch (Exception ex) { Warn("(db)", ex.Message); }
    }

    /// <summary>v4.22：清空某本书的全部书签（书签面板"清空"按钮；不动阅读进度）。</summary>
    public void ClearBookmarks(string bookPath)
    {
        foreach (var root in CandidateRoots(bookPath))
        {
            var cfg = ConfigFor(root);
            if (!cfg.TryKeyFor(bookPath, out var key)) continue;
            if (cfg.ClearBookmarks(key) > 0) ScheduleFlush();
            return;
        }
        try { Db.ClearBookmarks(bookPath); } catch (Exception ex) { Warn("(db)", ex.Message); }
    }

    /// <summary>v4.22：把 src 的进度/书签数据迁到 dst（复制或移动；支持文件夹前缀=其下所有书）。
    /// 目标已存在同名数据时**以本次为准覆盖**；<paramref name="isCopy"/>=true 时源保留。
    /// 返回 (迁入状态数, 迁入书签数)。只处理进度/书签；封面由调用方（主窗口）另行处理。</summary>
    public (int States, int Bookmarks) CopyOrMoveBookData(string srcAbs, string dstAbs, bool isCopy)
    {
        if (string.IsNullOrWhiteSpace(srcAbs) || string.IsNullOrWhiteSpace(dstAbs)) return (0, 0);
        int states = 0, bookmarks = 0;

        // 源键（相对源库根）与目标键
        var srcRoot = CandidateRoots(srcAbs).FirstOrDefault();
        string? srcKey = srcRoot is not null && ConfigFor(srcRoot).TryKeyFor(srcAbs, out var sk) ? sk : null;
        var dstRoot = CandidateRoots(dstAbs).FirstOrDefault();
        string? dstKey = dstRoot is not null && ConfigFor(dstRoot).TryKeyFor(dstAbs, out var dk) ? dk : null;

        // 收集源数据（库文件 → 库文件/库外）
        var stateRows = new List<(string SrcKey, string DstAbs, int Page, ReadingMode Mode, ReadingDirection2 Dir)>();
        var markRows = new List<(string SrcKey, string DstAbs, int Page, string? Note)>();
        if (srcRoot is not null && srcKey is not null)
        {
            var cfg = ConfigFor(srcRoot);
            foreach (var (key, st) in cfg.StateSnapshot())
            {
                if (!TryMapKey(key, srcKey, dstKey, dstAbs, out var dstAbsItem)) continue;
                stateRows.Add((key, dstAbsItem, st.Page, st.Mode, st.Direction));
            }
            foreach (var (key, list) in cfg.BookmarkSnapshot())
            {
                if (list.Count == 0) continue;
                if (!TryMapKey(key, srcKey, dstKey, dstAbs, out var dstAbsItem)) continue;
                foreach (var b in list) markRows.Add((key, dstAbsItem, b.Page, b.Note));
            }
            // 目标已有同名数据 → 先清掉（本次覆盖）
            if (dstRoot is not null && dstKey is not null && !string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase))
                ConfigFor(dstRoot).ClearBook(dstKey);
        }

        // 源是库外书 → 数据在 db
        if (srcRoot is null)
        {
            try
            {
                if (Db.LoadState(srcAbs) is { } st)
                    stateRows.Add(("", dstAbs, st.LogicalPage, st.Mode, st.Direction));
                foreach (var b in Db.LoadBookmarkEntries(srcAbs))
                    markRows.Add(("", dstAbs, b.Page, b.Note));
            }
            catch { }
        }

        // 写入目标
        if (dstRoot is not null && dstKey is not null)
        {
            var dstCfg = ConfigFor(dstRoot);
            foreach (var r in stateRows)
            {
                var key = RelativeKeyFor(dstRoot, r.DstAbs);
                if (key is not null && dstCfg.TrySetState(key, r.Page, r.Mode, r.Dir)) states++;
            }
            foreach (var r in markRows)
            {
                var key = RelativeKeyFor(dstRoot, r.DstAbs);
                if (key is null) continue;
                dstCfg.AddBookmark(key, r.Page, r.Note);
                bookmarks++;
            }
            if (!dstCfg.Flush()) Warn(dstRoot, dstCfg.LastError);
        }
        else
        {
            // 目标在库外 → 回退 db
            foreach (var r in stateRows)
                try { Db.SaveState(r.DstAbs, r.Page, r.Mode, r.Dir); states++; } catch { }
            foreach (var r in markRows)
                try { Db.AddBookmark(r.DstAbs, r.Page, r.Note); bookmarks++; } catch { }
        }

        // 移动：清源
        if (!isCopy)
        {
            if (srcRoot is not null && srcKey is not null)
            {
                var cfg = ConfigFor(srcRoot);
                if (cfg.ClearBook(srcKey) && !cfg.Flush()) Warn(srcRoot, cfg.LastError);
            }
            else
            {
                try
                {
                    if (Db.LoadState(srcAbs) is not null) Db.ClearState(srcAbs);
                    Db.ClearBookmarks(srcAbs);
                }
                catch { }
            }
        }
        return (states, bookmarks);
    }

    /// <summary>源键 → 目标绝对路径（同键或前缀匹配；dstKey 为空表示目标在库外）。</summary>
    private static bool TryMapKey(string srcItemKey, string srcKey, string? dstKey, string dstAbs, out string dstAbsItem)
    {
        dstAbsItem = dstAbs;
        string suffix;
        if (srcItemKey.Equals(srcKey, StringComparison.OrdinalIgnoreCase)) suffix = "";
        else if (srcItemKey.StartsWith(srcKey + "/", StringComparison.OrdinalIgnoreCase)) suffix = srcItemKey.Substring(srcKey.Length);
        else return false;
        if (dstKey is null) return suffix.Length == 0;   // 目标在库外：只迁精确匹配的书
        dstAbsItem = suffix.Length == 0
            ? dstAbs
            : Path.Combine(dstAbs, suffix.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        return true;
    }

    private static string? RelativeKeyFor(string root, string absPath)
        => LibraryProgressConfig.TryRelativeKey(root, absPath, out var key) ? key : null;

    /// <summary>删除书/文件夹时清理其进度与书签（所有候选库根；含父库根残留）。</summary>
    public void ClearBook(string bookAbsPath)
    {
        foreach (var root in CandidateRoots(bookAbsPath))
        {
            try
            {
                var cfg = ConfigFor(root);
                if (!cfg.TryKeyFor(bookAbsPath, out var key)) continue;
                if (cfg.ClearBook(key) && !cfg.Flush()) Warn(root, cfg.LastError);
            }
            catch (Exception ex) { Warn(root, ex.Message); }
        }
    }

    /// <summary>库根变化（添加/关联库）后丢弃配置缓存，避免继续用旧路径的实例。</summary>
    public void InvalidateCache()
    {
        lock (_gate) _configs.Clear();
    }

    /// <summary>立即落盘所有脏数据（关书/关窗/切库）。</summary>
    public void Flush() => FlushCore();

    /// <summary>有脏数据才落盘（供低频定时器调用）。</summary>
    public void FlushIfDirty() => FlushCore();

    private void FlushCore()
    {
        lock (_gate)
        {
            foreach (var (root, cfg) in _configs)
                if (cfg.IsDirty && !cfg.Flush()) Warn(root, cfg.LastError);
            _lastFlushMs = Environment.TickCount64;
        }
    }

    private void ScheduleFlush()
    {
        if (FlushIntervalMs <= 0) { FlushCore(); return; }
        long now = Environment.TickCount64;
        if (now - _lastFlushMs < FlushIntervalMs) return;
        _lastFlushMs = now;
        _ = Task.Run(() => { try { FlushCore(); } catch { } });   // 后台落盘，不阻塞翻页
    }

    private void Warn(string root, string? error)
    {
        bool first;
        lock (_gate) first = _warnedRoots.Add(root);
        if (!first) return;
        OnWarning?.Invoke($"该库无法写入 progress.json（{error}）：请检查目录只读属性或权限");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { FlushCore(); } catch { }
        try { _db?.Dispose(); } catch { }
        _db = null;
    }

    // ---------- v4.21：一次性迁移（reading.db → 各库 progress.json） ----------

    /// <summary>把 <c>reading.db</c> 里已有的进度/书签搬到各库 <c>progress.json</c>，成功的从 db 删除。
    /// 一次性、幂等：库里已有记录的不覆盖；无法定位/库不可写的保留在 db（读取时会回退使用）。
    /// 返回 (迁移条数, 保留条数)。</summary>
    public static (int Migrated, int Kept) MigrateFromDatabase(string stateDbPath)
    {
        if (!File.Exists(stateDbPath)) return (0, 0);
        int migrated = 0, kept = 0;
        using var db = new ReadingStateStore(stateDbPath);
        var states = db.LoadAllStates();
        var marks = db.LoadAllBookmarks();

        // 按库根分组，尽量只 flush 一次
        var configs = new Dictionary<string, LibraryProgressConfig>(StringComparer.OrdinalIgnoreCase);
        LibraryProgressConfig Cfg(string root)
        {
            if (!configs.TryGetValue(root, out var c)) configs[root] = c = LibraryProgressConfig.Load(root);
            return c;
        }
        var touchedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var migratedStateKeys = new List<(string Key, int Page)>();

        foreach (var s in states)
        {
            var abs = ResolveStoredPath(s.Path, out var root);
            if (abs is null || root is null) { kept++; continue; }
            var cfg = Cfg(root);
            if (!cfg.TryKeyFor(abs, out var key)) { kept++; continue; }
            if (cfg.GetState(key) is not null) { kept++; continue; }   // 库里已有 → 不覆盖
            cfg.TrySetState(key, s.Page,
                Enum.TryParse<ReadingMode>(s.Mode, out var m) ? m : ReadingMode.Single,
                Enum.TryParse<ReadingDirection2>(s.Direction, out var d) ? d : ReadingDirection2.Rtl);
            touchedRoots.Add(root);
        }
        foreach (var b in marks)
        {
            var abs = ResolveStoredPath(b.Path, out var root);
            if (abs is null || root is null) { kept++; continue; }
            var cfg = Cfg(root);
            if (!cfg.TryKeyFor(abs, out var key)) { kept++; continue; }
            var existing = cfg.GetBookmarks(key);
            if (existing.Any(x => x.Page == b.Page)) { kept++; continue; }   // 已存在 → 不覆盖
            cfg.AddBookmark(key, b.Page, b.Note);
            touchedRoots.Add(root);
        }

        // 先落盘，成功后再删 db 行（避免写失败导致数据丢失）
        var flushed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in touchedRoots)
            if (Cfg(root).Flush()) flushed.Add(root);
            else kept += 0;   // 该库写失败：保留 db 行（下方删除时会跳过该根）

        foreach (var s in states)
        {
            var abs = ResolveStoredPath(s.Path, out var root);
            if (abs is null || root is null || !flushed.Contains(root)) continue;
            var cfg = Cfg(root);
            if (!cfg.TryKeyFor(abs, out var key)) continue;
            if (db.DeleteStateByStorageKey(s.Path, s.Page)) migrated++;
            else migrated += 0;
        }
        foreach (var b in marks)
        {
            var abs = ResolveStoredPath(b.Path, out var root);
            if (abs is null || root is null || !flushed.Contains(root)) continue;
            var cfg = Cfg(root);
            if (!cfg.TryKeyFor(abs, out var key)) continue;
            if (db.DeleteBookmarkByStorageKey(b.Path, b.Page)) migrated++;
        }
        return (migrated, kept);
    }

    /// <summary>存储键 → 绝对书路径 + 所属库根（相对键按库根拼、取存在者；绝对键原样）。</summary>
    private static string? ResolveStoredPath(string storedKey, out string? root)
    {
        root = null;
        if (string.IsNullOrEmpty(storedKey)) return null;
        try
        {
            if (Path.IsPathRooted(storedKey))
            {
                if (!File.Exists(storedKey) && !Directory.Exists(storedKey)) return null;
                root = FindRoot(storedKey);
                return storedKey;
            }
        }
        catch { return null; }
        foreach (var r in ReadingStateStore.LibraryRoots.OrderByDescending(x => x.Length))
        {
            string cand;
            try { cand = Path.Combine(r, storedKey.Replace('/', Path.DirectorySeparatorChar)); } catch { continue; }
            if (File.Exists(cand) || Directory.Exists(cand))
            {
                root = FindRoot(cand);
                return cand;
            }
        }
        return null;
    }

    private static string? FindRoot(string absPath)
    {
        try
        {
            var full = Path.GetFullPath(absPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var r in ReadingStateStore.LibraryRoots.OrderByDescending(x => x.Length))
            {
                if (string.IsNullOrEmpty(r)) continue;
                if (full.Equals(r, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
        }
        catch { }
        return null;
    }
}
