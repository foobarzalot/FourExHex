using System;
using Xunit;

namespace FourExHex.Tests;

public class RotatedBoardBoxTests
{
    private const float Tolerance = 0.001f;

    [Fact]
    public void AngleZero_IsUnrotatedScaledBox()
    {
        var (minX, minY, maxX, maxY) = MapPlacement.RotatedBoardBox(100f, 60f, 1f, 0f);
        Assert.Equal(0f, minX, Tolerance);
        Assert.Equal(0f, minY, Tolerance);
        Assert.Equal(100f, maxX, Tolerance);
        Assert.Equal(60f, maxY, Tolerance);
    }

    [Fact]
    public void AngleZero_AppliesZoom()
    {
        var (minX, minY, maxX, maxY) = MapPlacement.RotatedBoardBox(100f, 60f, 2f, 0f);
        Assert.Equal(0f, minX, Tolerance);
        Assert.Equal(0f, minY, Tolerance);
        Assert.Equal(200f, maxX, Tolerance);
        Assert.Equal(120f, maxY, Tolerance);
    }

    [Fact]
    public void Ccw90_SwapsExtentAndSitsAboveOrigin()
    {
        // (x,y) -> (y,-x): corners of [0,100]x[0,60] map to x in [0,60],
        // y in [-100,0]. Width becomes the old height, height the old width.
        var (minX, minY, maxX, maxY) =
            MapPlacement.RotatedBoardBox(100f, 60f, 1f, -MathF.PI / 2f);
        Assert.Equal(0f, minX, Tolerance);
        Assert.Equal(-100f, minY, Tolerance);
        Assert.Equal(60f, maxX, Tolerance);
        Assert.Equal(0f, maxY, Tolerance);
    }

    [Fact]
    public void Ccw90_AppliesZoom()
    {
        var (minX, minY, maxX, maxY) =
            MapPlacement.RotatedBoardBox(100f, 60f, 2f, -MathF.PI / 2f);
        Assert.Equal(0f, minX, Tolerance);
        Assert.Equal(-200f, minY, Tolerance);
        Assert.Equal(120f, maxX, Tolerance);
        Assert.Equal(0f, maxY, Tolerance);
    }

    [Fact]
    public void Cw90_SwapsExtentAndSitsLeftOfOrigin()
    {
        // (x,y) -> (-y,x): corners map to x in [-60,0], y in [0,100].
        // Locks the rotation convention so a sign error is caught.
        var (minX, minY, maxX, maxY) =
            MapPlacement.RotatedBoardBox(100f, 60f, 1f, MathF.PI / 2f);
        Assert.Equal(-60f, minX, Tolerance);
        Assert.Equal(0f, minY, Tolerance);
        Assert.Equal(0f, maxX, Tolerance);
        Assert.Equal(100f, maxY, Tolerance);
    }

    [Fact]
    public void Ccw90_SquareBoardKeepsSquareExtent()
    {
        var (minX, minY, maxX, maxY) =
            MapPlacement.RotatedBoardBox(80f, 80f, 1f, -MathF.PI / 2f);
        Assert.Equal(80f, maxX - minX, Tolerance);
        Assert.Equal(80f, maxY - minY, Tolerance);
    }
}
