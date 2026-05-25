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
}
