using System;
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
            viewportX: 1920f, viewportY: 1080f,
            hudHeight: 60f,
            mapPixelX: 800f, mapPixelY: 600f);

        Assert.Equal(1f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_TallMapInWideViewport_YBinds()
    {
        // 1920x1080 viewport, 60px HUD => 1020 available Y.
        // Map 1000x2040 => Y-fit = 1020/2040 = 0.5; X-fit = 1920/1000 = 1.92.
        float min = ZoomMath.ComputeZoomMin(
            viewportX: 1920f, viewportY: 1080f,
            hudHeight: 60f,
            mapPixelX: 1000f, mapPixelY: 2040f);

        Assert.Equal(0.5f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_WideMapInTallViewport_XBinds()
    {
        // 1000x2000 viewport, 0px HUD. Map 2000x1000 => X-fit = 0.5,
        // Y-fit = 2.0. Min picks X.
        float min = ZoomMath.ComputeZoomMin(
            viewportX: 1000f, viewportY: 2000f,
            hudHeight: 0f,
            mapPixelX: 2000f, mapPixelY: 1000f);

        Assert.Equal(0.5f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_HudHeightSubtractedFromY()
    {
        // Without HUD subtraction, Y-fit would be 1080/1080 = 1.0 and the
        // result would be 1.0 (since X also fits). With a 60px HUD strip
        // taken off, Y-fit becomes 1020/1080 ≈ 0.9444 and binds.
        float min = ZoomMath.ComputeZoomMin(
            viewportX: 2000f, viewportY: 1080f,
            hudHeight: 60f,
            mapPixelX: 1500f, mapPixelY: 1080f);

        Assert.Equal(1020f / 1080f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_FourExHexDefaults_LessThanOne()
    {
        // Sanity check on real game numbers: 30x20 grid, HexSize=48 gives
        // PixelSize ~ (1443, 1488). On a 1920x1080 fullscreen viewport with
        // a 60px HUD strip, the available area is ~1920x1020, so Y binds
        // and the fit-min is well under 1.
        float pixelW = (30 + 0.5f) * (float)Math.Sqrt(3.0) * 48f;
        float pixelH = (1.5f * 20 + 0.5f) * 48f;

        float min = ZoomMath.ComputeZoomMin(
            viewportX: 1920f, viewportY: 1080f,
            hudHeight: 60f,
            mapPixelX: pixelW, mapPixelY: pixelH);

        Assert.True(min < 1f, $"Expected min < 1, got {min}");
        Assert.True(min > 0.5f, $"Expected min > 0.5, got {min}");
    }

    [Fact]
    public void ComputeZoomMin_GraceDividesTheFit()
    {
        // Raw fit: X binds at 1000/2000 = 0.5 (Y-fit = 2000/1000 = 2.0).
        // Grace 1.2 lowers the floor to 0.5/1.2 so the map sits with margin.
        float min = ZoomMath.ComputeZoomMin(
            viewportX: 1000f, viewportY: 2000f,
            hudHeight: 0f,
            mapPixelX: 2000f, mapPixelY: 1000f,
            zoomOutGrace: 1.2f);

        Assert.Equal(0.5f / 1.2f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_GraceStillCappedAtOne()
    {
        // Map far smaller than the viewport: raw fit = 1920/800 vs 1020/600
        // => 1.7, and 1.7/1.2 > 1, so the 1.0 cap binds — a tiny grid that
        // already fits comfortably gains no zoom-out from the grace.
        float min = ZoomMath.ComputeZoomMin(
            viewportX: 1920f, viewportY: 1080f,
            hudHeight: 60f,
            mapPixelX: 800f, mapPixelY: 600f,
            zoomOutGrace: 1.2f);

        Assert.Equal(1f, min, Tolerance);
    }

    [Fact]
    public void ComputeZoomMin_GraceWithPartialSlack_AllowsSlightZoomOut()
    {
        // Raw fit = 1100/1000 = 1.1 (Y has more slack): the map already fits
        // at 1x, but with less than the grace's worth of slack, so the floor
        // drops to 1.1/1.2 ≈ 0.9167 — a small zoom-out is allowed.
        float min = ZoomMath.ComputeZoomMin(
            viewportX: 1100f, viewportY: 3000f,
            hudHeight: 0f,
            mapPixelX: 1000f, mapPixelY: 1000f,
            zoomOutGrace: 1.2f);

        Assert.Equal(1.1f / 1.2f, min, Tolerance);
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

    [Fact]
    public void PinchZoom_FingersSpreadApart_ZoomsIn()
    {
        // Fingers moved from 100px apart to 150px apart => 1.5x scale.
        float zoom = ZoomMath.PinchZoom(currentZoom: 0.6f, prevDist: 100f, curDist: 150f);

        Assert.Equal(0.9f, zoom, Tolerance);
    }

    [Fact]
    public void PinchZoom_FingersPinchTogether_ZoomsOut()
    {
        // Fingers moved from 200px apart to 100px apart => 0.5x scale.
        float zoom = ZoomMath.PinchZoom(currentZoom: 0.8f, prevDist: 200f, curDist: 100f);

        Assert.Equal(0.4f, zoom, Tolerance);
    }

    [Fact]
    public void PinchZoom_DistanceUnchanged_NoOp()
    {
        float zoom = ZoomMath.PinchZoom(currentZoom: 0.7f, prevDist: 120f, curDist: 120f);

        Assert.Equal(0.7f, zoom, Tolerance);
    }

    [Fact]
    public void PinchZoom_PrevDistZeroOrNegative_ReturnsCurrentUnchanged()
    {
        // A degenerate seed distance (fingers at the same point, or an
        // uninitialized value) must not divide-by-zero or blow the zoom up.
        Assert.Equal(0.7f, ZoomMath.PinchZoom(0.7f, prevDist: 0f, curDist: 150f), Tolerance);
        Assert.Equal(0.7f, ZoomMath.PinchZoom(0.7f, prevDist: -10f, curDist: 150f), Tolerance);
    }

    // ---- ClosestLevelIndex ----------------------------------------------

    private static readonly float[] Levels = { 0.5f, 0.625f, 0.75f, 0.875f, 1f };

    [Fact]
    public void ClosestLevelIndex_ExactMatch_ReturnsThatIndex()
    {
        Assert.Equal(0, ZoomMath.ClosestLevelIndex(Levels, 0.5f));
        Assert.Equal(2, ZoomMath.ClosestLevelIndex(Levels, 0.75f));
        Assert.Equal(4, ZoomMath.ClosestLevelIndex(Levels, 1f));
    }

    [Fact]
    public void ClosestLevelIndex_BetweenStops_PicksNearest()
    {
        Assert.Equal(1, ZoomMath.ClosestLevelIndex(Levels, 0.6f));   // 0.6 closer to 0.625
        Assert.Equal(3, ZoomMath.ClosestLevelIndex(Levels, 0.83f));  // closer to 0.875
    }

    [Fact]
    public void ClosestLevelIndex_TiePrefersLowerIndex()
    {
        // 0.5625 is exactly between 0.5 (idx 0) and 0.625 (idx 1); strict-less
        // keeps the earlier (lower) index.
        Assert.Equal(0, ZoomMath.ClosestLevelIndex(Levels, 0.5625f));
    }

    [Fact]
    public void ClosestLevelIndex_OutsideRange_ClampsToEnds()
    {
        Assert.Equal(0, ZoomMath.ClosestLevelIndex(Levels, 0.1f));
        Assert.Equal(4, ZoomMath.ClosestLevelIndex(Levels, 5f));
    }

    // ---- StepLevel -------------------------------------------------------

    [Fact]
    public void StepLevel_MovesOneStopFromNearest()
    {
        Assert.Equal(3, ZoomMath.StepLevel(Levels, 0.75f, +1));  // idx 2 -> 3
        Assert.Equal(1, ZoomMath.StepLevel(Levels, 0.75f, -1));  // idx 2 -> 1
    }

    [Fact]
    public void StepLevel_StartsFromNearestWhenOffStop()
    {
        // 0.6 nearest is idx 1 (0.625); +1 -> 2.
        Assert.Equal(2, ZoomMath.StepLevel(Levels, 0.6f, +1));
    }

    [Fact]
    public void StepLevel_ClampsAtEnds()
    {
        Assert.Equal(0, ZoomMath.StepLevel(Levels, 0.5f, -1));   // already at bottom
        Assert.Equal(4, ZoomMath.StepLevel(Levels, 1f, +1));     // already at top
        Assert.Equal(0, ZoomMath.StepLevel(Levels, 0.75f, -10)); // big step floors at 0
        Assert.Equal(4, ZoomMath.StepLevel(Levels, 0.75f, +10)); // big step caps at last
    }

    [Fact]
    public void StepLevel_ZeroDelta_ReturnsNearest()
    {
        Assert.Equal(2, ZoomMath.StepLevel(Levels, 0.74f, 0));
    }
}
