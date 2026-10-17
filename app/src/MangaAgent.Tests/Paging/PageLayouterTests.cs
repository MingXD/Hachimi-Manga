using MangaAgent.Core.Comics;
using MangaAgent.Core.Paging;
using Xunit;

namespace MangaAgent.Tests.Paging;

public class PageLayouterTests
{
    private static List<PhysicalPageRef> Pages(params double[] ratios)
        => ratios.Select((r, i) => new PhysicalPageRef(i, r)).ToList();

    [Fact]
    public void Cover_Single_Then_Pairs()
    {
        var layout = PageLayouter.Layout(Pages(0.66, 0.66, 0.66, 0.66, 0.66), new PageLayoutOptions());
        Assert.Equal(3, layout.Count);
        Assert.False(layout[0].IsDouble);
        Assert.True(layout[1].IsDouble && layout[1].FirstIndex == 1 && layout[1].SecondIndex == 2);
        Assert.True(layout[2].IsDouble && layout[2].FirstIndex == 3 && layout[2].SecondIndex == 4);
    }

    [Fact]
    public void Odd_Remaining_Last_Single()
    {
        var layout = PageLayouter.Layout(Pages(0.66, 0.66, 0.66, 0.66), new PageLayoutOptions());
        Assert.Equal(3, layout.Count);
        Assert.False(layout[2].IsDouble);
        Assert.Equal(3, layout[2].FirstIndex);
    }

    [Fact]
    public void Landscape_Stays_Single()
    {
        var layout = PageLayouter.Layout(Pages(0.66, 2.0, 0.66, 0.66, 0.66), new PageLayoutOptions());
        Assert.False(layout[1].IsDouble);
        Assert.Equal(1, layout[1].FirstIndex);
        Assert.True(layout[2].IsDouble && layout[2].FirstIndex == 2 && layout[2].SecondIndex == 3);
    }

    [Fact]
    public void CoverOff_Starts_Pairing_From_Page0()
    {
        var layout = PageLayouter.Layout(Pages(0.66, 0.66, 0.66, 0.66),
            new PageLayoutOptions { CoverSingle = false });
        Assert.True(layout[0].IsDouble && layout[0].FirstIndex == 0 && layout[0].SecondIndex == 1);
    }

    [Fact]
    public void FirstPairOffset1_Pairs_From_Second_After_Offset()
    {
        // 6 页：封面单页 + 第2页单页 → (2,3),(4,5)
        var layout = PageLayouter.Layout(Pages(0.66, 0.66, 0.66, 0.66, 0.66, 0.66),
            new PageLayoutOptions { FirstPairOffset = 1 });
        Assert.Equal(4, layout.Count);
        Assert.False(layout[0].IsDouble);   // 封面
        Assert.False(layout[1].IsDouble);   // 第 2 页单页
        Assert.Equal(1, layout[1].FirstIndex);
        Assert.True(layout[2].IsDouble && layout[2].FirstIndex == 2 && layout[2].SecondIndex == 3);
        Assert.True(layout[3].IsDouble && layout[3].FirstIndex == 4 && layout[3].SecondIndex == 5);
    }

    [Fact]
    public void FirstPairOffset0_Pairs_From_Second()
    {
        var layout = PageLayouter.Layout(Pages(0.66, 0.66, 0.66, 0.66),
            new PageLayoutOptions { FirstPairOffset = 0 });
        Assert.True(layout[0].FirstIndex == 0 && !layout[0].IsDouble);   // 封面
        Assert.True(layout[1].IsDouble && layout[1].FirstIndex == 1 && layout[1].SecondIndex == 2);
    }

    [Fact]
    public void NaturalSort_Orders_Pages_Correctly()
    {
        var names = new[] { "page10.png", "page2.png", "page1.png", "page100.png" };
        Array.Sort(names, ImageInfo.CompareNatural);
        Assert.Equal(new[] { "page1.png", "page2.png", "page10.png", "page100.png" }, names);
    }

    // ---------- v4.3 强制配对（阅读器默认 ForcePairing=true） ----------

    [Fact]
    public void ForcePairing_Offset0_Pairs_From_First_Page()
    {
        // 配对起点1：强制 (0,1)、(2,3)、(4,5)——封面页也参与配对，忽略宽高
        var layout = PageLayouter.Layout(Pages(0.66, 0.66, 0.66, 0.66, 0.66, 0.66),
            new PageLayoutOptions { ForcePairing = true, FirstPairOffset = 0 });
        Assert.Equal(3, layout.Count);
        Assert.True(layout[0].IsDouble && layout[0].FirstIndex == 0 && layout[0].SecondIndex == 1);
        Assert.True(layout[1].IsDouble && layout[1].FirstIndex == 2 && layout[1].SecondIndex == 3);
        Assert.True(layout[2].IsDouble && layout[2].FirstIndex == 4 && layout[2].SecondIndex == 5);
    }

    [Fact]
    public void ForcePairing_Offset1_First_Single_Then_Pairs()
    {
        // 配对起点2：第 1 张单页，强制 (1,2)、(3,4)、(5,6)
        var layout = PageLayouter.Layout(Pages(0.66, 0.66, 0.66, 0.66, 0.66, 0.66, 0.66),
            new PageLayoutOptions { ForcePairing = true, FirstPairOffset = 1 });
        Assert.Equal(4, layout.Count);
        Assert.False(layout[0].IsDouble);
        Assert.Equal(0, layout[0].FirstIndex);
        Assert.True(layout[1].IsDouble && layout[1].FirstIndex == 1 && layout[1].SecondIndex == 2);
        Assert.True(layout[2].IsDouble && layout[2].FirstIndex == 3 && layout[2].SecondIndex == 4);
        Assert.True(layout[3].IsDouble && layout[3].FirstIndex == 5 && layout[3].SecondIndex == 6);
    }

    [Fact]
    public void ForcePairing_Ignores_Landscape_And_Odd_Tail()
    {
        // 宽页（2.0）也强制并排；末张不足单页（5 页，起点1 → (0,1),(2,3),4 单页）
        var layout = PageLayouter.Layout(Pages(2.0, 0.66, 2.0, 0.66, 0.66),
            new PageLayoutOptions { ForcePairing = true, FirstPairOffset = 0 });
        Assert.Equal(3, layout.Count);
        Assert.True(layout[0].IsDouble && layout[0].FirstIndex == 0 && layout[0].SecondIndex == 1);
        Assert.True(layout[1].IsDouble && layout[1].FirstIndex == 2 && layout[1].SecondIndex == 3);
        Assert.False(layout[2].IsDouble);
        Assert.Equal(4, layout[2].FirstIndex);
    }

    [Fact]
    public void ForcePairing_Offset1_Landscape_Still_Paired()
    {
        // 起点2 + 首张宽页：1 单页，(2,3) 宽页也并排，末张不足单页（4 页 → [0],[1,2],[3]）
        var layout = PageLayouter.Layout(Pages(2.0, 2.0, 0.66, 0.66),
            new PageLayoutOptions { ForcePairing = true, FirstPairOffset = 1 });
        Assert.Equal(3, layout.Count);
        Assert.False(layout[0].IsDouble);
        Assert.Equal(0, layout[0].FirstIndex);
        Assert.True(layout[1].IsDouble && layout[1].FirstIndex == 1 && layout[1].SecondIndex == 2);
        Assert.False(layout[2].IsDouble);
        Assert.Equal(3, layout[2].FirstIndex);
    }
}
