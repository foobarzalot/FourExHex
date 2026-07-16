// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class TreeCensusTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);
    private static readonly PlayerId Neutral = PlayerId.None;

    [Fact]
    public void Of_CountsLandTilesAsEveryTileOnTheGrid()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, Red);

        TreeCensus census = TreeCensus.Of(grid);

        Assert.Equal(12, census.LandTiles);
    }

    [Fact]
    public void Of_CountsTreesAndGravesSeparately()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Red);
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Grave();

        TreeCensus census = TreeCensus.Of(grid);

        Assert.Equal(2, census.Trees);
        Assert.Equal(1, census.Graves);
    }

    [Fact]
    public void Of_SplitsTreesByOwnedVsNeutral()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, Red);
        // Two owned trees (Red / Blue), one neutral tree.
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Tree();
        HexTile blueTree = grid.Get(HexCoord.FromOffset(1, 0))!;
        blueTree.Owner = Blue;
        blueTree.Occupant = new Tree();
        HexTile neutralTree = grid.Get(HexCoord.FromOffset(2, 0))!;
        neutralTree.Owner = Neutral;
        neutralTree.Occupant = new Tree();

        TreeCensus census = TreeCensus.Of(grid);

        Assert.Equal(3, census.Trees);
        Assert.Equal(2, census.OwnedTrees);
        Assert.Equal(1, census.NeutralTrees);
        // Invariant: the owned/neutral split partitions the trees exactly.
        Assert.Equal(census.Trees, census.OwnedTrees + census.NeutralTrees);
    }

    [Fact]
    public void Of_GravesDoNotCountAsTrees()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Neutral);
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Grave();

        TreeCensus census = TreeCensus.Of(grid);

        Assert.Equal(0, census.Trees);
        Assert.Equal(0, census.OwnedTrees);
        Assert.Equal(0, census.NeutralTrees);
        Assert.Equal(1, census.Graves);
    }

    [Fact]
    public void Of_EmptyOfTreesReportsZeros()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(4, 4, Red);

        TreeCensus census = TreeCensus.Of(grid);

        Assert.Equal(16, census.LandTiles);
        Assert.Equal(0, census.Trees);
        Assert.Equal(0, census.Graves);
        Assert.Equal(0, census.OwnedTrees);
        Assert.Equal(0, census.NeutralTrees);
    }
}
