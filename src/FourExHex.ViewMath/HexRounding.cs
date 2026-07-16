// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Fractional cube-rounding (the float→int boundary point at the
/// pixel↔axial projection). The naive approach — round Q and R
/// independently — produces wrong results near hex corners; the
/// cube-coordinate invariant <c>x + y + z == 0</c> fixes it by
/// re-deriving the axis with the largest rounding error.
///
/// Lives in FourExHex.ViewMath (the float-allowed Godot-free
/// library) so <see cref="HexCoord"/> itself can stay integer-only in
/// FourExHex.Model. View code (scripts/HexPixel) calls this directly
/// after projecting from pixel space.
/// </summary>
public static class HexRounding
{
    public static HexCoord Round(float qFrac, float rFrac)
    {
        float sFrac = -qFrac - rFrac;

        int q = (int)MathF.Round(qFrac, MidpointRounding.AwayFromZero);
        int r = (int)MathF.Round(rFrac, MidpointRounding.AwayFromZero);
        int s = (int)MathF.Round(sFrac, MidpointRounding.AwayFromZero);

        float qDiff = MathF.Abs(q - qFrac);
        float rDiff = MathF.Abs(r - rFrac);
        float sDiff = MathF.Abs(s - sFrac);

        if (qDiff > rDiff && qDiff > sDiff)
        {
            q = -r - s;
        }
        else if (rDiff > sDiff)
        {
            r = -q - s;
        }
        // else: s had the largest error; q and r are already correct.

        return new HexCoord(q, r);
    }
}
