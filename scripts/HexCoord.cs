using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Axial coordinate for a pointy-top hex. Q runs roughly east, R runs roughly
/// south-east. The six neighbors live at Q±1, R±1, and (Q+1,R-1)/(Q-1,R+1).
/// </summary>
public readonly struct HexCoord : IEquatable<HexCoord>, IComparable<HexCoord>
{
    public int Q { get; }
    public int R { get; }

    public HexCoord(int q, int r)
    {
        Q = q;
        R = r;
    }

    // Pointy-top axial neighbor directions, in clockwise order starting east.
    private static readonly HexCoord[] Directions =
    {
        new HexCoord(1, 0),   // E
        new HexCoord(1, -1),  // NE
        new HexCoord(0, -1),  // NW
        new HexCoord(-1, 0),  // W
        new HexCoord(-1, 1),  // SW
        new HexCoord(0, 1),   // SE
    };

    public HexCoord Neighbor(int direction)
    {
        HexCoord d = Directions[direction];
        return new HexCoord(Q + d.Q, R + d.R);
    }

    public IEnumerable<HexCoord> Neighbors()
    {
        for (int i = 0; i < 6; i++)
        {
            yield return Neighbor(i);
        }
    }

    /// <summary>
    /// Convert a (col, row) offset coordinate (odd-r: odd rows shifted right)
    /// into axial. Row becomes R; Q is col minus half of row rounded down.
    /// </summary>
    public static HexCoord FromOffset(int col, int row)
    {
        int q = col - (row - (row & 1)) / 2;
        return new HexCoord(q, row);
    }

    /// <summary>Convert axial back to odd-r offset.</summary>
    public (int Col, int Row) ToOffset()
    {
        int row = R;
        int col = Q + (R - (R & 1)) / 2;
        return (col, row);
    }

    /// <summary>
    /// Pixel center for a hex of radius <paramref name="size"/>, measured from
    /// axial origin (0,0). Callers add their own padding offset.
    /// </summary>
    public Vector2 ToPixel(float size)
    {
        float x = size * Mathf.Sqrt(3f) * (Q + R * 0.5f);
        float y = size * 1.5f * R;
        return new Vector2(x, y);
    }

    /// <summary>
    /// Inverse of <see cref="ToPixel"/>: find the hex whose footprint contains
    /// <paramref name="pixel"/>. Uses cube-coordinate rounding to pick the
    /// correct hex for points near an edge.
    /// </summary>
    public static HexCoord FromPixel(Vector2 pixel, float size)
    {
        float qFrac = (pixel.X * Mathf.Sqrt(3f) / 3f - pixel.Y / 3f) / size;
        float rFrac = (pixel.Y * 2f / 3f) / size;
        return Round(qFrac, rFrac);
    }

    /// <summary>
    /// Round fractional axial coordinates to the nearest integer hex. The
    /// naive approach (round Q and R independently) can produce wrong
    /// results near hex corners; the cube-coordinate invariant
    /// <c>x + y + z == 0</c> fixes it by re-deriving the axis with the
    /// largest rounding error.
    /// </summary>
    public static HexCoord Round(float qFrac, float rFrac)
    {
        float sFrac = -qFrac - rFrac;

        int q = Mathf.RoundToInt(qFrac);
        int r = Mathf.RoundToInt(rFrac);
        int s = Mathf.RoundToInt(sFrac);

        float qDiff = Mathf.Abs(q - qFrac);
        float rDiff = Mathf.Abs(r - rFrac);
        float sDiff = Mathf.Abs(s - sFrac);

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

    public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
    public override bool Equals(object? obj) => obj is HexCoord h && Equals(h);
    public override int GetHashCode() => HashCode.Combine(Q, R);
    public override string ToString() => $"({Q},{R})";

    /// <summary>
    /// Lex-min ordering on (R, Q) — row-major, so "top-left" comes first.
    /// Used as a deterministic tiebreaker in capital placement and merge
    /// reconciliation.
    /// </summary>
    public int CompareTo(HexCoord other)
    {
        int rCompare = R.CompareTo(other.R);
        return rCompare != 0 ? rCompare : Q.CompareTo(other.Q);
    }

    public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
    public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
}
