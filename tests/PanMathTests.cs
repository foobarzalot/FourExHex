using Xunit;

namespace FourExHex.Tests;

public class PanMathTests
{
    private const float Tolerance = 0.0001f;

    // ---- VisualCenter ----------------------------------------------------

    [Fact]
    public void VisualCenter_SymmetricInsets_CentersInRemainingArea()
    {
        // 1920x1080, 60 top + 60 bottom => availY 960, center at 60 + 480 = 540.
        (float x, float y) = PanMath.VisualCenter(1920f, 1080f, 60f, 60f);
        Assert.Equal(960f, x, Tolerance);
        Assert.Equal(540f, y, Tolerance);
    }

    [Fact]
    public void VisualCenter_AsymmetricInsets_ShiftsTowardLargerBottom()
    {
        // 1000x1000, 200 top + 0 bottom => availY 800, center y = 200 + 400 = 600.
        (float x, float y) = PanMath.VisualCenter(1000f, 1000f, 200f, 0f);
        Assert.Equal(500f, x, Tolerance);
        Assert.Equal(600f, y, Tolerance);
    }

    [Fact]
    public void VisualCenter_ZeroInsets_IsGeometricCenter()
    {
        (float x, float y) = PanMath.VisualCenter(800f, 600f, 0f, 0f);
        Assert.Equal(400f, x, Tolerance);
        Assert.Equal(300f, y, Tolerance);
    }

    // ---- Clamp: centered-axis lock (board+pad smaller than available) ----

    [Fact]
    public void Clamp_BoardSmallerThanAvailOnBothAxes_LocksBothToCentered()
    {
        // vp 1920x1080, no insets. Board box [0,0,400,300], no pad.
        // boxW 400 <= 1920 => x = (1920-400)/2 - 0 = 760.
        // boxH 300 <= 1080 => y = 0 + (1080-300)/2 - 0 = 390.
        (float x, float y) = PanMath.Clamp(
            desiredX: 9999f, desiredY: 9999f,
            vpWidth: 1920f, vpHeight: 1080f, topInset: 0f, bottomInset: 0f,
            boxMinX: 0f, boxMinY: 0f, boxMaxX: 400f, boxMaxY: 300f,
            scaledPad: 0f);
        Assert.Equal(760f, x, Tolerance);
        Assert.Equal(390f, y, Tolerance);
    }

    [Fact]
    public void Clamp_LockedAxisIgnoresDesired()
    {
        // Same locked board; a wildly different desired must not change the result.
        (float x1, float y1) = PanMath.Clamp(
            -5000f, -5000f, 1920f, 1080f, 0f, 0f, 0f, 0f, 400f, 300f, 0f);
        (float x2, float y2) = PanMath.Clamp(
            5000f, 5000f, 1920f, 1080f, 0f, 0f, 0f, 0f, 400f, 300f, 0f);
        Assert.Equal(x1, x2, Tolerance);
        Assert.Equal(y1, y2, Tolerance);
    }

    [Fact]
    public void Clamp_CenteredLockRespectsTopInset()
    {
        // vp 1000x1000, top 100 bottom 0 => availY 900. Board box [0,0,200,200].
        // y = topInset + (availY - boxH)/2 - minY = 100 + (900-200)/2 - 0 = 450.
        (float _, float y) = PanMath.Clamp(
            0f, 0f, 1000f, 1000f, 100f, 0f, 0f, 0f, 200f, 200f, 0f);
        Assert.Equal(450f, y, Tolerance);
    }

    // ---- Clamp: range branch (board larger than available) ---------------

    [Fact]
    public void Clamp_BoardWiderThanAvail_ClampsDesiredIntoRange()
    {
        // vp 800 wide, board box [0,0,2000,300]. boxW 2000 > 800 => clamp branch.
        // x range = [availX - maxX, -minX] = [800-2000, -0] = [-1200, 0].
        // desired 500 -> clamped to 0 (upper); desired -3000 -> -1200 (lower);
        // desired -600 -> -600 (in range).
        Assert.Equal(0f,
            PanMath.Clamp(500f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 0f).x, Tolerance);
        Assert.Equal(-1200f,
            PanMath.Clamp(-3000f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 0f).x, Tolerance);
        Assert.Equal(-600f,
            PanMath.Clamp(-600f, 0f, 800f, 1080f, 0f, 0f, 0f, 0f, 2000f, 300f, 0f).x, Tolerance);
    }

    [Fact]
    public void Clamp_BoardTallerThanAvail_ClampsYWithTopInset()
    {
        // vp 1000x600, top 100 bottom 0 => availY 500. Board box [0,0,200,2000].
        // boxH 2000 > 500 => clamp branch.
        // y range = [topInset + availY - maxY, topInset - minY]
        //         = [100 + 500 - 2000, 100 - 0] = [-1400, 100].
        Assert.Equal(100f,
            PanMath.Clamp(0f, 9999f, 1000f, 600f, 100f, 0f, 0f, 0f, 200f, 2000f, 0f).y, Tolerance);
        Assert.Equal(-1400f,
            PanMath.Clamp(0f, -9999f, 1000f, 600f, 100f, 0f, 0f, 0f, 200f, 2000f, 0f).y, Tolerance);
        Assert.Equal(-500f,
            PanMath.Clamp(0f, -500f, 1000f, 600f, 100f, 0f, 0f, 0f, 200f, 2000f, 0f).y, Tolerance);
    }

    // ---- Pad overflow: fits without pad, not with pad --------------------

    [Fact]
    public void Clamp_PadFlipsCenteredAxisIntoClampBranch()
    {
        // vp 1000 wide. Board box [0,0,900,300] fits without pad (900<=1000),
        // locked & centered at (1000-900)/2 = 50.
        (float xNoPad, float _) = PanMath.Clamp(
            -5000f, 0f, 1000f, 1080f, 0f, 0f, 0f, 0f, 900f, 300f, 0f);
        Assert.Equal(50f, xNoPad, Tolerance);

        // With pad 100 on each side: box becomes [-100,-100,1000,400], boxW 1100 > 1000
        // => clamp branch. x range = [availX - maxX, -minX] = [1000-1000, 100] = [0, 100].
        // desired -5000 -> 0 (lower).
        (float xPad, float __) = PanMath.Clamp(
            -5000f, 0f, 1000f, 1080f, 0f, 0f, 0f, 0f, 900f, 300f, 100f);
        Assert.Equal(0f, xPad, Tolerance);
    }

    // ---- Rotation: 90-degree board box (swapped extents, negative origin) -

    [Fact]
    public void Clamp_RotatedBox_NegativeOriginCentersCorrectly()
    {
        // A board rotated +90deg yields a box like [-300,0,0,500] (negative minX).
        // vp 1920x1080, no insets, no pad. boxW = 0-(-300) = 300 <= 1920 =>
        // x = (1920-300)/2 - (-300) = 810 + 300 = 1110.
        // boxH = 500 <= 1080 => y = (1080-500)/2 - 0 = 290.
        (float x, float y) = PanMath.Clamp(
            0f, 0f, 1920f, 1080f, 0f, 0f,
            boxMinX: -300f, boxMinY: 0f, boxMaxX: 0f, boxMaxY: 500f,
            scaledPad: 0f);
        Assert.Equal(1110f, x, Tolerance);
        Assert.Equal(290f, y, Tolerance);
    }

    [Fact]
    public void Clamp_RotatedBoxLargerThanViewport_ClampsWithNegativeExtents()
    {
        // Rotated tall box [-100,-50,1900,1100] in a 800x600 vp, no insets, no pad.
        // boxW = 1900-(-100) = 2000 > 800 => x range = [800-1900, 100] = [-1100, 100].
        // desired -5000 -> -1100; desired 5000 -> 100.
        Assert.Equal(-1100f,
            PanMath.Clamp(-5000f, 0f, 800f, 600f, 0f, 0f, -100f, -50f, 1900f, 1100f, 0f).x, Tolerance);
        Assert.Equal(100f,
            PanMath.Clamp(5000f, 0f, 800f, 600f, 0f, 0f, -100f, -50f, 1900f, 1100f, 0f).x, Tolerance);
    }
}
