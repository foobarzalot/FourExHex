using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="CampaignGridMath"/> (issue #2): the pointy-top
/// honeycomb layout behind the campaign screen's tier grids. One tier is
/// a 64-cell block laid out 8 wide (portrait) or 16 wide (landscape);
/// odd rows shift right half a cell and rows interlock at a 0.75-height
/// vertical pitch.
/// </summary>
public class CampaignGridMathTests
{
    // A convenient cell size: width 40, height 48, gap 4.
    private const float W = 40f;
    private const float H = 48f;
    private const float G = 4f;

    // Horizontal step between columns and vertical pitch between rows.
    private const float StepX = W + G;          // 44
    private const float PitchY = 0.75f * H + G; // 40

    [Fact]
    public void CellCenter_FirstCell_IsHalfCellFromOrigin()
    {
        (float x, float y) = CampaignGridMath.CellCenter(0, 8, W, H, G);

        Assert.Equal(W / 2f, x, 3);
        Assert.Equal(H / 2f, y, 3);
    }

    [Fact]
    public void CellCenter_SecondColumn_StepsByWidthPlusGap()
    {
        (float x, float y) = CampaignGridMath.CellCenter(1, 8, W, H, G);

        Assert.Equal(W / 2f + StepX, x, 3);
        Assert.Equal(H / 2f, y, 3);
    }

    [Fact]
    public void CellCenter_OddRow_ShiftsRightHalfStep_AndInterlocksVertically()
    {
        // Index 8 in an 8-wide block = row 1, col 0.
        (float x, float y) = CampaignGridMath.CellCenter(8, 8, W, H, G);

        Assert.Equal(W / 2f + StepX / 2f, x, 3);
        Assert.Equal(H / 2f + PitchY, y, 3);
    }

    [Fact]
    public void CellCenter_SixteenWide_RowIsHighHexDigit()
    {
        // At 16 columns each row is one 0x10 block: level 0x2F sits in
        // row 2, column 15. This self-indexing property is part of the
        // design — preserve it.
        (float x, float y) = CampaignGridMath.CellCenter(0x2F, 16, W, H, G);

        Assert.Equal(W / 2f + 15 * StepX, x, 3); // row 2 is even: no shift
        Assert.Equal(H / 2f + 2 * PitchY, y, 3);
    }

    [Fact]
    public void BlockSize_PortraitEightByEight()
    {
        (float width, float height) = CampaignGridMath.BlockSize(64, 8, W, H, G);

        // 8 columns plus the odd-row half-step overhang; 8 interlocked rows.
        Assert.Equal(8 * StepX - G + StepX / 2f, width, 3);
        Assert.Equal(7 * PitchY + H, height, 3);
    }

    [Fact]
    public void BlockSize_LandscapeSixteenByFour()
    {
        (float width, float height) = CampaignGridMath.BlockSize(64, 16, W, H, G);

        Assert.Equal(16 * StepX - G + StepX / 2f, width, 3);
        Assert.Equal(3 * PitchY + H, height, 3);
    }

    [Fact]
    public void BlockSize_SingleRow_HasNoOddRowOverhang()
    {
        (float width, float height) = CampaignGridMath.BlockSize(16, 16, W, H, G);

        Assert.Equal(16 * StepX - G, width, 3);
        Assert.Equal(H, height, 3);
    }

    [Fact]
    public void HitTest_CellCenters_ResolveToTheirIndex()
    {
        for (int i = 0; i < 64; i++)
        {
            (float cx, float cy) = CampaignGridMath.CellCenter(i, 8, W, H, G);

            Assert.Equal(i, CampaignGridMath.HitTest(cx, cy, 64, 8, W, H, G));
        }
    }

    [Fact]
    public void HitTest_BoundingBoxCornerOfFirstCell_IsOutsideTheHex()
    {
        // The hexagon clips its bounding box corners: a point near the
        // top-left corner of cell 0's box is outside every hex.
        Assert.Null(CampaignGridMath.HitTest(1f, 1f, 64, 8, W, H, G));
    }

    [Fact]
    public void HitTest_FarOutsideBlock_ReturnsNull()
    {
        Assert.Null(CampaignGridMath.HitTest(-50f, -50f, 64, 8, W, H, G));
        Assert.Null(CampaignGridMath.HitTest(10_000f, 10f, 64, 8, W, H, G));
    }

    [Fact]
    public void HitTest_InterlockOverlapBand_ResolvesByTrueHexShape()
    {
        // Rows overlap by 25% of hex height, so y-bands are ambiguous and
        // only the exact hex shape disambiguates. Just below a row-1
        // hex's top vertex → that row-1 hex; just above it → the notch
        // between two row-0 hexes (their slanted edges have pulled away
        // at that x), which is a dead zone.
        (float cx, float cy) = CampaignGridMath.CellCenter(8, 8, W, H, G);
        float topOfRow1 = cy - H / 2f;

        Assert.Equal(8, CampaignGridMath.HitTest(cx, topOfRow1 + 1f, 64, 8, W, H, G));
        Assert.Null(CampaignGridMath.HitTest(cx, topOfRow1 - 1f, 64, 8, W, H, G));
    }
}
