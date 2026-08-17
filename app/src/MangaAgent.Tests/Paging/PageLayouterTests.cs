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
    public void NaturalSort_Orders_Pages_Correctly()
    {
        var names = new[] { "page10.png", "page2.png", "page1.png", "page100.png" };
        Array.Sort(names, ImageInfo.CompareNatural);
        Assert.Equal(new[] { "page1.png", "page2.png", "page10.png", "page100.png" }, names);
    }
}
