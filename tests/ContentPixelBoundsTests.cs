// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Xunit;

namespace FourExHex.Tests;

// Pure bbox math for content-aware map centering: MapPlacement.ContentPixelBounds
// returns the unscaled board-pixel bounding box of a set of hex coords, in the
// SAME local space as HexMapView.PixelSize (origin at 0,0, the first-hex offset
// folded in). Mirrors HexMapView's FirstHexCenterOffset + HexPixel.ToPixel and
// the pointy-top hex extent (width √3·s, height 2·s).
public class ContentPixelBoundsTests
{
    private const float Tol = 0.001f;
    private static readonly float Sqrt3 = MathF.Sqrt(3f);

    [Fact]
    public void SingleOriginHex_BoxStartsAtZero()
    {
        // Hex (0,0) at size s: center = (0.5√3 s, s); extent ±(0.5√3 s, s).
        // So the tight box is (0, 0, √3 s, 2 s) — anchored at the origin.
        const float s = 2f;
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.ContentPixelBounds(new[] { new HexCoord(0, 0) }, s);

        Assert.Equal(0f, minX, Tol);
        Assert.Equal(0f, minY, Tol);
        Assert.Equal(Sqrt3 * s, maxX, Tol);
        Assert.Equal(2f * s, maxY, Tol);
    }

    [Fact]
    public void TwoAdjacentHexes_WidenTheBox()
    {
        // Adding (1,0) extends the box one hex-step to the right.
        const float s = 2f;
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.ContentPixelBounds(
                new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, s);

        Assert.Equal(0f, minX, Tol);
        Assert.Equal(0f, minY, Tol);
        Assert.Equal(2f * Sqrt3 * s, maxX, Tol);
        Assert.Equal(2f * s, maxY, Tol);
    }

    [Fact]
    public void OffsetCluster_BoxIsNotAnchoredAtOrigin()
    {
        // The whole point of content-aware centering: a cluster far from the
        // axial origin produces a box with a large min, not one starting at 0.
        const float s = 2f;
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.ContentPixelBounds(new[] { new HexCoord(5, 8) }, s);

        // center.x = 0.5√3 s + √3 s (5 + 4) = √3 s · 9.5 ; min/max ± 0.5√3 s
        Assert.Equal(Sqrt3 * s * 9f, minX, Tol);        // 9.5 - 0.5
        Assert.Equal(Sqrt3 * s * 10f, maxX, Tol);       // 9.5 + 0.5
        // center.y = s + 1.5 s · 8 = 13 s ; ± s → [12 s, 14 s]
        Assert.Equal(12f * s, minY, Tol);
        Assert.Equal(14f * s, maxY, Tol);
    }

    [Fact]
    public void Empty_ReturnsZeroBox()
    {
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.ContentPixelBounds(Array.Empty<HexCoord>(), 48f);

        Assert.Equal(0f, minX, Tol);
        Assert.Equal(0f, minY, Tol);
        Assert.Equal(0f, maxX, Tol);
        Assert.Equal(0f, maxY, Tol);
    }
}
