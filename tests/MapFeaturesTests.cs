// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="MapFeatures"/> — the pure grid-scan helper behind the
/// first-encounter terrain intros (issue #53). Verifies presence detection and
/// the deterministic (min-<see cref="HexCoord"/>) representative-tile pick the
/// camera pan focuses on.
/// </summary>
public class MapFeaturesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);

    private static void Paint(HexGrid grid, HexCoord coord, TerrainFeature feature)
    {
        HexTile tile = grid.Get(coord)!;
        if (feature == TerrainFeature.Gold) tile.IsGold = true;
        else if (feature == TerrainFeature.Mountain) tile.IsMountain = true;
    }

    [Fact]
    public void Contains_IsFalse_OnAPlainGrid()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 4, Red);
        Assert.False(MapFeatures.Contains(grid, TerrainFeature.Gold));
        Assert.False(MapFeatures.Contains(grid, TerrainFeature.Mountain));
    }

    [Fact]
    public void Contains_DetectsGoldAndMountainIndependently()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 4, Red);
        Paint(grid, HexCoord.FromOffset(2, 1), TerrainFeature.Gold);

        Assert.True(MapFeatures.Contains(grid, TerrainFeature.Gold));
        Assert.False(MapFeatures.Contains(grid, TerrainFeature.Mountain));
    }

    [Fact]
    public void FirstTile_ReturnsNull_WhenFeatureAbsent()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 4, Red);
        Assert.Null(MapFeatures.FirstTile(grid, TerrainFeature.Gold));
    }

    [Fact]
    public void FirstTile_PicksTheMinimumCoord_Deterministically()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(6, 6, Red);
        // Paint several gold tiles; the smallest HexCoord among them must win
        // regardless of dictionary iteration order.
        HexCoord[] golds =
        {
            HexCoord.FromOffset(4, 3),
            HexCoord.FromOffset(1, 0),
            HexCoord.FromOffset(2, 5),
        };
        foreach (HexCoord c in golds) Paint(grid, c, TerrainFeature.Gold);

        HexCoord expected = golds.Min();
        Assert.Equal(expected, MapFeatures.FirstTile(grid, TerrainFeature.Gold));
    }

    [Fact]
    public void FirstTile_PicksTheRightFeature_WhenBothCoexist()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(6, 6, Red);
        HexCoord gold = HexCoord.FromOffset(1, 1);
        HexCoord mountain = HexCoord.FromOffset(4, 4);
        Paint(grid, gold, TerrainFeature.Gold);
        Paint(grid, mountain, TerrainFeature.Mountain);

        Assert.Equal(gold, MapFeatures.FirstTile(grid, TerrainFeature.Gold));
        Assert.Equal(mountain, MapFeatures.FirstTile(grid, TerrainFeature.Mountain));
    }
}
