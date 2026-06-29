using Xunit;

namespace FourExHex.Tests;

public class HitTestMathTests
{
    [Theory]
    [InlineData(0, 0)]      // top-left corner
    [InlineData(29, 19)]    // bottom-right corner (inclusive)
    [InlineData(15, 10)]    // interior
    public void InOffsetBounds_InsideRectangle_True(int col, int row)
    {
        Assert.True(HitTestMath.InOffsetBounds(col, row, 30, 20));
    }

    [Theory]
    [InlineData(-1, 5)]     // col below 0
    [InlineData(30, 5)]     // col == cols (exclusive upper)
    [InlineData(5, -1)]     // row below 0
    [InlineData(5, 20)]     // row == rows (exclusive upper)
    [InlineData(-1, -1)]    // off both axes
    [InlineData(100, 100)]  // far outside
    public void InOffsetBounds_OutsideRectangle_False(int col, int row)
    {
        Assert.False(HitTestMath.InOffsetBounds(col, row, 30, 20));
    }

    [Fact]
    public void InOffsetBounds_DegenerateZeroSize_AlwaysFalse()
    {
        Assert.False(HitTestMath.InOffsetBounds(0, 0, 0, 0));
    }
}
