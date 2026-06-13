using Xunit;

namespace FourExHex.Tests;

public class MultiTouchTapDetectorTests
{
    [Fact]
    public void ThirdConcurrentTouch_Fires()
    {
        var detector = new MultiTouchTapDetector();
        Assert.False(detector.Press(0));
        Assert.False(detector.Press(1));
        Assert.True(detector.Press(2));
    }

    [Fact]
    public void TwoTouches_DoesNotFire()
    {
        var detector = new MultiTouchTapDetector();
        Assert.False(detector.Press(0));
        Assert.False(detector.Press(1));
        detector.Release(0);
        detector.Release(1);
        Assert.False(detector.Press(0));
    }

    [Fact]
    public void FourthFinger_DoesNotRefire()
    {
        var detector = new MultiTouchTapDetector();
        detector.Press(0);
        detector.Press(1);
        Assert.True(detector.Press(2));
        Assert.False(detector.Press(3));
    }

    [Fact]
    public void RearmsOnlyAfterAllTouchesReleased()
    {
        var detector = new MultiTouchTapDetector();
        detector.Press(0);
        detector.Press(1);
        Assert.True(detector.Press(2));

        // Two fingers lift, one stays down; pressing back up to three
        // must not fire again mid-gesture.
        detector.Release(2);
        detector.Release(1);
        Assert.False(detector.Press(1));
        Assert.False(detector.Press(2));

        // Full release re-arms the detector.
        detector.Release(0);
        detector.Release(1);
        detector.Release(2);
        Assert.False(detector.Press(0));
        Assert.False(detector.Press(1));
        Assert.True(detector.Press(2));
    }

    [Fact]
    public void StrayRelease_IsIgnored()
    {
        var detector = new MultiTouchTapDetector();
        detector.Release(7);
        Assert.False(detector.Press(0));
        Assert.False(detector.Press(1));
        Assert.True(detector.Press(2));
    }

    [Fact]
    public void RepeatPressOfSameIndex_CountsOnce()
    {
        var detector = new MultiTouchTapDetector();
        Assert.False(detector.Press(0));
        Assert.False(detector.Press(0));
        Assert.False(detector.Press(0));
        Assert.False(detector.Press(1));
        Assert.True(detector.Press(2));
    }
}
