using MangaAgent.Core.Comics;
using Xunit;

namespace MangaAgent.Tests.Comics;

/// <summary>页面自然排序回归（v3 #14：后记字母页曾排到数字正文页前成为封面）。
/// 核心要求：① 数字块按数值比较；② 数字优先于字母；③ 比较器对称（Compare(a,b) == -Compare(b,a)）。</summary>
public class ImageInfoTests
{
    private static void AssertSorted(string[] names, string[] expected)
    {
        Array.Sort(names, ImageInfo.CompareNatural);
        Assert.Equal(expected, names);
    }

    [Fact]
    public void Letter_Afterword_Sorts_After_Digit_Pages()
    {
        // 用户场景：正文 _1/_2/_10，后记 xy —— xy 必须排最后
        AssertSorted(new[] { "xy", "_2", "_1", "_10" }, new[] { "_1", "_2", "_10", "xy" });
    }

    [Fact]
    public void ZeroPadded_Digits_Then_Letter()
    {
        AssertSorted(new[] { "xy", "0002", "0001" }, new[] { "0001", "0002", "xy" });
    }

    [Fact]
    public void Digit_Names_Before_Letter_Names()
    {
        AssertSorted(new[] { "xy", "1", "2", "ab" }, new[] { "1", "2", "ab", "xy" });
    }

    [Fact]
    public void Natural_Numeric_Order_With_Common_Prefix()
    {
        AssertSorted(new[] { "page10.png", "page2.png", "page1.png", "page100.png" },
            new[] { "page1.png", "page2.png", "page10.png", "page100.png" });
    }

    [Fact]
    public void Comparer_Is_Antisymmetric()
    {
        // 对称性：任意一对 (a,b) 必须满足 Compare(a,b) == -Compare(b,a)，否则 List.Sort 结果不可预测
        string[] cases = { "_1", "_2", "_10", "xy", "ab", "0001", "1", "page2", "page10", "a1", "a" };
        for (int i = 0; i < cases.Length; i++)
            for (int j = 0; j < cases.Length; j++)
            {
                int ab = ImageInfo.CompareNatural(cases[i], cases[j]);
                int ba = ImageInfo.CompareNatural(cases[j], cases[i]);
                Assert.True(ab == -ba, $"不对称: Compare({cases[i]},{cases[j]})={ab} vs Compare({cases[j]},{cases[i]})={ba}");
            }
    }
}
