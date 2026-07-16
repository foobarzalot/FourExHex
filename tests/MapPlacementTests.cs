// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Xunit;

namespace FourExHex.Tests;

public class MapPlacementTests
{
    private const float Tolerance = 0.001f;

    // ---- BoxCenter -------------------------------------------------------

    [Fact]
    public void BoxCenter_ReturnsMidpoint()
    {
        (float x, float y) = MapPlacement.BoxCenter(0f, 0f, 400f, 300f);
        Assert.Equal(200f, x, Tolerance);
        Assert.Equal(150f, y, Tolerance);
    }

    [Fact]
    public void BoxCenter_OffsetBox()
    {
        (float x, float y) = MapPlacement.BoxCenter(83f, 72f, 2453f, 1392f);
        Assert.Equal(1268f, x, Tolerance);
        Assert.Equal(732f, y, Tolerance);
    }

    [Fact]
    public void BoxCenter_NegativeExtents_CentersAtOrigin()
    {
        (float x, float y) = MapPlacement.BoxCenter(-100f, -50f, 100f, 50f);
        Assert.Equal(0f, x, Tolerance);
        Assert.Equal(0f, y, Tolerance);
    }

    // ---- ToWorldOffset ---------------------------------------------------

    [Fact]
    public void ToWorldOffset_AngleZero_ScalesByZoomOnly()
    {
        (float x, float y) = MapPlacement.ToWorldOffset(10f, 20f, zoom: 2f, angleRad: 0f);
        Assert.Equal(20f, x, Tolerance);
        Assert.Equal(40f, y, Tolerance);
    }

    [Fact]
    public void ToWorldOffset_NinetyDegrees_RotatesCcwConvention()
    {
        // Godot Vector2.Rotated: (x·cos − y·sin, x·sin + y·cos).
        // +90deg: (10,0) -> (0,10); (0,10) -> (-10,0).
        (float ax, float ay) = MapPlacement.ToWorldOffset(10f, 0f, 1f, MathF.PI / 2f);
        Assert.Equal(0f, ax, Tolerance);
        Assert.Equal(10f, ay, Tolerance);

        (float bx, float by) = MapPlacement.ToWorldOffset(0f, 10f, 1f, MathF.PI / 2f);
        Assert.Equal(-10f, bx, Tolerance);
        Assert.Equal(0f, by, Tolerance);
    }

    [Fact]
    public void ToWorldOffset_PortraitMinusNinety_WithZoom()
    {
        // -90deg (portrait board rotation) at zoom 2: (5,0) -> scaled (10,0) -> (0,-10).
        (float x, float y) = MapPlacement.ToWorldOffset(5f, 0f, 2f, -MathF.PI / 2f);
        Assert.Equal(0f, x, Tolerance);
        Assert.Equal(-10f, y, Tolerance);
    }
}
