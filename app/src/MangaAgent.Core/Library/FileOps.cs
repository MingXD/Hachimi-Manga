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
}
