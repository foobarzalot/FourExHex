// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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

    // RotatedRectBox generalizes RotatedBoardBox to an arbitrary (offset) rect,
    // for content-aware clamping where the content isn't anchored at origin.

    [Fact]
    public void RectBox_AngleZero_ScalesOffsetRect()
    {
        var (minX, minY, maxX, maxY) =
            MapPlacement.RotatedRectBox(2f, 3f, 5f, 7f, 2f, 0f);
        Assert.Equal(4f, minX, Tolerance);
        Assert.Equal(6f, minY, Tolerance);
        Assert.Equal(10f, maxX, Tolerance);
        Assert.Equal(14f, maxY, Tolerance);
    }

    [Fact]
    public void RectBox_Ccw90_RotatesOffsetRect()
    {
        // (x,y) -> (y,-x): corners of [2,5]x[3,7] map to x in [3,7], y in [-5,-2].
        var (minX, minY, maxX, maxY) =
            MapPlacement.RotatedRectBox(2f, 3f, 5f, 7f, 1f, -MathF.PI / 2f);
        Assert.Equal(3f, minX, Tolerance);
        Assert.Equal(-5f, minY, Tolerance);
        Assert.Equal(7f, maxX, Tolerance);
        Assert.Equal(-2f, maxY, Tolerance);
    }
}
