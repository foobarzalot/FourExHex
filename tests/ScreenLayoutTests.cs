using Xunit;

namespace FourExHex.Tests;

public class ScreenLayoutTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void Resolve_WiderThanTall_IsLandscape()
    {
        Assert.Equal(ScreenOrientation.Landscape, ScreenLayout.Resolve(1280f, 720f));
    }

    [Fact]
    public void Resolve_TallerThanWide_IsPortrait()
    {
        Assert.Equal(ScreenOrientation.Portrait, ScreenLayout.Resolve(720f, 1280f));
    }

    [Fact]
    public void Resolve_Square_TieGoesToLandscape()
    {
        Assert.Equal(ScreenOrientation.Landscape, ScreenLayout.Resolve(1000f, 1000f));
    }

    [Fact]
    public void ComputeInsets_Landscape_BottomBarOnly_TopZero()
    {
        MapInsets insets = ScreenLayout.ComputeInsets(
            ScreenOrientation.Landscape,
            topBarVisible: true,
            landscapeBarHeight: 96f,
            portraitTopBarHeight: 80f,
            portraitBottomBarHeight: 110f);

        Assert.Equal(0f, insets.Top, Tolerance);
        Assert.Equal(96f, insets.Bottom, Tolerance);
    }

    [Fact]
    public void ComputeInsets_Landscape_IgnoresTopBarVisibleFlag()
    {
        // Landscape never hides its bar, so topBarVisible is irrelevant there.
        MapInsets insets = ScreenLayout.ComputeInsets(
            ScreenOrientation.Landscape,
            topBarVisible: false,
            landscapeBarHeight: 96f,
            portraitTopBarHeight: 80f,
            portraitBottomBarHeight: 110f);

        Assert.Equal(0f, insets.Top, Tolerance);
        Assert.Equal(96f, insets.Bottom, Tolerance);
    }

    [Fact]
    public void ComputeInsets_Portrait_TopBarVisible_ReservesBoth()
    {
        MapInsets insets = ScreenLayout.ComputeInsets(
            ScreenOrientation.Portrait,
            topBarVisible: true,
            landscapeBarHeight: 96f,
            portraitTopBarHeight: 80f,
            portraitBottomBarHeight: 110f);

        Assert.Equal(80f, insets.Top, Tolerance);
        Assert.Equal(110f, insets.Bottom, Tolerance);
    }

    [Fact]
    public void ComputeInsets_Portrait_TopBarHidden_TopIsZero()
    {
        // No territory selected → top bar hidden → only the bottom bar
        // reserves space.
        MapInsets insets = ScreenLayout.ComputeInsets(
            ScreenOrientation.Portrait,
            topBarVisible: false,
            landscapeBarHeight: 96f,
            portraitTopBarHeight: 80f,
            portraitBottomBarHeight: 110f);

        Assert.Equal(0f, insets.Top, Tolerance);
        Assert.Equal(110f, insets.Bottom, Tolerance);
    }
}
