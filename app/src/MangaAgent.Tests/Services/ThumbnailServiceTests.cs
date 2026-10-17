using MangaAgent.App.Services;
using Xunit;

namespace MangaAgent.Tests.Services;

/// <summary>v4.4 封面解码宽计算（UniformToFill 按比例 + DPI 余量 + 钳位）。</summary>
public class ThumbnailServiceTests
{
    [Fact]
    public void Portrait_Cover_Uses_Frame_Width()
    {
        // 纵向 0.7：需要宽 = max(216, 309×0.7≈216.3) → ×1.25 ≈ 270
        Assert.Equal(270, ThumbnailService.CoverDecodeWidth(216, 309, 0.7, 1.0));
    }

    [Fact]
    public void Landscape_Cover_Gets_Much_Larger_Decode_Width()
    {
        // 横向 1.6：需要宽 = max(216, 309×1.6≈494) → ×1.25 ≈ 618（修复横向封面糊的核心）
        int w = ThumbnailService.CoverDecodeWidth(216, 309, 1.6, 1.0);
        Assert.InRange(w, 600, 630);
        Assert.True(w > 216 * 2, "横向封面解码宽应显著大于框宽");
    }

    [Fact]
    public void Folder_Cover_Frame_Applies_To_Same_Formula()
    {
        // 文件夹封面框 216×281 竖图 0.7：max(216, 196.7) × 1.25 ≈ 270
        Assert.Equal(270, ThumbnailService.CoverDecodeWidth(216, 281, 0.7, 1.0));
        // 横向 1.8：max(216, 505.8) × 1.25 ≈ 632
        Assert.InRange(ThumbnailService.CoverDecodeWidth(216, 281, 1.8, 1.0), 620, 640);
    }

    [Fact]
    public void Dpi_Scaling_Increases_Decode_Width()
    {
        int dpi1 = ThumbnailService.CoverDecodeWidth(216, 309, 0.7, 1.0);
        int dpi15 = ThumbnailService.CoverDecodeWidth(216, 309, 0.7, 1.5);
        Assert.True(dpi15 > dpi1, "150% DPI 需要更高解码宽");
        Assert.Equal(406, dpi15);   // 216.3 × 1.5 × 1.25 ≈ 406
    }

    [Fact]
    public void Very_Wide_Cover_Is_Clamped()
    {
        // 超宽 4.0：max(216, 1236) × 1.25 ≈ 1545 → 钳到 1200
        Assert.Equal(1200, ThumbnailService.CoverDecodeWidth(216, 309, 4.0, 1.0));
    }

    [Fact]
    public void Tiny_Frame_Is_Floored()
    {
        // 极小框（100×100）竖图：100×1.25=125 → 钳到 160
        Assert.Equal(160, ThumbnailService.CoverDecodeWidth(100, 100, 0.7, 1.0));
    }
}
