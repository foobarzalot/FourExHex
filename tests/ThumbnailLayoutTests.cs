using Xunit;

namespace FourExHex.Tests;

// Pure "contain" fit for the map thumbnail's offscreen SubViewport:
// ThumbnailLayout.FitInside returns the largest box with the content's aspect
// ratio that fits inside the target rect. Used to size the render viewport so
// the board snapshot is framed without distortion.
public class ThumbnailLayoutTests
{
    private const float Tol = 0.001f;

    [Fact]
    public void WideContent_ClampsToWidth()
    {
        // 4:1 content into a 200×200 box → width-limited: 200×50.
        (float w, float h) = ThumbnailLayout.FitInside(400f, 100f, 200f, 200f);
        Assert.Equal(200f, w, Tol);
        Assert.Equal(50f, h, Tol);
    }

    [Fact]
    public void TallContent_ClampsToHeight()
    {
        // 1:4 content into a 200×200 box → height-limited: 50×200.
        (float w, float h) = ThumbnailLayout.FitInside(100f, 400f, 200f, 200f);
        Assert.Equal(50f, w, Tol);
        Assert.Equal(200f, h, Tol);
    }

    [Fact]
    public void MatchingAspect_FillsExactly()
    {
        // 3:2 content into a 3:2 box scales uniformly to fill it.
        (float w, float h) = ThumbnailLayout.FitInside(30f, 20f, 300f, 200f);
        Assert.Equal(300f, w, Tol);
        Assert.Equal(200f, h, Tol);
    }

    [Fact]
    public void BoardAspect_FitsWithinRectAndPreservesAspect()
    {
        // The 30×20 board (~720×640 px content) into a 240×180 thumbnail rect:
        // result must fit inside and keep the content aspect ratio.
        (float w, float h) = ThumbnailLayout.FitInside(720f, 640f, 240f, 180f);
        Assert.True(w <= 240f + Tol && h <= 180f + Tol);
        Assert.Equal(720f / 640f, w / h, Tol);
        // Content is wider-than-rect-relative? 720/640=1.125, 240/180=1.333 →
        // height-limited: h=180, w=180*1.125=202.5.
        Assert.Equal(180f, h, Tol);
        Assert.Equal(202.5f, w, Tol);
    }

    [Fact]
    public void OrientedFit_Landscape_MatchesPlainFit()
    {
        // Landscape passes the grid box through unchanged.
        (float w, float h) = ThumbnailLayout.OrientedFit(
            400f, 100f, portrait: false, 200f, 200f);
        Assert.Equal(200f, w, Tol);
        Assert.Equal(50f, h, Tol);
    }

    [Fact]
    public void OrientedFit_Portrait_SwapsGridAspect()
    {
        // Portrait swaps the grid box to a tall aspect (the HexMapView
        // inside a tall viewport rotates the board −90°, matching the
        // in-game portrait map): 400×100 grid → 100×400 content → 50×200.
        (float w, float h) = ThumbnailLayout.OrientedFit(
            400f, 100f, portrait: true, 200f, 200f);
        Assert.Equal(50f, w, Tol);
        Assert.Equal(200f, h, Tol);
    }

    [Theory]
    [InlineData(0f, 100f, 200f, 200f)]
    [InlineData(100f, 0f, 200f, 200f)]
    [InlineData(100f, 100f, 0f, 200f)]
    [InlineData(100f, 100f, 200f, 0f)]
    [InlineData(-5f, 100f, 200f, 200f)]
    public void DegenerateInput_ReturnsZero(float cw, float ch, float mw, float mh)
    {
        (float w, float h) = ThumbnailLayout.FitInside(cw, ch, mw, mh);
        Assert.Equal(0f, w, Tol);
        Assert.Equal(0f, h, Tol);
    }
}
