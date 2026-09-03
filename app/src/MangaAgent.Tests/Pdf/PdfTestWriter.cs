using System.Globalization;
using System.Text;

namespace MangaAgent.Tests.Pdf;

/// <summary>测试用最小合法 PDF 生成器（纯 BCL，无第三方依赖）：
/// N 页矢量 PDF（页尺寸参数化；每页顶部一个按 i%3 循环的主色块），用于 PdfComicSource 单测与阅读器 E2E。
/// 结构：Catalog + Pages + 共享 Font + 每页 [Page, Contents 流]；经典 xref 表（PDFium 实测可解析）。
/// 参考实现：vendor/pdfium/smoke/SmokeLib.cs 的 MiniPdf（P0 已像素级验证）。</summary>
public static class PdfTestWriter
{
    public static string WriteFile(string dir, string name, int pageCount, int widthPt = 600, int heightPt = 800,
        string? infoTitle = null)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        byte[] pdf = Build(pageCount, widthPt, heightPt, infoTitle);
        File.WriteAllBytes(path, pdf);
        return path;
    }

    public static byte[] Build(int pageCount, int widthPt, int heightPt, string? infoTitle = null)
    {
        if (pageCount <= 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        int pageObjStart = 3;                       // 1 Catalog, 2 Pages, 3 Font
        int baseObjs = pageObjStart + pageCount * 2;
        bool hasInfo = infoTitle is not null;
        int infoObj = hasInfo ? baseObjs + 1 : 0;
        int totalObjs = hasInfo ? baseObjs + 1 : baseObjs;

        var streamObjs = new List<(string Dict, string Body)>();
        // 每页内容流：底色 + 顶部主色块 + 页号文字
        for (int i = 0; i < pageCount; i++)
        {
            var (r, g, b) = ((i % 3) switch { 0 => (1f, 0.1f, 0.1f), 1 => (0.1f, 1f, 0.1f), _ => (0.1f, 0.1f, 1f) });
            var sb = new StringBuilder();
            sb.Append("0.95 0.95 0.95 rg 0 0 ").Append(Pt(widthPt)).Append(' ').Append(Pt(heightPt)).Append(" re f\n");
            // 顶部色块（y 上坐标系）：y = H-60..H-20
            sb.Append(N(r)).Append(' ').Append(N(g)).Append(' ').Append(N(b)).Append(" rg 10 ")
              .Append(Pt(heightPt - 60)).Append(' ').Append(Pt(widthPt - 20)).Append(" 40 re f\n");
            sb.Append("0 0 0 rg BT /F1 20 Tf 20 ").Append(Pt(heightPt - 130))
              .Append(" Td (Page ").Append(i + 1).Append(" of ").Append(pageCount).Append(") Tj ET\n");
            streamObjs.Add((" /Length " + sb.Length, sb.ToString()));
        }

        using var ms = new MemoryStream();
        void Ascii(string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }
        Ascii("%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n");   // 二进制注释行（按 ASCII 写字节）

        var offsets = new long[totalObjs + 1];
        void Emit(int n, byte[] body)
        {
            offsets[n] = ms.Position;
            Ascii(n + " 0 obj\n");
            ms.Write(body, 0, body.Length);
            Ascii("\nendobj\n");
        }

        var kids = string.Join(" ", Enumerable.Range(0, pageCount)
            .Select(i => (pageObjStart + i * 2) + " 0 R"));
        Emit(1, Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        Emit(2, Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [" + kids + "] /Count " + pageCount + " >>"));
        Emit(3, Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));

        for (int i = 0; i < pageCount; i++)
        {
            int pageObj = pageObjStart + i * 2;
            int contentObj = pageObj + 1;
            var (dict, body) = streamObjs[i];
            Emit(pageObj, Encoding.ASCII.GetBytes(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + widthPt + " " + heightPt + "]"
                + " /Resources << /Font << /F1 3 0 R >> >> /Contents " + contentObj + " 0 R >>"));
            byte[] bodyBytes = Encoding.ASCII.GetBytes(body);
            var head = Encoding.ASCII.GetBytes("<< /Length " + bodyBytes.Length + " >>\nstream\n");
            var tail = Encoding.ASCII.GetBytes("\nendstream");
            var full = new byte[head.Length + bodyBytes.Length + tail.Length];
            Buffer.BlockCopy(head, 0, full, 0, head.Length);
            Buffer.BlockCopy(bodyBytes, 0, full, head.Length, bodyBytes.Length);
            Buffer.BlockCopy(tail, 0, full, head.Length + bodyBytes.Length, tail.Length);
            Emit(contentObj, full);
        }
        // 可选 /Info（脏元数据测试）：测试值不含 ()\ 等需转义字符
        if (hasInfo)
            Emit(infoObj, Encoding.ASCII.GetBytes("<< /Title (" + infoTitle + ") >>"));

        long xref = ms.Position;
        Ascii("xref\n0 " + (totalObjs + 1) + "\n");
        Ascii("0000000000 65535 f \r\n");
        for (int n = 1; n <= totalObjs; n++)
            Ascii(offsets[n].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \r\n");
        string trailer = "<< /Size " + (totalObjs + 1) + " /Root 1 0 R";
        if (hasInfo) trailer += " /Info " + infoObj + " 0 R";
        Ascii("trailer\n" + trailer + " >>\nstartxref\n" + xref + "\n%%EOF\n");
        return ms.ToArray();
    }

    private static string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Pt(int v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>生成"位图不满 MediaBox、无全幅背景矩形"的真实扫描 PDF 形态：
    /// 每页 = 一张 FlateDecode RGB 位图（左半红/右半蓝，1px→1pt）居中绘制在纸面，四周留 marginPt 空区（无背景）。
    /// 用途：BUG-009 渲染背景填白、裁切模式（位图本身）的单测样本。</summary>
    public static string WriteEmbeddedImageFile(string dir, string name, int pageCount,
        int widthPt = 600, int heightPt = 800, float marginPt = 24f, bool useFlate = true)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, BuildEmbeddedImage(pageCount, widthPt, heightPt, marginPt, useFlate));
        return path;
    }

    public static byte[] BuildEmbeddedImage(int pageCount, int widthPt, int heightPt, float marginPt,
        bool useFlate = true)
    {
        if (pageCount <= 0) throw new ArgumentOutOfRangeException(nameof(pageCount));
        int imgW = Math.Max(1, (int)Math.Round(widthPt - marginPt * 2));
        int imgH = Math.Max(1, (int)Math.Round(heightPt - marginPt * 2));

        // 图像像素：左半红右半蓝（便于裁切边界像素断言）
        byte[] imgData;
        {
            var row = new byte[imgW * 3];
            for (int x = 0; x < imgW; x++)
            {
                row[x * 3] = x < imgW / 2 ? (byte)255 : (byte)0;
                row[x * 3 + 1] = 0;
                row[x * 3 + 2] = x < imgW / 2 ? (byte)0 : (byte)255;
            }
            if (useFlate)
            {
                using var px = new MemoryStream();
                using (var zs = new System.IO.Compression.ZLibStream(px, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                    for (int y = 0; y < imgH; y++) zs.Write(row, 0, row.Length);
                imgData = px.ToArray();
            }
            else
            {
                using var px = new MemoryStream();
                for (int y = 0; y < imgH; y++) px.Write(row, 0, row.Length);
                imgData = px.ToArray();
            }
        }

            int totalObjs = 3 + pageCount * 2;   // 1 Cat, 2 Pages, 3 Image, 每页 [Page, Contents]
            using var ms = new MemoryStream();
            void Ascii(string s)
            {
                byte[] b = Encoding.ASCII.GetBytes(s);
                ms.Write(b, 0, b.Length);
            }
            Ascii("%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n");
            long[] off = new long[totalObjs + 1];
            void Emit(int n, byte[] body)
            {
                off[n] = ms.Position;
                Ascii(n + " 0 obj\n");
                ms.Write(body, 0, body.Length);
                Ascii("\nendobj\n");
            }
            var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => (4 + i * 2) + " 0 R"));
            Emit(1, Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
            Emit(2, Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [" + kids + "] /Count " + pageCount + " >>"));
            // 共享图像对象
            string filterTag = useFlate ? " /Filter /FlateDecode" : "";
            string imgHead = "<< /Type /XObject /Subtype /Image /Width " + imgW + " /Height " + imgH
                + " /ColorSpace /DeviceRGB /BitsPerComponent 8" + filterTag + " /Length " + imgData.Length + " >>\nstream\n";
            var imgBytes = BuildStream(imgHead, imgData);
            Emit(3, imgBytes);
            // 每页：Page 字典 + Contents（绘制图像于 (marginPt, marginPt)，显示尺寸 = cm 的宽高分量：
            // PDF 图像显示区域由 CTM 决定（QuickPDF/MiniPdf 同型），1×1 cm 会把图压成 1pt）
            string margin = marginPt.ToString("0.###", CultureInfo.InvariantCulture);
            for (int i = 0; i < pageCount; i++)
            {
                int pageObj = 4 + i * 2;
                int contentObj = pageObj + 1;
                Emit(pageObj, Encoding.ASCII.GetBytes(
                    "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + widthPt + " " + heightPt + "]"
                    + " /Resources << /XObject << /Im0 3 0 R >> >> /Contents " + contentObj + " 0 R >>"));
                string content = "q " + imgW + " 0 0 " + imgH + " " + margin + " " + margin + " cm /Im0 Do Q\n";
                byte[] contentBytes = Encoding.ASCII.GetBytes(content);
                Emit(contentObj, BuildStream("<< /Length " + contentBytes.Length + " >>\nstream\n", contentBytes));
            }
            long xref = ms.Position;
            Ascii("xref\n0 " + (totalObjs + 1) + "\n");
            Ascii("0000000000 65535 f \r\n");
            for (int n = 1; n <= totalObjs; n++)
                Ascii(off[n].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \r\n");
            Ascii("trailer\n<< /Size " + (totalObjs + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            return ms.ToArray();
    }

    private static byte[] BuildStream(string head, byte[] data)
    {
        byte[] h = Encoding.ASCII.GetBytes(head);
        byte[] t = Encoding.ASCII.GetBytes("\nendstream");
        byte[] full = new byte[h.Length + data.Length + t.Length];
        Buffer.BlockCopy(h, 0, full, 0, h.Length);
        Buffer.BlockCopy(data, 0, full, h.Length, data.Length);
        Buffer.BlockCopy(t, 0, full, h.Length + data.Length, t.Length);
        return full;
    }
}
