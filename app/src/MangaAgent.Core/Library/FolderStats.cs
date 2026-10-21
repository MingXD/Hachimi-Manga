namespace MangaAgent.Core.Library;

/// <summary>v4.15：文件夹递归统计（大小/文件数/子文件夹数）。供"属性"对话框后台计算用。
/// 逐项容错：无法访问/瞬时删除的目录或文件计为 Skipped 并继续；支持取消。</summary>
public static class FolderStats
{
    /// <summary>统计结果（TotalBytes 累加、Files 文件数、Folders 子文件夹数、Skipped 跳过项数）。</summary>
    public readonly record struct Result(long TotalBytes, int Files, int Folders, int Skipped);

    /// <summary>递归统计文件夹（后台线程；progress 回调在计算线程触发，节流交给调用方）。
    /// 注意：单目录内文件极多时也会按 2000 文件节流报告一次。</summary>
    public static Task<Result> ComputeAsync(string path, IProgress<Result>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            long bytes = 0;
            int files = 0, folders = 0, skipped = 0, sinceReport = 0;
            var stack = new Stack<string>();
            stack.Push(path);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(dir))
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            bytes += new FileInfo(f).Length;
                            files++;
                        }
                        catch
                        {
                            skipped++;
                        }
                        if (++sinceReport >= 2000)
                        {
                            sinceReport = 0;
                            progress?.Report(new Result(bytes, files, folders, skipped));
                        }
                    }
                    foreach (var d in Directory.EnumerateDirectories(dir))
                    {
                        ct.ThrowIfCancellationRequested();
                        folders++;
                        stack.Push(d);
                    }
                }
                catch
                {
                    skipped++;   // 目录整体无法访问：跳过继续
                }
                progress?.Report(new Result(bytes, files, folders, skipped));
            }
            return new Result(bytes, files, folders, skipped);
        }, ct);
    }

    /// <summary>字节数 → 人类可读（1024 进制；&lt;1KB 显示 B）。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return $"{bytes} B";
        double v = bytes;
        string[] units = { "KB", "MB", "GB", "TB", "PB" };
        int u = -1;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return $"{v:0.#} {units[u]}";
    }
}
