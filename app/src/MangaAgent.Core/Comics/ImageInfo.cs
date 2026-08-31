using System.Text.RegularExpressions;

namespace MangaAgent.Core.Comics;

/// <summary>从文件头解析图片尺寸（仅 PNG/JPEG，避免完整解码）。</summary>
public static class ImageInfo
{
    private static readonly Regex s_natural = new(@"\d+", RegexOptions.Compiled);

    /// <summary>自然排序比较（manga 页面 1,2,...,10 而非 1,10,2）。</summary>
    public static int CompareNatural(string a, string b)
    {
        var ma = s_natural.Matches(a);
        var mb = s_natural.Matches(b);
        int ia = 0, ib = 0;
        int posA = 0, posB = 0;
        while (ia < ma.Count && ib < mb.Count)
        {
            var na = ma[ia]; var nb = mb[ib];
            var preA = a.Substring(posA, na.Index - posA);
            var preB = b.Substring(posB, nb.Index - posB);
            int cmp = string.Compare(preA, preB, StringComparison.OrdinalIgnoreCase);
            if (cmp != 0) return cmp;
            cmp = CompareNumeric(na.Value, nb.Value);
            if (cmp != 0) return cmp;
            posA = na.Index + na.Length; posB = nb.Index + nb.Length;
            ia++; ib++;
        }
        // 一方数字块耗尽：剩余还有数字的一方排前（数字优先，与 Windows 资源管理器自然排序一致）。
        // 修复（v3 #14）：原实现两个分支方向写反（return 1 / return -1 互换前），
        // 导致"xy" vs "_1" 两个方向都返回 -1 → 比较器违反反对称性 → List.Sort 结果不可预测，
        // 字母命名的后记页（如 xy）常排到数字正文页（_1、0002…）之前成为封面。
        if (ia < ma.Count) return -1;   // a 还有数字块 → a 排前
        if (ib < mb.Count) return 1;    // b 还有数字块 → b 排前
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>数字串按数值比较（溢出安全，v3 修复）：去前导零后比长度，同长逐位比。
    /// 原实现 long.Parse 遇超长数字串（&gt;long.MaxValue，如文件名含 32 位数字）抛 OverflowException →
    /// List.Sort 报 "Failed to compare two elements in the array" → 整个目录树扫描失败。</summary>
    private static int CompareNumeric(string x, string y)
    {
        string a = x.TrimStart('0');
        string b = y.TrimStart('0');
        // 全零视为 "0"
        if (a.Length == 0) a = "0";
        if (b.Length == 0) b = "0";
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        return string.CompareOrdinal(a, b);
    }

    /// <summary>从文件头解析图片尺寸（PNG/JPEG/GIF/BMP/WebP，避免完整解码）。
    /// v3 重写：JPEG 改按段结构解析（原逐字节扫描会被 EXIF 内嵌缩略图的 0xFFC0 误匹配 → 返回缩略图尺寸）；新增 GIF/BMP/WebP。</summary>
    public static PageSize? ParseHeader(ReadOnlySpan<byte> head)
    {
        if (head.Length < 4) return null;
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)   // PNG
        {
            if (head.Length >= 24)
            {
                int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return new PageSize(w, h);
            }
            return null;
        }
        if (head[0] == 0xFF && head[1] == 0xD8) return ParseJpeg(head);                 // JPEG
        if (head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8'
            && (head[4] == '7' || head[4] == '9') && head[5] == 'a') return ParseGif(head);
        if (head[0] == 'B' && head[1] == 'M') return ParseBmp(head);
        if (head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F'
            && head.Length >= 12 && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P')
            return ParseWebp(head);
        return null;
    }

    /// <summary>JPEG：按段结构从 SOI 逐段解析——普通段（APP/DQT/DHT/COM）读长度跳过，真正 SOF 段读宽高，SOS 停止。
    /// 原逐字节扫描会被 APP1/EXIF 内嵌缩略图的 0xFFC0 误匹配 → 返回缩略图尺寸（v3 模糊根因）。</summary>
    private static PageSize? ParseJpeg(ReadOnlySpan<byte> head)
    {
        int i = 2;   // 跳过 SOI
        while (i + 9 < head.Length)
        {
            if (head[i] != 0xFF) { i++; continue; }   // 段间填充
            byte marker = head[i + 1];
            if (marker == 0xFF) { i += 2; continue; }
            if (marker == 0xDA) return null;          // SOS：图像数据开始，SOF 必在其前
            if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9)) { i += 2; continue; }  // 无长度段
            if (i + 4 > head.Length) return null;
            int segLen = (head[i + 2] << 8) | head[i + 3];
            if (segLen < 2) return null;
            // SOF 段（含精度/高/宽）
            if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
            {
                if (segLen >= 7 && i + 9 < head.Length)
                {
                    int h = (head[i + 5] << 8) | head[i + 6];
                    int w = (head[i + 7] << 8) | head[i + 8];
                    return new PageSize(w, h);
                }
                return null;
            }
            i += 2 + segLen;
        }
        return null;
    }

    private static PageSize? ParseGif(ReadOnlySpan<byte> head)
    {
        if (head.Length < 10) return null;
        int w = head[6] | (head[7] << 8);
        int h = head[8] | (head[9] << 8);
        return w > 0 && h > 0 ? new PageSize(w, h) : null;
    }

    private static PageSize? ParseBmp(ReadOnlySpan<byte> head)
    {
        if (head.Length < 26) return null;
        int dibSize = head[14] | (head[15] << 8) | (head[16] << 16) | (head[17] << 24);
        int w, hRaw;
        if (dibSize == 12)   // BITMAPCOREHEADER (OS/2)：宽高各 2 字节
        {
            w = head[18] | (head[19] << 8);
            hRaw = head[20] | (head[21] << 8);
        }
        else
        {
            w = head[18] | (head[19] << 8) | (head[20] << 16) | (head[21] << 24);
            hRaw = head[22] | (head[23] << 8) | (head[24] << 16) | (head[25] << 24);
        }
        int h = Math.Abs(hRaw);   // BMP 高度可负（自顶向下）
        return w > 0 && h > 0 ? new PageSize(w, h) : null;
    }

    /// <summary>WebP：RIFF/WEBP 容器内找 VP8（有损）/VP8L（无损）/VP8X（扩展）块解析宽高。</summary>
    private static PageSize? ParseWebp(ReadOnlySpan<byte> head)
    {
        int p = 12;
        while (p + 8 <= head.Length)
        {
            if (p + 4 > head.Length) return null;
            var type = new string(new[]
            {
                (char)head[p], (char)head[p + 1], (char)head[p + 2], (char)head[p + 3],
            });
            int size = head[p + 4] | (head[p + 5] << 8) | (head[p + 6] << 16) | (head[p + 7] << 24);
            int dataStart = p + 8;
            if (type == "VP8 " && dataStart + 10 <= head.Length)        // 有损：3 字节 start code + 3 字节尺寸
            {
                int w = (head[dataStart + 3] | ((head[dataStart + 4] & 0x3F) << 8)) + 1;
                int h = (((head[dataStart + 4] >> 6) | (head[dataStart + 5] << 2)
                    | ((head[dataStart + 6] & 0x0F) << 10)) & 0x3FFF) + 1;
                return new PageSize(w, h);
            }
            if (type == "VP8L" && dataStart + 5 <= head.Length)         // 无损：0x2F + 14bit 宽高
            {
                int w = (head[dataStart + 1] | ((head[dataStart + 2] & 0x3F) << 8)) + 1;
                int h = (((head[dataStart + 2] >> 6) | (head[dataStart + 3] << 2)
                    | ((head[dataStart + 4] & 0x0F) << 10)) & 0x3FFF) + 1;
                return new PageSize(w, h);
            }
            if (type == "VP8X" && dataStart + 10 <= head.Length)        // 扩展：3 字节保留 + 24bit 宽高（-1）
            {
                long w24 = head[dataStart + 4] | (head[dataStart + 5] << 8) | (head[dataStart + 6] << 16);
                long h24 = head[dataStart + 7] | (head[dataStart + 8] << 8) | (head[dataStart + 9] << 16);
                return new PageSize((int)w24 + 1, (int)h24 + 1);
            }
            p += 8 + size + (size & 1);   // 块 2 字节对齐
        }
        return null;
    }
}
