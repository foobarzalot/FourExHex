// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Xunit;

namespace FourExHex.Tests;

public class FogBackdropMathTests
{
    private const float ZoomOutGrace = 1.2f;  // HexMapView.ZoomOutGrace
    private const float ScrollPad = 600f;     // HexMapView.ScrollPaddingPx

    [Fact]
    public void HalfExtent_IsDiagonalPlusViewportOverZoomMinPlusPad()
    {
        // hypot(300,400)=500; max(1000,500)/0.5 = 2000; +600.
        float half = FogBackdropMath.HalfExtent(300f, 400f, 1000f, 500f, 0.5f, 600f);
        Assert.Equal(3100f, half, 0.001f);
    }

    // The invariant the backdrop exists for: at every pan-clamp extreme, at
    // both the zoom floor and 1×, every viewport corner mapped into
    // board-local space lands inside the square — so no viewport pixel is
    // left to the clear colour.
    [Theory]
    // landscape phone/desktop, HUD insets, no rotation
    [InlineData(1200f, 750f, 60f, 60f, 1400f, 900f, 0f)]
    // portrait phone, rotated board (90°)
    [InlineData(425f, 920f, 60f, 80f, 1400f, 900f, MathF.PI / 2f)]
    // small square (thumbnail-ish), odd angle
    [InlineData(300f, 300f, 0f, 0f, 1400f, 900f, 0.65f)]
    // tall board, landscape viewport, no insets
    [InlineData(1920f, 1080f, 0f, 0f, 600f, 1100f, 0f)]
    // board far smaller than the viewport (zoomMin capped at 1×)
    [InlineData(1920f, 1080f, 60f, 60f, 200f, 150f, 0f)]
    public void HalfExtent_CoversEveryViewportCornerAtEveryPanExtreme(
        float vpW, float vpH, float topInset, float bottomInset,
        float boardW, float boardH, float angle)
    {
        (float fMinX, float fMinY, float fMaxX, float fMaxY) =
            MapPlacement.RotatedBoardBox(boardW, boardH, 1f, angle);
        float zoomMin = ZoomMath.ComputeZoomMin(
            vpW, vpH, topInset + bottomInset, fMaxX - fMinX, fMaxY - fMinY, ZoomOutGrace);
        float half = FogBackdropMath.HalfExtent(boardW, boardH, vpW, vpH, zoomMin, ScrollPad);
        float cx = boardW * 0.5f, cy = boardH * 0.5f;

        foreach (float zoom in new[] { zoomMin, 1f })
        {
            (float minX, float minY, float maxX, float maxY) =
                MapPlacement.RotatedBoardBox(boardW, boardH, zoom, angle);
            float pad = ScrollPad * zoom;
            foreach (float sx in new[] { -1e6f, 1e6f })
            foreach (float sy in new[] { -1e6f, 1e6f })
            {
                (float px, float py) = PanMath.Clamp(
                    sx, sy, vpW, vpH, topInset, bottomInset, minX, minY, maxX, maxY, pad);
                foreach ((float vx, float vy) in new[] { (0f, 0f), (vpW, 0f), (0f, vpH), (vpW, vpH) })
                {
                    // Invert the node transform: global = P + R(angle)·(zoom·local).
                    float dx = vx - px, dy = vy - py;
                    float cos = MathF.Cos(-angle), sin = MathF.Sin(-angle);
                    float lx = (dx * cos - dy * sin) / zoom;
                    float ly = (dx * sin + dy * cos) / zoom;
                    Assert.True(MathF.Abs(lx - cx) <= half && MathF.Abs(ly - cy) <= half,
                        $"corner ({vx},{vy}) at pos ({px:0},{py:0}) zoom {zoom:0.000} " +
                        $"maps to local ({lx:0},{ly:0}), outside half-extent {half:0} " +
                        $"around ({cx},{cy})");
                }
            }
        }
    }
}
