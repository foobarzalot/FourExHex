using Xunit;

namespace FourExHex.Tests;

public class EasingMathTests
{
    private const float Tolerance = 0.0001f;

    // ---- SmoothStep ----

    [Fact]
    public void SmoothStep_AtZero_ReturnsZero()
    {
        Assert.Equal(0f, EasingMath.SmoothStep(0f), Tolerance);
    }

    [Fact]
    public void SmoothStep_AtOne_ReturnsOne()
    {
        Assert.Equal(1f, EasingMath.SmoothStep(1f), Tolerance);
    }

    [Fact]
    public void SmoothStep_AtMidpoint_ReturnsHalf()
    {
        // 3t^2 - 2t^3 at t=0.5 => 0.75 - 0.25 = 0.5 (symmetric curve).
        Assert.Equal(0.5f, EasingMath.SmoothStep(0.5f), Tolerance);
    }

    [Fact]
    public void SmoothStep_EarlyInput_EasesInBelowLinear()
    {
        // t=0.1 => 0.01*(3 - 0.2) = 0.028, less than linear 0.1 (slow start).
        float s = EasingMath.SmoothStep(0.1f);
        Assert.Equal(0.028f, s, Tolerance);
        Assert.True(s < 0.1f, $"expected ease-in below linear, got {s}");
    }

    [Fact]
    public void SmoothStep_LateInput_EasesOutAboveLinear()
    {
        // t=0.9 => 0.81*(3 - 1.8) = 0.972, greater than linear 0.9 (slow finish).
        float s = EasingMath.SmoothStep(0.9f);
        Assert.Equal(0.972f, s, Tolerance);
        Assert.True(s > 0.9f, $"expected ease-out above linear, got {s}");
    }

    [Fact]
    public void SmoothStep_BelowZero_ClampsToZero()
    {
        // Guards the case where callers overshoot the range.
        Assert.Equal(0f, EasingMath.SmoothStep(-0.5f), Tolerance);
    }

    [Fact]
    public void SmoothStep_AboveOne_ClampsToOne()
    {
        // The final animation frame (elapsed/duration >= 1) resolves exactly to 1.
        Assert.Equal(1f, EasingMath.SmoothStep(1.7f), Tolerance);
    }

    // ---- Lerp ----

    [Fact]
    public void Lerp_AtZero_ReturnsStart()
    {
        Assert.Equal(100f, EasingMath.Lerp(100f, 300f, 0f), Tolerance);
    }

    [Fact]
    public void Lerp_AtOne_ReturnsEnd()
    {
        Assert.Equal(300f, EasingMath.Lerp(100f, 300f, 1f), Tolerance);
    }

    [Fact]
    public void Lerp_AtMidpoint_ReturnsAverage()
    {
        Assert.Equal(200f, EasingMath.Lerp(100f, 300f, 0.5f), Tolerance);
    }

    [Fact]
    public void Lerp_NegativeStart_InterpolatesAcrossZero()
    {
        // A pan axis can move from a negative to a positive Position.
        Assert.Equal(-10f, EasingMath.Lerp(-50f, 30f, 0.5f), Tolerance);
    }
}
