using System.Runtime.InteropServices;

namespace MangaAgent.Core.Comics.Pdf;

/// <summary>PDF 打开失败类别（驱动 PdfComicSource 的异常与用户提示）。</summary>
public enum PdfErrorKind
{
    /// <summary>找不到 pdfium.dll（未随程序分发）。</summary>
    NativeMissing,
    /// <summary>文件需要密码。</summary>
    Encrypted,
    /// <summary>文件损坏或不是 PDF。</summary>
    Corrupt,
    /// <summary>其他（安全策略、文件 IO 等）。</summary>
    Other,
}

/// <summary>PDF 打开/渲染失败的明确异常（调用方据此提示，不静默）。</summary>
public sealed class PdfException : Exception
{
    public PdfErrorKind Kind { get; }

    public PdfException(PdfErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;

    public PdfException(PdfErrorKind kind, string message, int nativeError, Exception? inner = null)
        : base($"{message} (pdfium error {nativeError})", inner) => Kind = kind;
}

/// <summary>
/// pdfium.dll 定位与运行期探测。加载策略：不显式 LoadLibrary —— dll 经 Directory.Build.targets
/// 复制到各项目输出目录（AppContext.BaseDirectory 在标准搜索路径内），DllImport 自动命中；
/// 数据目录 %LOCALAPPDATA%\HachimiManga\pdfium\ 兜底（用户自备/绿色版升级不覆盖）需要显式 LoadLibrary。
/// </summary>
public static class PdfiumLibrary
{
    private const string DllName = "pdfium.dll";

    private static readonly bool s_available;

    static PdfiumLibrary()
    {
        bool ok = false;
        try
        {
            // 1) BaseDirectory（构建/发布输出，自动在 DllImport 搜索路径内）
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, DllName))) ok = true;
            else
            {
                // 2) 数据目录兜底
                var dll = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HachimiManga", "pdfium", DllName);
                if (File.Exists(dll))
                    ok = NativeMethods.LoadLibraryW(dll) != IntPtr.Zero;
            }
        }
        catch
        {
            ok = false;
        }
        if (ok)
        {
            Fpdf.FPDF_InitLibrary();   // 进程级一次性
            s_available = true;
        }
    }

    /// <summary>pdfium.dll 是否可用（随包分发或数据目录存在）。</summary>
    public static bool IsAvailable => s_available;

    /// <summary>找不到 pdfium.dll 时抛出可读的 PdfException（供扫描跳过/打开提示）。</summary>
    public static void EnsureAvailable()
    {
        if (!s_available)
            throw new PdfException(PdfErrorKind.NativeMissing,
                "未找到 pdfium.dll：PDF 渲染组件缺失。请将 pdfium.dll 放到程序目录或 %LOCALAPPDATA%\\HachimiManga\\pdfium\\。");
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadLibraryW(string path);
    }
}

/// <summary>PDFium FPDF_* 原生接口的 P/Invoke 薄封装（常量与签名核对自 pdfium 154 include/fpdfview.h）。
/// 调用纪律：所有 FPDF 调用须在调用方串行化（PdfComicSource._gate）；渲染为 CPU 密集操作，放线程池执行。</summary>
internal static class Fpdf
{
    // FPDF_BITMAP_FORMAT：UNKNOWN=0 GRAY=1 BGR=2 BGRx=3 BGRA=4
    internal const int FPDFBitmap_BGRA = 4;

    // 渲染标志：内容 + 注解。禁用 FPDF_LCD_TEXT(0x08)——实测会把整页输出灰度化
    internal const int RenderFlags = 0x01 /* FPDF_ANNOT */;

    // FPDF_GetLastError 值
    internal const int FPDF_ERR_SUCCESS = 0;
    internal const int FPDF_ERR_PASSWORD = 4;

    [DllImport("pdfium.dll")] internal static extern void FPDF_InitLibrary();
    [DllImport("pdfium.dll")] internal static extern void FPDF_DestroyLibrary();
    [DllImport("pdfium.dll", EntryPoint = "FPDF_LoadDocument")]
    internal static extern IntPtr FPDF_LoadDocumentUtf8([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);
    [DllImport("pdfium.dll")] internal static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport("pdfium.dll")] internal static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport("pdfium.dll")] internal static extern void FPDF_ClosePage(IntPtr page);
    [DllImport("pdfium.dll")] internal static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport("pdfium.dll")] internal static extern int FPDF_GetLastError();
    [DllImport("pdfium.dll")] internal static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport("pdfium.dll")] internal static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport("pdfium.dll")] internal static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format,
        IntPtr firstScanline, int stride);
    [DllImport("pdfium.dll")] internal static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport("pdfium.dll")] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page,
        int startX, int startY, int destWidth, int destHeight, int rotate, int flags);
    [DllImport("pdfium.dll")] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    // FPDF_GetMetaText：键为 UTF-8，buffer 为 UTF-16LE（长度按 UTF-16 计，含结尾 NUL）
    [DllImport("pdfium.dll")] internal static extern int FPDF_GetMetaText(IntPtr doc, IntPtr tag,
        IntPtr buffer, int buflen);

    /// <summary>读取文档元数据文本（无此键返回 null）。</summary>
    internal static string? GetMetaText(IntPtr doc, string key)
    {
        IntPtr keyPtr = Marshal.StringToCoTaskMemUTF8(key);
        try
        {
            int len = FPDF_GetMetaText(doc, keyPtr, IntPtr.Zero, 0);
            if (len <= 1) return null;
            IntPtr buf = Marshal.AllocCoTaskMem(len * 2);
            try
            {
                if (FPDF_GetMetaText(doc, keyPtr, buf, len) <= 0) return null;
                return Marshal.PtrToStringUni(buf, len - 1);
            }
            finally { Marshal.FreeCoTaskMem(buf); }
        }
        finally { Marshal.FreeCoTaskMem(keyPtr); }
    }
}
