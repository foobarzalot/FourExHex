using Xunit;
using FourExHex.Model;

namespace FourExHex.Tests;

// Pure mapping of a physical-pixel safe-area rect (as Godot reports from
// DisplayServer.GetDisplaySafeArea) to logical-pixel insets the HUD layout
// consumes. The model assembly stays Godot-free; scripts/SafeArea.cs feeds
// these helpers with the live values and forwards the result to HudView.
public class SafeAreaMathTests
{
    private const float Tol = 0.001f;

    [Fact]
    public void DesktopWindow_FullySafe_HasZeroInsets()
    {
        // A typical desktop: the OS reports the safe area equal to the whole
        // window, so every edge yields zero inset.
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 1920, physicalWindowHeight: 1080,
            physicalSafeX: 0, physicalSafeY: 0,
            physicalSafeWidth: 1920, physicalSafeHeight: 1080,
            contentScaleFactor: 1.0f);

        Assert.Equal(LogicalSafeInsets.Zero, insets);
    }

    [Fact]
    public void EmptySafeRect_TreatedAsUnknown_ReturnsZeroInsets()
    {
        // A 0×0 safe rect means we don't have reliable info; better to layout
        // edge-to-edge than to invent insets.
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 1170, physicalWindowHeight: 2532,
            physicalSafeX: 0, physicalSafeY: 0,
            physicalSafeWidth: 0, physicalSafeHeight: 0,
            contentScaleFactor: 2.0f);

        Assert.Equal(LogicalSafeInsets.Zero, insets);
    }

    [Fact]
    public void NonPositiveScaleFactor_ReturnsZeroInsets()
    {
        // Defensive: a misreported scale factor shouldn't produce NaN/∞ insets.
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 1170, physicalWindowHeight: 2532,
            physicalSafeX: 0, physicalSafeY: 100,
            physicalSafeWidth: 1170, physicalSafeHeight: 2300,
            contentScaleFactor: 0f);

        Assert.Equal(LogicalSafeInsets.Zero, insets);
    }

    [Fact]
    public void NotchOnly_PortraitTopInset_OtherEdgesZero()
    {
        // iPhone-like portrait with a 47px-tall notch at the top, no home
        // indicator. ContentScaleFactor=1 so logical == physical insets.
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 1170, physicalWindowHeight: 2532,
            physicalSafeX: 0, physicalSafeY: 47,
            physicalSafeWidth: 1170, physicalSafeHeight: 2485,
            contentScaleFactor: 1.0f);

        Assert.Equal(47f, insets.Top, Tol);
        Assert.Equal(0f, insets.Bottom, Tol);
        Assert.Equal(0f, insets.Left, Tol);
        Assert.Equal(0f, insets.Right, Tol);
    }

    [Fact]
    public void ScaleFactor_DividesPhysicalIntoLogicalPixels()
    {
        // Same 47px physical notch at content-scale 2.0 yields a 23.5 logical
        // inset — that's what the layout shifts the HUD by.
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 1170, physicalWindowHeight: 2532,
            physicalSafeX: 0, physicalSafeY: 47,
            physicalSafeWidth: 1170, physicalSafeHeight: 2485,
            contentScaleFactor: 2.0f);

        Assert.Equal(23.5f, insets.Top, Tol);
        Assert.Equal(0f, insets.Bottom, Tol);
    }

    [Fact]
    public void NotchAndHomeIndicator_PortraitTopAndBottomInsetsBothNonZero()
    {
        // iPhone portrait: 59px notch at top + 34px home indicator at bottom.
        // contentScaleFactor=3 (typical high-DPI iPhone in our scaling regime).
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 1170, physicalWindowHeight: 2532,
            physicalSafeX: 0, physicalSafeY: 59,
            physicalSafeWidth: 1170, physicalSafeHeight: 2532 - 59 - 34,
            contentScaleFactor: 3.0f);

        Assert.Equal(59f / 3f, insets.Top, Tol);
        Assert.Equal(34f / 3f, insets.Bottom, Tol);
        Assert.Equal(0f, insets.Left, Tol);
        Assert.Equal(0f, insets.Right, Tol);
    }

    [Fact]
    public void LandscapeNotchOnLeft_LeftInsetOnly()
    {
        // iPhone rotated so the notch is on the left edge: physical safe rect
        // starts at x=59, width shortened accordingly. Top/bottom safe.
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 2532, physicalWindowHeight: 1170,
            physicalSafeX: 59, physicalSafeY: 0,
            physicalSafeWidth: 2532 - 59 - 34, physicalSafeHeight: 1170,
            contentScaleFactor: 2.0f);

        Assert.Equal(0f, insets.Top, Tol);
        Assert.Equal(0f, insets.Bottom, Tol);
        Assert.Equal(59f / 2f, insets.Left, Tol);
        Assert.Equal(34f / 2f, insets.Right, Tol);
    }

    [Fact]
    public void SafeRectLargerThanWindow_DoesNotProduceNegativeInsets()
    {
        // Defensive: if Godot reports a safe rect that overflows the window
        // (shouldn't happen, but malformed reports do exist), insets clamp
        // at zero rather than going negative and pulling the HUD off-screen.
        LogicalSafeInsets insets = SafeAreaMath.InsetsFor(
            physicalWindowWidth: 1170, physicalWindowHeight: 2532,
            physicalSafeX: -50, physicalSafeY: -50,
            physicalSafeWidth: 1300, physicalSafeHeight: 2700,
            contentScaleFactor: 2.0f);

        Assert.Equal(0f, insets.Top, Tol);
        Assert.Equal(0f, insets.Bottom, Tol);
        Assert.Equal(0f, insets.Left, Tol);
        Assert.Equal(0f, insets.Right, Tol);
    }
}
