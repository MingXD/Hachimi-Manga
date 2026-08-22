using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Xunit;

namespace MangaAgent.Tests.PoC;

/// <summary>诊断（临时）：验证哪种坏数据能让 Bitmap.DecodeToWidth 失败。</summary>
public class DecodeFailProbeTests
{
    [AvaloniaFact]
    public void Probe()
    {
        var sb = new StringBuilder();
        // 1) 纯垃圾字符串
        var garbage = Encoding.UTF8.GetBytes("THIS IS NOT A REAL IMAGE - corrupt page");
        Bitmap? g = null; string? ge = null;
        try { using var ms = new MemoryStream(garbage); g = Bitmap.DecodeToWidth(ms, 200); }
        catch (Exception ex) { ge = ex.GetType().Name; }
        sb.AppendLine($"garbage-string: bmp={g != null} size={(g?.PixelSize.ToString() ?? "-")} err={ge ?? "-"}");
        g?.Dispose();
        // 2) 截断的合法 PNG（签名有效 + 数据截断）
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var truncated = png[..20];
        Bitmap? t = null; string? te = null;
        try { using var ms = new MemoryStream(truncated); t = Bitmap.DecodeToWidth(ms, 200); }
        catch (Exception ex) { te = ex.GetType().Name; }
        sb.AppendLine($"truncated-png: bmp={t != null} size={(t?.PixelSize.ToString() ?? "-")} err={te ?? "-"}");
        t?.Dispose();
        // 3) 完整合法 PNG
        Bitmap? ok = null; string? oe = null;
        try { using var ms = new MemoryStream(png); ok = Bitmap.DecodeToWidth(ms, 200); }
        catch (Exception ex) { oe = ex.GetType().Name; }
        sb.AppendLine($"valid-png: bmp={ok != null} size={(ok?.PixelSize.ToString() ?? "-")} err={oe ?? "-"}");
        ok?.Dispose();
        File.WriteAllText(@"D:\MangaAgent\app\diag-garbage.txt", sb.ToString());
    }
}
