using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MangaAgent.Core.Library;

/// <summary>文件操作：删除(回收站)/重命名/在资源管理器显示。</summary>
public static class FileOps
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;   // 允许撤销 → 进回收站
    private const ushort FOF_NOCONFIRMATION = 0x10;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <summary>删除到回收站（可恢复）。</summary>
    public static bool DeleteToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION),
        };
        return SHFileOperation(ref op) == 0;
    }

    /// <summary>重命名（文件或文件夹）。</summary>
    public static void Rename(string path, string newName)
    {
        string dir = Path.GetDirectoryName(path)!;
        string newPath = Path.Combine(dir, newName);
        if (Directory.Exists(path)) Directory.Move(path, newPath);
        else File.Move(path, newPath);
    }

    /// <summary>在资源管理器中显示。</summary>
    public static void ShowInExplorer(string path)
    {
        if (File.Exists(path) || Directory.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    /// <summary>复制到目标目录（文件或文件夹递归）。目标已存在同名 → 追加后缀。返回 (成功, 错误信息)。</summary>
    public static (bool Ok, string? Error) CopyTo(string path, string destDir)
    {
        try
        {
            Directory.CreateDirectory(destDir);
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = new DirectoryInfo(path).Name;
            var dest = Path.Combine(destDir, name);
            dest = UniquePath(dest);
            if (Directory.Exists(path))
            {
                CopyDirectory(path, dest);
            }
            else if (File.Exists(path))
            {
                File.Copy(path, dest);
            }
            else return (false, "源路径不存在");
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>移动到目标目录：同卷优先 Move；跨卷回退"复制+删除源"（复制成功才删源，防数据丢失）。
    /// v3 #3 修复：原实现 File.Move/Directory.Move 跨卷必抛 IOException 且被静默吞掉 → 用户看到"没反应"；
    /// 现返回错误信息、处理源=目标/子目录等异常。</summary>
    public static (bool Ok, string? Error) MoveTo(string path, string destDir)
    {
        try
        {
            Directory.CreateDirectory(destDir);
            // v3 #3：目标目录就是源所在目录 → 拒绝（否则 UniquePath 会把"移动到自己目录"意外变成重命名 (2)）
            if ((Directory.Exists(path) || File.Exists(path)) &&
                string.Equals(Path.GetDirectoryName(Path.GetFullPath(path.TrimEnd('\\'))),
                    Path.GetFullPath(destDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return (false, "源与目标目录相同");
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = new DirectoryInfo(path).Name;
            var dest = Path.Combine(destDir, name);
            dest = UniquePath(dest);
            if (Directory.Exists(path))
            {
                if (Path.GetFullPath(path).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    return (false, "源与目标相同");
                if (IsSubPath(dest, path))
                    return (false, "目标不能是源文件夹的子目录");
                if (SameVolume(path, dest)) Directory.Move(path, dest);
                else { CopyDirectory(path, dest); Directory.Delete(path, true); }
            }
            else if (File.Exists(path))
            {
                if (Path.GetFullPath(path).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    return (false, "源与目标相同");
                if (SameVolume(path, dest)) File.Move(path, dest);
                else { File.Copy(path, dest); File.Delete(path); }
            }
            else return (false, "源路径不存在");
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>递归枚举目录下所有文件（进度统计用）。</summary>
    private static List<string> EnumerateFilesRecursive(string dir)
    {
        var list = new List<string>();
        foreach (var f in Directory.GetFiles(dir)) list.Add(f);
        foreach (var d in Directory.GetDirectories(dir)) list.AddRange(EnumerateFilesRecursive(d));
        return list;
    }

    /// <summary>异步复制（带进度，v3 #1）：先枚举源文件总数，逐个复制并报告 (done,total)。
    /// 同步语义与 CopyTo 一致（UniquePath 防覆盖、错误返回）；供 UI 显示"正在复制 x/y…"。</summary>
    public static async Task<(bool Ok, string? Error)> CopyToAsync(string path, string destDir,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(destDir);
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = new DirectoryInfo(path).Name;
            var dest = Path.Combine(destDir, name);
            dest = UniquePath(dest);
            await Task.Yield();   // 让出一次（调用方通常已 Task.Run；此处避免同步路径完全占住调用线程）
            if (Directory.Exists(path))
            {
                var files = EnumerateFilesRecursive(path);
                int total = files.Count, done = 0;
                foreach (var f in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var rel = Path.GetRelativePath(path, f);
                    var target = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(f, target, true);
                    progress?.Report((++done, total));
                }
            }
            else if (File.Exists(path))
            {
                File.Copy(path, dest);
                progress?.Report((1, 1));
            }
            else return (false, "源路径不存在");
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    /// <summary>异步移动（带进度，v3 #1）：同卷 Move（秒级，无进度）；跨卷 = CopyToAsync 进度复制 + 成功后删源（防数据丢失）。
    /// v4.12：返回实际目标路径 TargetPath（同卷/跨卷、重名 UniquePath 后均准确），供调用方迁移关联数据（如文件夹封面记录）。</summary>
    public static async Task<(bool Ok, string? Error, string? TargetPath)> MoveToAsync(string path, string destDir,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(destDir);
            // v3 #3：目标目录就是源所在目录 → 拒绝（否则 UniquePath 会把"移动到自己目录"意外变成重命名 (2)）
            if ((Directory.Exists(path) || File.Exists(path)) &&
                string.Equals(Path.GetDirectoryName(Path.GetFullPath(path.TrimEnd('\\'))),
                    Path.GetFullPath(destDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return (false, "源与目标目录相同", null);
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name)) name = new DirectoryInfo(path).Name;
            var dest = Path.Combine(destDir, name);
            dest = UniquePath(dest);
            if (Directory.Exists(path))
            {
                if (Path.GetFullPath(path).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    return (false, "源与目标相同", null);
                if (IsSubPath(dest, path))
                    return (false, "目标不能是源文件夹的子目录", null);
                if (SameVolume(path, dest))
                {
                    Directory.Move(path, dest);
                }
                else
                {
                    var (ok, err) = await CopyToAsync(path, destDir, progress, ct);   // 同输入 → 同 UniquePath 结果
                    if (!ok) return (false, err, null);
                    Directory.Delete(path, true);
                }
            }
            else if (File.Exists(path))
            {
                if (Path.GetFullPath(path).Equals(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                    return (false, "源与目标相同", null);
                if (SameVolume(path, dest))
                {
                    File.Move(path, dest);
                }
                else
                {
                    File.Copy(path, dest);
                    progress?.Report((1, 1));
                    File.Delete(path);
                }
            }
            else return (false, "源路径不存在", null);
            return (true, null, dest);
        }
        catch (Exception ex) { return (false, ex.Message, null); }
    }

    /// <summary>同卷判断（跨卷时 File.Move/Directory.Move 不支持，须回退复制+删源）。</summary>
    public static bool SameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>child 是否位于 parent 目录之内（含同级子路径判定）。</summary>
    private static bool IsSubPath(string child, string parent)
    {
        var fullChild = Path.GetFullPath(child).TrimEnd('\\') + "\\";
        var fullParent = Path.GetFullPath(parent).TrimEnd('\\') + "\\";
        return fullChild.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniquePath(string dest)
    {
        if (!Directory.Exists(dest) && !File.Exists(dest)) return dest;
        string dir = Path.GetDirectoryName(dest)!;
        string name = Path.GetFileNameWithoutExtension(dest);
        string ext = Path.GetExtension(dest);
        for (int i = 2; i < 999; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        return dest;
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        foreach (var sub in Directory.GetDirectories(sourceDir))
            CopyDirectory(sub, Path.Combine(destDir, Path.GetFileName(sub)));
    }
}
