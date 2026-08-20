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

    /// <summary>复制到目标目录（文件或文件夹递归）。目标已存在同名 → 追加后缀。</summary>
    public static bool CopyTo(string path, string destDir)
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
            else return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>移动到目标目录（同盘优先 rename，跨盘复制+删除）。</summary>
    public static bool MoveTo(string path, string destDir)
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
                Directory.Move(path, dest);
            }
            else if (File.Exists(path))
            {
                File.Move(path, dest);
            }
            else return false;
            return true;
        }
        catch { return false; }
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
