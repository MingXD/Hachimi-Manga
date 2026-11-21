namespace MangaAgent.Core.Storage;

/// <summary>v4.22：库内配置文件的"备份残留"清理（用户要求：减少程序输出的冗余文件）。
/// v4.22 起正常写入不再产生 `.bak`（改为原子覆盖），本类只用于清理历史遗留与异常备份：
/// `<库根>\*.bak`、`*.bak-*`、`*.broken`、`*.tmp`，以及 `%LOCALAPPDATA%\HachimiManga\legacy-covers-backup-*`。
/// 只删备份/临时文件，绝不动 `covers.json` / `progress.json` 本体。</summary>
public static class LibraryFileCleaner
{
    /// <summary>清理指定库根与数据目录下的备份/临时文件，返回 (删除数, 释放字节数)。</summary>
    public static (int Removed, long FreedBytes) CleanBackups(IEnumerable<string> libraryRoots, string appDataDir)
    {
        int removed = 0;
        long freed = 0;
        foreach (var root in libraryRoots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)))
        {
            foreach (var pattern in new[] { "*.bak", "*.bak-*", "*.broken", "*.tmp" })
            {
                string[] files;
                try { files = Directory.GetFiles(root, pattern); } catch { continue; }
                foreach (var file in files)
                {
                    try
                    {
                        long len = new FileInfo(file).Length;
                        File.Delete(file);
                        removed++;
                        freed += len;
                    }
                    catch { /* 占用/只读：跳过 */ }
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(appDataDir) && Directory.Exists(appDataDir))
        {
            try
            {
                foreach (var file in Directory.GetFiles(appDataDir, "legacy-covers-backup-*"))
                {
                    try
                    {
                        long len = new FileInfo(file).Length;
                        File.Delete(file);
                        removed++;
                        freed += len;
                    }
                    catch { }
                }
            }
            catch { }
        }
        return (removed, freed);
    }
}
