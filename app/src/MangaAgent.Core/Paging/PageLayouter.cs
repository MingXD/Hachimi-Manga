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

    /// <summary>
    /// 双页配对起点偏移（0 或 1）：
    /// 0 = 封面单页后 (1,2) 成对（原作 1、2 相接）；
    /// 1 = 封面与第 2 页均单页，(2,3) 成对（原作 2、3 相接，第 1 页是独立封面）。
    /// </summary>
    public int FirstPairOffset { get; init; } = 0;

    /// <summary>
    /// v4.3 强制配对（阅读器默认开启）：按配对起点**顺序两两配对，忽略页面宽高/封面规则**。
    /// 起点 0 → (1,2)、(3,4)…；起点 1 → 第 1 张单页，(2,3)、(4,5)…；末张不足单页。
    /// 关闭时走旧的封面单页/横向页单页规则（默认值仍为 false，测试与旧行为不受影响）。
    /// </summary>
    public bool ForcePairing { get; init; }
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

        // v4.3 强制配对：按配对起点顺序两两配对，忽略宽高/封面规则
        if (o.ForcePairing)
        {
            int first = Math.Clamp(o.FirstPairOffset, 0, 1);
            for (int k = 0; k < first && k < n; k++)
                result.Add(new LogicalPage(pages[k].Index, -1));   // 起点 1：第 1 张单页
            int j = first;
            while (j < n)
            {
                if (j + 1 < n)
                {
                    result.Add(new LogicalPage(pages[j].Index, pages[j + 1].Index));
                    j += 2;
                }
                else
                {
                    result.Add(new LogicalPage(pages[j].Index, -1));   // 末张不足单页
                    j++;
                }
            }
            return result;
        }

        // 封面单页
        if (o.CoverSingle && n > 0)
        {
            result.Add(new LogicalPage(pages[0].Index, -1));
            i = 1;
        }

        // 配对起点偏移：第 2 页也单页，从 (2,3) 开始成对（原作 2、3 相接）
        if (o.FirstPairOffset > 0 && i < n)
        {
            result.Add(new LogicalPage(pages[i].Index, -1));
            i++;
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
