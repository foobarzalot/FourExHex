using Xunit;

namespace FourExHex.Tests;

public class StepperMathTests
{
    // ---- Clamp: linear (no stops) snap + range clamp ---------------------

    [Theory]
    [InlineData(12, 0, 100, 5, 10)]   // 12 -> nearest multiple of 5 below midpoint -> 10
    [InlineData(13, 0, 100, 5, 15)]   // 13 -> rounds up to 15
    [InlineData(5, 0, 100, 10, 10)]   // exact half (.5) rounds up
    [InlineData(4, 0, 100, 10, 0)]    // below half rounds down
    [InlineData(7, 0, 100, 1, 7)]     // step 1 is identity within range
    public void Clamp_Linear_SnapsToNearestStep(int value, int min, int max, int step, int expected)
    {
        Assert.Equal(expected, StepperMath.Clamp(value, min, max, step, stops: null));
    }

    [Theory]
    [InlineData(200, 0, 25, 5, 25)]   // over max -> clamped to max (after snap)
    [InlineData(0, 5, 25, 5, 5)]      // under min -> clamped to min
    [InlineData(-50, 0, 100, 5, 0)]   // negative -> 0 guard then snap/clamp
    public void Clamp_Linear_ClampsIntoRange(int value, int min, int max, int step, int expected)
    {
        Assert.Equal(expected, StepperMath.Clamp(value, min, max, step, stops: null));
    }

    [Fact]
    public void Clamp_Linear_StepZeroLeavesValueUnsnappedButClamped()
    {
        // step 0 disables snapping; value still clamps to [min,max].
        Assert.Equal(37, StepperMath.Clamp(37, 0, 100, 0, stops: null));
        Assert.Equal(100, StepperMath.Clamp(250, 0, 100, 0, stops: null));
    }

    // ---- Clamp: explicit stops ------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(63, 75)]   // |50-63|=13 > |75-63|=12 -> 75
    [InlineData(88, 90)]
    [InlineData(1000, 100)] // beyond top stop -> top
    [InlineData(-10, 0)]    // negative guard -> nearest is 0
    public void Clamp_Stops_SnapsToNearestStop(int value, int expected)
    {
        int[] stops = { 0, 50, 75, 90, 95, 100 };
        Assert.Equal(expected, StepperMath.Clamp(value, stops[0], stops[^1], step: 0, stops));
    }

    [Fact]
    public void Clamp_Stops_TiePrefersLowerStop()
    {
        // value 25 is equidistant from 0 and 50; strict-less tie-break keeps the lower.
        int[] stops = { 0, 50, 75, 90, 95, 100 };
        Assert.Equal(0, StepperMath.Clamp(25, 0, 100, 0, stops));
    }

    // ---- NearestStopIndex ------------------------------------------------

    [Fact]
    public void NearestStopIndex_ReturnsClosestAscendingIndex()
    {
        int[] stops = { 0, 50, 75, 90, 95, 100 };
        Assert.Equal(0, StepperMath.NearestStopIndex(stops, 10));
        Assert.Equal(2, StepperMath.NearestStopIndex(stops, 80));
        Assert.Equal(5, StepperMath.NearestStopIndex(stops, 200));
    }

    // ---- Neighbor: linear ------------------------------------------------

    [Theory]
    [InlineData(20, +1, 5, 25)]
    [InlineData(20, -1, 5, 15)]
    public void Neighbor_Linear_MovesOneStep(int cur, int dir, int step, int expected)
    {
        // Neighbor does NOT clamp (the caller re-Clamps); it just offsets.
        Assert.Equal(expected, StepperMath.Neighbor(cur, dir, step, stops: null));
    }

    // ---- Neighbor: stops -------------------------------------------------

    [Fact]
    public void Neighbor_Stops_MovesToAdjacentStopAndClampsAtEnds()
    {
        int[] stops = { 0, 50, 75, 90, 95, 100 };
        Assert.Equal(75, StepperMath.Neighbor(50, +1, 0, stops));  // 50 -> next stop 75
        Assert.Equal(50, StepperMath.Neighbor(75, -1, 0, stops));  // 75 -> prev stop 50
        Assert.Equal(100, StepperMath.Neighbor(100, +1, 0, stops)); // top stop holds
        Assert.Equal(0, StepperMath.Neighbor(0, -1, 0, stops));    // bottom stop holds
    }

    [Fact]
    public void Neighbor_Stops_StartsFromNearestStopWhenCurrentIsOffGrid()
    {
        // cur 60 nearest is 50 (idx 1); +1 -> 75.
        int[] stops = { 0, 50, 75, 90, 95, 100 };
        Assert.Equal(75, StepperMath.Neighbor(60, +1, 0, stops));
    }

    // ---- ParseDigits -----------------------------------------------------

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12%", 12)]
    [InlineData("x12y", 12)]
    [InlineData("3 4", 34)]   // digits concatenated across non-digits
    [InlineData("", 0)]
    [InlineData("%", 0)]      // no digits -> 0
    public void ParseDigits_ExtractsDigitRun(string text, int expected)
    {
        Assert.Equal(expected, StepperMath.ParseDigits(text));
    }

    [Fact]
    public void ParseDigits_OverflowReturnsMaxValue()
    {
        // A digit run too large for int -> int.MaxValue so it clamps to max.
        Assert.Equal(int.MaxValue, StepperMath.ParseDigits("99999999999"));
    }
}
