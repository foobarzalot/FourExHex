// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Sizing for the Fog Of War backdrop: the opaque fog-coloured square
/// <c>HexMapView</c> paints under every map layer while fog is active, so the
/// viewport clear colour (the sea) can never show past the baked water rim at
/// any zoom or pan. Board-local, centred on the nominal grid's centre, so it
/// zooms and pans with the map. Godot-free (plain floats) like
/// <see cref="PanMath"/> and <see cref="ZoomMath"/>.
/// </summary>
public static class FogBackdropMath
{
    /// <summary>
    /// Half-extent (board-local px) of a square centred on
    /// <c>(boardW/2, boardH/2)</c> that covers every viewport position the pan
    /// clamp allows. <see cref="PanMath.Clamp"/> keeps each viewport point
    /// within <c>max(vpW, vpH) + pad·zoom</c> screen px of the rotated board
    /// AABB; dividing by the smallest zoom and adding the full board diagonal
    /// (rotation pivots at the local origin, so the AABB can sit a diagonal
    /// away from the rect) bounds that in local space for every angle.
    /// Generous by design — it sizes one quad.
    /// </summary>
    public static float HalfExtent(
        float boardW, float boardH, float vpW, float vpH, float zoomMin, float pad)
    {
        float diagonal = MathF.Sqrt(boardW * boardW + boardH * boardH);
        return diagonal + Math.Max(vpW, vpH) / zoomMin + pad;
    }
}
