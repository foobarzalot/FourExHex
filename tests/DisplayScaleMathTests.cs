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
        // natural factor ≈ 2.22. The mobile floor must not pull it down. (Since
        // MobileMinFactor is now tuned to S9-portrait parity the natural factor
        // and the floor coincide here; the assertion still proves clamping
        // doesn't reduce a high natural value.)
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

    [Fact]
    public void MobileMinFactor_IsTunedToS9PortraitParity()
    {
        // The mobile floor is tuned so iPhones (logical dpi ~158, just under the
        // 160 baseline) get the same factor as the S9 portrait's natural ~2.22 —
        // equalizing physical button size and logical viewport across the two
        // reference devices. Pinned here so an accidental revert is caught by CI.
        Assert.Equal(2.2222f, DisplayScaleMath.MobileMinFactor, Tolerance);
    }

    // FactorForRawMobileDpi — mobile uses RAW DPI / MobileReferenceDpi
    // (=180) so devices land at the same physical button size regardless
    // of their pixel density. logicalDpi (which divides raw by osScale)
    // is wrong on iOS — Apple's retina pixel doubling doesn't change what
    // physical size our buttons render at, so dividing by osScale just
    // makes iPhones come up short. Desktop continues to use the
    // logicalDpi / 160 path because retina OS-scaling there genuinely
    // pre-renders content at logical points.

    [Fact]
    public void MobileReferenceDpi_PinsS9FhdPortrait()
    {
        // 180 is reverse-engineered from S9 FHD+ portrait at the
        // 2.22 factor we already ship: 401 raw DPI / 2.22 ≈ 180. Any
        // future tweak to "what S9 looks like" should retune this.
        Assert.Equal(180f, DisplayScaleMath.MobileReferenceDpi, Tolerance);
    }

    [Fact]
    public void FactorForRawMobileDpi_iPhone13Mini_Matches264()
    {
        // iPhone 13 mini raw DPI 476. Target 476/180 = 2.6444. This is
        // the whole point of the formula — high-DPI iPhone gets a higher
        // factor so its buttons physically match the S9.
        Assert.Equal(2.6444f, DisplayScaleMath.FactorForRawMobileDpi(476f, DisplayScaleMath.MobileMinFactor), Tolerance);
    }

    [Fact]
    public void FactorForRawMobileDpi_S9FhdPortrait_Matches222()
    {
        // S9 portrait raw DPI 401. 401/180 = 2.227 ≈ 2.22 — preserves
        // current S9 behavior unchanged.
        Assert.Equal(2.2278f, DisplayScaleMath.FactorForRawMobileDpi(401f, DisplayScaleMath.MobileMinFactor), Tolerance);
    }

    [Fact]
    public void FactorForRawMobileDpi_BelowFloor_LiftsToFloor()
    {
        // A low-density Android phone (e.g., 160 DPI) would compute
        // 160/180 = 0.89 — below the mobile floor. Must lift to the
        // MobileMinFactor safety net so buttons are still tappable.
        Assert.Equal(
            DisplayScaleMath.MobileMinFactor,
            DisplayScaleMath.FactorForRawMobileDpi(160f, DisplayScaleMath.MobileMinFactor),
            Tolerance);
    }

    [Fact]
    public void FactorForRawMobileDpi_AbsurdDensity_ClampsToMaxFactor()
    {
        Assert.Equal(
            DisplayScaleMath.MaxFactor,
            DisplayScaleMath.FactorForRawMobileDpi(10000f, DisplayScaleMath.MobileMinFactor),
            Tolerance);
    }

    [Fact]
    public void FactorForRawMobileDpi_HeadlessOrZero_ReturnsFloor()
    {
        // A headless mobile rig (dpi<=0) should still see the mobile lift.
        Assert.Equal(
            DisplayScaleMath.MobileMinFactor,
            DisplayScaleMath.FactorForRawMobileDpi(0f, DisplayScaleMath.MobileMinFactor),
            Tolerance);
    }
}
