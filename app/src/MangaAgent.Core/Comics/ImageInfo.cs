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
            cmp = long.Parse(na.Value).CompareTo(long.Parse(nb.Value));
            if (cmp != 0) return cmp;
            posA = na.Index + na.Length; posB = nb.Index + nb.Length;
            ia++; ib++;
        }
        if (ia < ma.Count) return 1;
        if (ib < mb.Count) return -1;
        return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从 PNG/JPEG 文件头解析尺寸；不支持返回 null。</summary>
    public static PageSize? ParseHeader(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 24 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
        {
            int w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return new PageSize(w, h);
        }
        if (head.Length >= 4 && head[0] == 0xFF && head[1] == 0xD8)
        {
            for (int i = 2; i + 9 < head.Length; i++)
            {
                if (head[i] == 0xFF && head[i + 1] >= 0xC0 && head[i + 1] <= 0xCF &&
                    head[i + 1] != 0xC4 && head[i + 1] != 0xC8 && head[i + 1] != 0xCC)
                {
                    int h = (head[i + 5] << 8) | head[i + 6];
                    int w = (head[i + 7] << 8) | head[i + 8];
                    return new PageSize(w, h);
                }
            }
        }
        return null;
    }
}
