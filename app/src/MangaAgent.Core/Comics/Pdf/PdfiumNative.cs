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
    // 渲染前把位图整体填充为指定色（页面"未绘制区"的背景 —— PDFium 不自动填背景，不填会输出初始化黑值）
    [DllImport("pdfium.dll")] internal static extern void FPDFBitmap_FillRect(IntPtr bitmap,
        int left, int top, int width, int height, uint color);
    [DllImport("pdfium.dll")] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page,
        int startX, int startY, int destWidth, int destHeight, int rotate, int flags);
    [DllImport("pdfium.dll")] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    // 页面对象枚举（裁切模式取"位图本身"边界用；只读使用，无需 formfill 环境）
    [DllImport("pdfium.dll")] internal static extern int FPDFPage_CountObjects(IntPtr page);
    [DllImport("pdfium.dll")] internal static extern IntPtr FPDFPage_GetObject(IntPtr page, int index);
    [DllImport("pdfium.dll")] internal static extern int FPDFPageObj_GetType(IntPtr pageObject);
    [DllImport("pdfium.dll")] internal static extern bool FPDFPageObj_GetBounds(IntPtr pageObject,
        out float left, out float bottom, out float right, out float top);

    // FPDF_PAGEOBJ_*（fpdf_edit.h）：裁切统计 IMAGE(3) 与 FORM(5)
    internal const int FPDF_PAGEOBJ_TEXT = 1;
    internal const int FPDF_PAGEOBJ_IMAGE = 3;
    internal const int FPDF_PAGEOBJ_FORM = 5;

    // FPDF_GetMetaText：键为 UTF-8，buffer 为 UTF-16LE（长度按 UTF-16 计，含结尾 NUL）
    [DllImport("pdfium.dll")] internal static extern int FPDF_GetMetaText(IntPtr doc, IntPtr tag,
        IntPtr buffer, int buflen);

    /// <summary>
    /// 读取文档元数据文本并净化：PDF 空值/脏元数据可能以 NUL（U+0000）、控制字符、Unicode 格式符
    /// （零宽等）形式出现 —— 剔除后 Trim，为空返回 null（BUG-008：空 Title 被当成书名 → 目录显示空白）。
    /// </summary>
    internal static string? GetMetaText(IntPtr doc, string key)
    {
        IntPtr keyPtr = Marshal.StringToCoTaskMemUTF8(key);
        try
        {
            // FPDF_GetMetaText：返回值为所需 buffer 的【字节数】（含结尾 NUL，UTF-16LE）；buflen 同为字节单位
            int lenBytes = FPDF_GetMetaText(doc, keyPtr, IntPtr.Zero, 0);
            if (lenBytes <= 2) return null;   // 空（仅 NUL 或不存在）
            IntPtr buf = Marshal.AllocCoTaskMem(lenBytes);
            try
            {
                if (FPDF_GetMetaText(doc, keyPtr, buf, lenBytes) <= 0) return null;
                string? raw = Marshal.PtrToStringUni(buf, lenBytes / 2);   // 字节 → UTF-16 字符数
                if (raw is null) return null;
                // 剔除 C0/C1 控制字符（含 NUL）与 Unicode 格式符（Cf：零宽空格/方向符等），Trim 后为空返回 null
                var cleaned = new System.Text.StringBuilder(raw.Length);
                foreach (var ch in raw)
                {
                    if (ch == '\0' || ch < 0x20 || (ch >= 0x7F && ch < 0xA0)
                        || char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.Format)
                        continue;
                    cleaned.Append(ch);
                }
                string result = cleaned.ToString().Trim();
                return result.Length == 0 ? null : result;
            }
            finally { Marshal.FreeCoTaskMem(buf); }
        }
        finally { Marshal.FreeCoTaskMem(keyPtr); }
    }
}
