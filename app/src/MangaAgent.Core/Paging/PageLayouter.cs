using MangaAgent.Core.Comics;

namespace MangaAgent.Core.Paging;

/// <summary>物理页引用（用于布局计算）。</summary>
public readonly record struct PhysicalPageRef(int Index, double AspectRatio);

/// <summary>逻辑页：单页（SecondIndex=-1）或双页合成。</summary>
public readonly record struct LogicalPage(int FirstIndex, int SecondIndex)
{
    public bool IsDouble => SecondIndex >= 0;
}

/// <summary>双页布局规则（全部可配置，默认贴合日漫阅读习惯）。</summary>
public sealed class PageLayoutOptions
{
    /// <summary>封面恒单页（ComicInfo FrontCover / 默认第 1 页）。</summary>
    public bool CoverSingle { get; init; } = true;
    /// <summary>宽高比 ≥ 此值视为横向跨页图，保持单页。默认 1.6。</summary>
    public double SpreadThreshold { get; init; } = 1.6;
    /// <summary>首尾逻辑页强制单页（Komga 规则）。</summary>
    public bool ForceFirstLastSingle { get; init; } = true;
}

/// <summary>
/// 双页合成纯函数：封面单页 → 成对 → 横向页单页 → 首尾单页。
/// 方向（RTL/LTR）不在此层：只影响 UI 推进方向与单页对齐。
/// </summary>
public static class PageLayouter
{
    public static List<LogicalPage> Layout(IReadOnlyList<PhysicalPageRef> pages, PageLayoutOptions? options = null)
    {
        var o = options ?? new PageLayoutOptions();
        var result = new List<LogicalPage>(pages.Count / 2 + 2);
        int i = 0;
        int n = pages.Count;

        // 封面单页
        if (o.CoverSingle && n > 0)
        {
            result.Add(new LogicalPage(pages[0].Index, -1));
            i = 1;
        }

        // 成对分组；横向页单页；最后若剩 1 页则单页
        while (i < n)
        {
            var cur = pages[i];
            if (cur.AspectRatio >= o.SpreadThreshold)
            {
                result.Add(new LogicalPage(cur.Index, -1));   // 横向跨页整张显示
                i++;
                continue;
            }
            if (i + 1 < n && pages[i + 1].AspectRatio < o.SpreadThreshold)
            {
                result.Add(new LogicalPage(cur.Index, pages[i + 1].Index));
                i += 2;
            }
            else
            {
                result.Add(new LogicalPage(cur.Index, -1));
                i++;
            }
        }

        // 首尾单页（Komga 规则）：末尾已是单页则无需处理；若成对后首个逻辑页是双页且"尾部单页"更合适，交给 UI 对齐。
        return result;
    }
}
