using Xunit;

namespace FourExHex.Tests;

// Pure horizontal-swipe recognition for the paged overlays (guided tour,
// Instructions panel): Press records the start point, Release returns the
// verdict — Left/Right when the drag is long enough and mostly horizontal
// (page-turning: left = Next, right = Back at the call sites).
public class SwipeDetectorTests
{
    [Fact]
    public void LeftSwipe_AtThreshold_FiresLeft()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Left,
            d.Release(200f - SwipeDetector.MinDistancePx, 100f));
    }

    [Fact]
    public void RightSwipe_AtThreshold_FiresRight()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Right,
            d.Release(200f + SwipeDetector.MinDistancePx, 100f));
    }

    [Fact]
    public void ShortDrag_IsNone()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.None,
            d.Release(200f - (SwipeDetector.MinDistancePx - 1f), 100f));
    }

    [Fact]
    public void VerticalDrag_IsNone()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.None, d.Release(200f, 300f));
    }

    [Fact]
    public void DiagonalDrag_WithoutHorizontalDominance_IsNone()
    {
        // dx = 80 but dy = 50: |dx| < 2*|dy| → not a horizontal swipe.
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.None, d.Release(120f, 150f));
    }

    [Fact]
    public void DiagonalDrag_WithHorizontalDominance_Fires()
    {
        // dx = -80, dy = 30: |dx| >= 2*|dy| → left swipe.
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Left, d.Release(120f, 130f));
    }

    [Fact]
    public void ReleaseWithoutPress_IsNone()
    {
        var d = new SwipeDetector();
        Assert.Equal(SwipeDirection.None, d.Release(0f, 0f));
    }

    [Fact]
    public void ReleaseDisarms_SecondReleaseIsNone()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        Assert.Equal(SwipeDirection.Left, d.Release(100f, 100f));
        Assert.Equal(SwipeDirection.None, d.Release(0f, 100f));
    }

    [Fact]
    public void Cancel_Disarms()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        d.Cancel();
        Assert.Equal(SwipeDirection.None, d.Release(100f, 100f));
    }

    [Fact]
    public void RearmsOnNextPress()
    {
        var d = new SwipeDetector();
        d.Press(200f, 100f);
        d.Cancel();
        d.Press(300f, 100f);
        Assert.Equal(SwipeDirection.Right, d.Release(400f, 100f));
    }
}
