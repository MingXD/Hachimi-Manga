namespace MangaAgent.Core.Storage;

/// <summary>v4.21：阅读状态存储抽象。实现：
/// ①<see cref="LibraryStateService"/>（正常路径：库内 progress.json，随库走）
/// ②<see cref="ReadingStateStore"/>（回退：不属于任何库的书 —— 仍在 reading.db）。
/// ReaderViewModel 只依赖本接口。</summary>
public interface ILibraryStateStore
{
    (int LogicalPage, ReadingMode Mode, ReadingDirection2 Direction)? LoadState(string bookPath);
    void SaveState(string bookPath, int logicalPage, ReadingMode mode, ReadingDirection2 direction);
    List<int> LoadBookmarks(string bookPath);
    IReadOnlyList<BookmarkEntryInfo> LoadBookmarkEntries(string bookPath);
    void AddBookmark(string bookPath, int page, string? note = null);
    void RemoveBookmark(string bookPath, int page);
    /// <summary>v4.22：清空某本书的全部书签（不动阅读进度）。</summary>
    void ClearBookmarks(string bookPath);
    /// <summary>立即落盘（关书/关窗/切库时调用；平时由节流写入）。</summary>
    void Flush();
}
