using Godot;
using Xunit;

namespace FourExHex.Tests;

public class ZoomMathTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void ComputeZoomMin_MapSmallerThanViewport_ReturnsOne()
    {
        // Map is 800x600 inside a 1920x1080 viewport (HUD 60). Already fits;
        // we floor at 1.0 so the player can't zoom out beyond the default.
        float min = ZoomMath.ComputeZoomMin(
            viewport: new Vector2(1920f, 1080f),
            hudHeight: 60f,
            mapPixelSize: new Vector2(800f, 600f));

        Assert.Equal(1f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_TallMapInWideViewport_YBinds()
    {
        // 1920x1080 viewport, 60px HUD => 1020 available Y.
        // Map 1000x2040 => Y-fit = 1020/2040 = 0.5; X-fit = 1920/1000 = 1.92.
        float min = ZoomMath.ComputeZoomMin(
            viewport: new Vector2(1920f, 1080f),
            hudHeight: 60f,
            mapPixelSize: new Vector2(1000f, 2040f));

        Assert.Equal(0.5f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_WideMapInTallViewport_XBinds()
    {
        // 1000x2000 viewport, 0px HUD. Map 2000x1000 => X-fit = 0.5,
        // Y-fit = 2.0. Min picks X.
        float min = ZoomMath.ComputeZoomMin(
            viewport: new Vector2(1000f, 2000f),
            hudHeight: 0f,
            mapPixelSize: new Vector2(2000f, 1000f));

        Assert.Equal(0.5f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_HudHeightSubtractedFromY()
    {
        // Without HUD subtraction, Y-fit would be 1080/1080 = 1.0 and the
        // result would be 1.0 (since X also fits). With a 60px HUD strip
        // taken off, Y-fit becomes 1020/1080 ≈ 0.9444 and binds.
        float min = ZoomMath.ComputeZoomMin(
            viewport: new Vector2(2000f, 1080f),
            hudHeight: 60f,
            mapPixelSize: new Vector2(1500f, 1080f));

        Assert.Equal(1020f / 1080f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_FourExHexDefaults_LessThanOne()
    {
        // Sanity check on real game numbers: 30x20 grid, HexSize=48 gives
        // PixelSize ~ (1443, 1488). On a 1920x1080 fullscreen viewport with
        // a 60px HUD strip, the available area is ~1920x1020, so Y binds
        // and the fit-min is well under 1.
        float pixelW = (30 + 0.5f) * Mathf.Sqrt(3f) * 48f;
        float pixelH = (1.5f * 20 + 0.5f) * 48f;

        float min = ZoomMath.ComputeZoomMin(
            viewport: new Vector2(1920f, 1080f),
            hudHeight: 60f,
            mapPixelSize: new Vector2(pixelW, pixelH));

        Assert.True(min < 1f, $"Expected min < 1, got {min}");
        Assert.True(min > 0.5f, $"Expected min > 0.5, got {min}");
    }

    [Fact]
    public void BuildLevels_FiveLevels_EndpointsExact()
    {
        float[] levels = ZoomMath.BuildLevels(zoomMin: 0.7f, count: 5);

        Assert.Equal(5, levels.Length);
        Assert.Equal(0.7f, levels[0], Tolerance);
        Assert.Equal(1f, levels[4], Tolerance);
    }

    [Fact]
    public void BuildLevels_FiveLevels_MidpointIsAverage()
    {
        float[] levels = ZoomMath.BuildLevels(zoomMin: 0.6f, count: 5);

        Assert.Equal(0.8f, levels[2], Tolerance);
    }

    [Fact]
    public void BuildLevels_FiveLevels_StrictlyIncreasing()
    {
        float[] levels = ZoomMath.BuildLevels(zoomMin: 0.5f, count: 5);

        for (int i = 1; i < levels.Length; i++)
        {
            Assert.True(levels[i] > levels[i - 1],
                $"levels[{i}]={levels[i]} not greater than levels[{i - 1}]={levels[i - 1]}");
        }
    }

    [Fact]
    public void BuildLevels_ZoomMinAtOne_AllLevelsAreOne()
    {
        // When the map already fits at 1.0 (small grid), every step is
        // 1.0 and stepping is a no-op.
        float[] levels = ZoomMath.BuildLevels(zoomMin: 1f, count: 5);

        foreach (float v in levels)
        {
            Assert.Equal(1f, v, Tolerance);
        }
    }
}
