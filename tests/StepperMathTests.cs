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
        Assert.Equal(expected, StepperMath.Clamp(value, min, max, step));
    }

    [Theory]
    [InlineData(200, 0, 25, 5, 25)]   // over max -> clamped to max (after snap)
    [InlineData(0, 5, 25, 5, 5)]      // under min -> clamped to min
    [InlineData(-50, 0, 100, 5, 0)]   // negative -> 0 guard then snap/clamp
    public void Clamp_Linear_ClampsIntoRange(int value, int min, int max, int step, int expected)
    {
        Assert.Equal(expected, StepperMath.Clamp(value, min, max, step));
    }

    [Fact]
    public void Clamp_Linear_StepZeroLeavesValueUnsnappedButClamped()
    {
        // step 0 disables snapping; value still clamps to [min,max].
        Assert.Equal(37, StepperMath.Clamp(37, 0, 100, 0));
        Assert.Equal(100, StepperMath.Clamp(250, 0, 100, 0));
    }

    // ---- Neighbor: linear ------------------------------------------------

    [Theory]
    [InlineData(20, +1, 5, 25)]
    [InlineData(20, -1, 5, 15)]
    public void Neighbor_Linear_MovesOneStep(int cur, int dir, int step, int expected)
    {
        // Neighbor does NOT clamp (the caller re-Clamps); it just offsets.
        Assert.Equal(expected, StepperMath.Neighbor(cur, dir, step));
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
