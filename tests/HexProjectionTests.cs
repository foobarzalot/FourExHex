// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Xunit;

namespace FourExHex.Tests;

// Pointy-top pixel↔axial projection: HexProjection is the single home for the
// board-geometry constants that HexPixel (the Godot Vector2 shim), MapPlacement,
// and HexMapView all consume. The expected values here are hand-derived from the
// pointy-top layout — a hex of radius s is √3·s wide and 2·s tall, rows pitch
// 1.5·s apart, and each row shifts half a width per r — NOT read back off the
// implementation. Cube-rounding itself is pinned separately in HexPixelTests.
public class HexProjectionTests
{
    private const float Tol = 0.001f;
    private static readonly float Sqrt3 = MathF.Sqrt(3f);

    // ---- ToPixel: known forward values ----

    // Expected center is (xMul · √3 · s, yMul · s): one full hex-width per q,
    // half a width per r, and 1.5·s of vertical pitch per r.
    [Theory]
    [InlineData(0, 0, 0f, 0f)]
    [InlineData(1, 0, 1f, 0f)]
    [InlineData(0, 1, 0.5f, 1.5f)]
    [InlineData(1, -1, 0.5f, -1.5f)]
    [InlineData(-1, 0, -1f, 0f)]
    [InlineData(0, -1, -0.5f, -1.5f)]
    [InlineData(2, 3, 3.5f, 4.5f)]
    public void ToPixel_KnownAxial_MatchesHandDerivedCenter(
        int q, int r, float xMul, float yMul)
    {
        const float s = 32f;
        (float x, float y) = HexProjection.ToPixel(new HexCoord(q, r), s);

        Assert.Equal(xMul * Sqrt3 * s, x, Tol);
        Assert.Equal(yMul * s, y, Tol);
    }

    // ---- FromPixel: round-trip ----

    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    [InlineData(45.5f)]
    public void FromPixel_OfToPixel_RecoversTheHex(float s)
    {
        for (int q = -8; q <= 8; q++)
        {
            for (int r = -8; r <= 8; r++)
            {
                var coord = new HexCoord(q, r);
                (float x, float y) = HexProjection.ToPixel(coord, s);
                Assert.Equal(coord, HexProjection.FromPixel(x, y, s));
            }
        }
    }

    // An exact center-to-center round-trip passes even if the inverse constants
    // are wrong in a way that cancels out. Nudging the pixel well off-center
    // (but still inside the hex) is what actually pins the inverse transform.
    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    [InlineData(45.5f)]
    public void FromPixel_SubHexJitter_StillRecoversTheHex(float s)
    {
        (float halfW, float halfH) = HexProjection.HexExtent(s);
        float dx = 0.4f * halfW;
        float dy = 0.4f * halfH;

        for (int q = -8; q <= 8; q++)
        {
            for (int r = -8; r <= 8; r++)
            {
                var coord = new HexCoord(q, r);
                (float x, float y) = HexProjection.ToPixel(coord, s);

                Assert.Equal(coord, HexProjection.FromPixel(x + dx, y, s));
                Assert.Equal(coord, HexProjection.FromPixel(x - dx, y, s));
                Assert.Equal(coord, HexProjection.FromPixel(x, y + dy, s));
                Assert.Equal(coord, HexProjection.FromPixel(x, y - dy, s));
            }
        }
    }

    // ---- FromPixelFrac: the pre-rounding boundary ----

    // Independent of HexRounding: a pixel at an exact hex center must produce
    // integral fractional axials, so any rounding rule would agree.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, -2)]
    [InlineData(-4, 5)]
    public void FromPixelFrac_AtHexCenter_ReturnsIntegralAxial(int q, int r)
    {
        const float s = 32f;
        (float x, float y) = HexProjection.ToPixel(new HexCoord(q, r), s);
        (float qFrac, float rFrac) = HexProjection.FromPixelFrac(x, y, s);

        Assert.Equal(q, qFrac, Tol);
        Assert.Equal(r, rFrac, Tol);
    }

    // ---- FirstHexCenterOffset / HexExtent ----

    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    public void FirstHexCenterOffset_AnchorsHexZeroZeroAtTheOrigin(float s)
    {
        // The offset exists so the grid's visual bounding box starts at local
        // (0,0): hex (0,0)'s drawn center, minus its own extent, lands exactly
        // on the origin.
        (float offX, float offY) = HexProjection.FirstHexCenterOffset(s);
        (float px, float py) = HexProjection.ToPixel(new HexCoord(0, 0), s);
        (float halfW, float halfH) = HexProjection.HexExtent(s);

        Assert.Equal(0f, offX + px - halfW, Tol);
        Assert.Equal(0f, offY + py - halfH, Tol);
    }

    [Theory]
    [InlineData(12f)]
    [InlineData(32f)]
    public void HexExtent_IsHalfOfThePointyTopFootprint(float s)
    {
        // A pointy-top hex of radius s measures √3·s across and 2·s tall.
        (float halfW, float halfH) = HexProjection.HexExtent(s);

        Assert.Equal(0.5f * Sqrt3 * s, halfW, Tol);
        Assert.Equal(s, halfH, Tol);
    }

    // ---- GridPixelSize ----

    // Pins the formula HexMapView.PixelSize carried inline: a Cols×Rows board
    // spans (Cols + 0.5) hex-widths (the half accounts for the odd-row shift)
    // and (1.5·Rows + 0.5) units of s (row pitch plus the last row's bottom cap).
    [Theory]
    [InlineData(30, 20, 32f)]  // FOUREXHEX_6AI full-mode grid
    [InlineData(18, 13, 32f)]  // FOUREXHEX_6AI_QUICK grid
    [InlineData(1, 1, 12f)]
    public void GridPixelSize_MatchesTheBoardBoundingBox(int cols, int rows, float s)
    {
        (float width, float height) = HexProjection.GridPixelSize(cols, rows, s);

        Assert.Equal((cols + 0.5f) * Sqrt3 * s, width, Tol);
        Assert.Equal((1.5f * rows + 0.5f) * s, height, Tol);
    }
}
