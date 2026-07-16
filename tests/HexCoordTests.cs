// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class HexCoordTests
{
    [Fact]
    public void Equality_SameQR_AreEqual()
    {
        var a = new HexCoord(2, 3);
        var b = new HexCoord(2, 3);

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentQR_AreNotEqual()
    {
        var a = new HexCoord(2, 3);
        var b = new HexCoord(3, 2);

        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(0, 5)]
    [InlineData(5, 7)]   // odd row
    [InlineData(10, 10)]
    [InlineData(17, 12)] // bottom-right of our 18x13 map
    public void Offset_RoundTrip_ReturnsOriginalOffset(int col, int row)
    {
        HexCoord coord = HexCoord.FromOffset(col, row);
        (int c, int r) = coord.ToOffset();

        Assert.Equal(col, c);
        Assert.Equal(row, r);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]   // top-left
    [InlineData(1, 0, 1, 0)]   // one east on even row
    [InlineData(0, 1, 0, 1)]   // one south, first odd row has no q shift
    [InlineData(0, 2, -1, 2)]  // two south: even row shifts q down by 1
    [InlineData(1, 2, 0, 2)]
    public void FromOffset_KnownValues(int col, int row, int expectedQ, int expectedR)
    {
        HexCoord coord = HexCoord.FromOffset(col, row);

        Assert.Equal(expectedQ, coord.Q);
        Assert.Equal(expectedR, coord.R);
    }

    [Fact]
    public void Neighbors_ReturnsSixDistinctCoords()
    {
        var c = new HexCoord(5, 5);

        var neighbors = c.Neighbors().ToList();

        Assert.Equal(6, neighbors.Count);
        Assert.Equal(6, neighbors.Distinct().Count());
        Assert.DoesNotContain(c, neighbors);
    }

    [Fact]
    public void Neighbors_AreReciprocal()
    {
        // If B is a neighbor of A, then A must be a neighbor of B.
        var a = new HexCoord(3, 4);

        foreach (HexCoord n in a.Neighbors())
        {
            Assert.Contains(a, n.Neighbors());
        }
    }

    [Fact]
    public void Neighbor_ByDirection_MatchesKnownOffsets()
    {
        // Pin the six axial direction offsets (E, NE, NW, W, SW, SE) by literal
        // so a corrupted Directions[] table is caught. Asserting against
        // Neighbors() would be tautological — Neighbors() just yields Neighbor(0..5).
        var expected = new[]
        {
            new HexCoord(1, 0),   // E
            new HexCoord(1, -1),  // NE
            new HexCoord(0, -1),  // NW
            new HexCoord(-1, 0),  // W
            new HexCoord(-1, 1),  // SW
            new HexCoord(0, 1),   // SE
        };
        var c = new HexCoord(0, 0);

        for (int dir = 0; dir < 6; dir++)
        {
            Assert.Equal(expected[dir], c.Neighbor(dir));
        }
    }
}
