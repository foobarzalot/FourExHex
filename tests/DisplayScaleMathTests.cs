using Xunit;

namespace FourExHex.Tests;

public class DisplayScaleMathTests
{
    private const float Tolerance = 0.0001f;

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(-160f)]
    public void FactorForDpi_NonPositive_IsMinFactor(float dpi)
    {
        // Headless / unknown screen reports dpi <= 0 → never shrink below design.
        Assert.Equal(DisplayScaleMath.MinFactor, DisplayScaleMath.FactorForDpi(dpi), Tolerance);
    }

    [Fact]
    public void FactorForDpi_TypicalDesktop_FloorsToOne()
    {
        // ~96 dpi (typical desktop logical density) is below the 160 baseline,
        // so it floors to 1.0 — desktop rendering stays unchanged.
        Assert.Equal(1.0f, DisplayScaleMath.FactorForDpi(96f), Tolerance);
    }

    [Fact]
    public void FactorForDpi_BaselineDensity_IsOne()
    {
        Assert.Equal(1.0f, DisplayScaleMath.FactorForDpi(160f), Tolerance);
    }

    [Fact]
    public void FactorForDpi_MidDensity_ScalesUp()
    {
        Assert.Equal(1.375f, DisplayScaleMath.FactorForDpi(220f), Tolerance);
    }

    [Fact]
    public void FactorForDpi_HighDensityPhone_ScalesUp()
    {
        Assert.Equal(2.75f, DisplayScaleMath.FactorForDpi(440f), Tolerance);
    }

    [Fact]
    public void FactorForDpi_AbsurdDensity_ClampsToMaxFactor()
    {
        Assert.Equal(DisplayScaleMath.MaxFactor, DisplayScaleMath.FactorForDpi(10000f), Tolerance);
    }

    [Fact]
    public void FactorForDpi_MobileFloor_LiftsLowDensityToFloor()
    {
        // iPhone 13 mini: dpi=476/osScale=3 → logicalDpi=158.67, just under the
        // 160 baseline. Without a mobile floor this floors to 1.0 (the user-
        // reported "too small" symptom). With MobileMinFactor it lifts.
        Assert.Equal(
            DisplayScaleMath.MobileMinFactor,
            DisplayScaleMath.FactorForDpi(158.67f, DisplayScaleMath.MobileMinFactor),
            Tolerance);
    }

    [Fact]
    public void FactorForDpi_MobileFloor_DoesNotLowerHighNaturalFactor()
    {
        // Galaxy S9 portrait: dpi=480/osScale=1.35 → logicalDpi≈355.5,
        // natural factor ≈ 2.22. The mobile floor must not pull it down.
        Assert.Equal(2.2222f, DisplayScaleMath.FactorForDpi(355.5556f, DisplayScaleMath.MobileMinFactor), Tolerance);
    }

    [Fact]
    public void FactorForDpi_MobileFloor_DoesNotLowerMaxFactor()
    {
        // MaxFactor still wins over an absurdly high density even with the
        // mobile floor in play.
        Assert.Equal(DisplayScaleMath.MaxFactor, DisplayScaleMath.FactorForDpi(10000f, DisplayScaleMath.MobileMinFactor), Tolerance);
    }

    [Fact]
    public void FactorForDpi_HeadlessWithMobileFloor_ReturnsFloor()
    {
        // A headless mobile rig (dpi<=0) should still see the mobile lift, not
        // collapse to the desktop default of 1.0.
        Assert.Equal(
            DisplayScaleMath.MobileMinFactor,
            DisplayScaleMath.FactorForDpi(0f, DisplayScaleMath.MobileMinFactor),
            Tolerance);
    }

    [Fact]
    public void FactorForDpi_SubMinFloor_GuardClampsFloorToOne()
    {
        // An adapter passing minFactor below 1.0 must not shrink design size
        // below the authored MinFactor.
        Assert.Equal(1.0f, DisplayScaleMath.FactorForDpi(96f, 0.5f), Tolerance);
    }
}
