using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TreeRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

    private static HexGrid BuildAxialGrid(int qMin, int qMax, int rMin, int rMax)
    {
        var grid = new HexGrid();
        for (int r = rMin; r <= rMax; r++)
        {
            for (int q = qMin; q <= qMax; q++)
            {
                grid.Add(new HexTile(new HexCoord(q, r), Red));
            }
        }
        return grid;
    }

    private static void PlantTree(HexGrid grid, HexCoord coord)
    {
        grid.Get(coord)!.Occupant = new Tree();
    }

    private static bool IsTree(HexGrid grid, HexCoord coord) =>
        grid.Get(coord)?.Occupant is Tree;

    // --- ConvertGravesToTrees --------------------------------------------

    [Fact]
    public void ConvertGravesToTrees_ReplacesOwnerColorGravesWithTree()
    {
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Grave();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Grave();

        TreeRules.ConvertGravesToTrees(grid, Red);

        Assert.IsType<Tree>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Tree>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void ConvertGravesToTrees_LeavesUnitsAndCapitalsAlone()
    {
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(1, 0))!.Occupant = new Capital();

        TreeRules.ConvertGravesToTrees(grid, Red);

        Assert.IsType<Unit>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Capital>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void ConvertGravesToTrees_IgnoresGravesOnOtherPlayersTiles()
    {
        // Red-tile grave converts, Blue-tile grave stays when we end
        // Red's turn.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        grid.Get(new HexCoord(0, 0))!.Color = Blue;
        grid.Get(new HexCoord(0, 0))!.Occupant = new Grave();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Grave(); // Red tile

        TreeRules.ConvertGravesToTrees(grid, Red);

        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Tree>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void ConvertGravesToTrees_NoGravesOfOwnerColor_NoOp()
    {
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        grid.Get(new HexCoord(0, 0))!.Color = Blue;
        grid.Get(new HexCoord(0, 0))!.Occupant = new Grave();

        TreeRules.ConvertGravesToTrees(grid, Red);

        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    // --- SpreadTrees: no-op cases -----------------------------------------

    [Fact]
    public void SpreadTrees_NoTrees_NoChange()
    {
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);

        TreeRules.SpreadTrees(grid);

        Assert.Empty(grid.Tiles.Where(t => t.Occupant is Tree));
    }

    [Fact]
    public void SpreadTrees_IsolatedTrees_NoSpread()
    {
        // Trees far enough apart to share no neighbors.
        HexGrid grid = BuildAxialGrid(-2, 3, -2, 3);
        PlantTree(grid, new HexCoord(-2, 0));
        PlantTree(grid, new HexCoord(3, 0));

        TreeRules.SpreadTrees(grid);

        Assert.Equal(2, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    [Fact]
    public void SpreadTrees_AdjacentPair_NoEmptyCommonNeighbor_NoSpread()
    {
        // (0,0) and (1,0) are neighbors. Their common neighbors are
        // (1,-1) and (0,1). Fill both with units so no candidate is empty.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        grid.Get(new HexCoord(1, -1))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(0, 1))!.Occupant = new Unit(Red);

        TreeRules.SpreadTrees(grid);

        // Still exactly the two original trees.
        Assert.Equal(2, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    // --- SpreadTrees: basic spread ---------------------------------------

    [Fact]
    public void SpreadTrees_AdjacentPair_SpawnsOneTreeInCommonEmpty()
    {
        // (0,0) and (1,0) share common neighbors (1,-1) and (0,1).
        // Both empty. Lex-min ordering on (R, Q) picks (1,-1) first
        // because it has the smaller R.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));

        TreeRules.SpreadTrees(grid);

        Assert.True(IsTree(grid, new HexCoord(1, -1)));
        Assert.False(IsTree(grid, new HexCoord(0, 1)));
        // Exactly one new tree added (two originals + one spawn).
        Assert.Equal(3, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    [Fact]
    public void SpreadTrees_PairWithSingleCommonEmpty_SpawnsThere()
    {
        // Block the lex-min candidate so only (0,1) remains.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        grid.Get(new HexCoord(1, -1))!.Occupant = new Capital();

        TreeRules.SpreadTrees(grid);

        Assert.True(IsTree(grid, new HexCoord(0, 1)));
        Assert.Equal(3, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    // --- SpreadTrees: simultaneous, non-cascading ------------------------

    [Fact]
    public void SpreadTrees_NewlySpawnedTree_DoesNotFeedFurtherSpreadThisCall()
    {
        // Trees at (0,0) and (0,1). Their common empty neighbors are
        // (1,0) and (-1,1); lex-min on (R, Q) picks (1,0) (R=0 beats R=1).
        // (2,0) is an existing tree but NOT adjacent to (0,0) or (0,1).
        // If spawning cascaded, the new tree at (1,0) would pair with
        // (2,0) and add another spawn. With simultaneous application we
        // expect exactly one new tree this call.
        HexGrid grid = BuildAxialGrid(-2, 3, -2, 3);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(0, 1));
        PlantTree(grid, new HexCoord(2, 0));

        int before = grid.Tiles.Count(t => t.Occupant is Tree);
        TreeRules.SpreadTrees(grid);
        int after = grid.Tiles.Count(t => t.Occupant is Tree);

        Assert.Equal(before + 1, after);
        Assert.True(IsTree(grid, new HexCoord(1, 0)));
    }

    [Fact]
    public void SpreadTrees_SpawnsAreDeterministic_LexMinWins()
    {
        // Verify determinism: running twice from identical setup picks
        // the same cell.
        HexGrid g1 = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(g1, new HexCoord(0, 0));
        PlantTree(g1, new HexCoord(1, 0));
        TreeRules.SpreadTrees(g1);

        HexGrid g2 = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(g2, new HexCoord(0, 0));
        PlantTree(g2, new HexCoord(1, 0));
        TreeRules.SpreadTrees(g2);

        var coords1 = g1.Tiles.Where(t => t.Occupant is Tree).Select(t => t.Coord).OrderBy(c => c).ToList();
        var coords2 = g2.Tiles.Where(t => t.Occupant is Tree).Select(t => t.Coord).OrderBy(c => c).ToList();
        Assert.Equal(coords1, coords2);
    }

    [Fact]
    public void SpreadTrees_DoesNotSpawnOffMap()
    {
        // Only three tiles exist: (0,0), (1,0), and (0,1). Trees at
        // (0,0) and (1,0) — their other common neighbor (1,-1) is off-map.
        // Only (0,1) is a candidate.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(0, 1), Red));
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));

        TreeRules.SpreadTrees(grid);

        Assert.True(IsTree(grid, new HexCoord(0, 1)));
        Assert.Equal(3, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    [Fact]
    public void SpreadTrees_DoesNotSpawnOntoTower()
    {
        // Regression lock: SpreadTrees picks the lex-min EMPTY common
        // neighbor. A tower occupies the lex-min spot; the other common
        // neighbor is empty and should be chosen. Trees must NEVER
        // overwrite a tower.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        grid.Get(new HexCoord(1, -1))!.Occupant = new Tower();

        TreeRules.SpreadTrees(grid);

        Assert.IsType<Tower>(grid.Get(new HexCoord(1, -1))!.Occupant);
        Assert.True(IsTree(grid, new HexCoord(0, 1)));
    }

    [Fact]
    public void SpreadTrees_DoesNotReplaceOccupiedCommonNeighbor()
    {
        // Common neighbor (1,-1) holds a unit; lex-min pick must fall
        // through to (0,1).
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        grid.Get(new HexCoord(1, -1))!.Occupant = new Unit(Red);

        TreeRules.SpreadTrees(grid);

        // Unit still there.
        Assert.IsType<Unit>(grid.Get(new HexCoord(1, -1))!.Occupant);
        // Tree added at the other candidate.
        Assert.True(IsTree(grid, new HexCoord(0, 1)));
    }

    // --- CountNonTreeTiles -----------------------------------------------

    [Fact]
    public void CountNonTreeTiles_IgnoresTreeTilesInTerritory()
    {
        HexGrid grid = BuildAxialGrid(0, 3, 0, 0);
        PlantTree(grid, new HexCoord(1, 0));
        PlantTree(grid, new HexCoord(2, 0));

        var territory = new Territory(
            Red,
            new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0) },
            capital: new HexCoord(0, 0));

        Assert.Equal(2, TreeRules.CountNonTreeTiles(territory, grid));
    }

    [Fact]
    public void CountNonTreeTiles_CountsUnitAndCapitalTilesAsIncome()
    {
        // Only Tree tiles are excluded; units, capitals, graves still
        // count as income-producing.
        HexGrid grid = BuildAxialGrid(0, 3, 0, 0);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(2, 0))!.Occupant = new Grave();

        var territory = new Territory(
            Red,
            new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0) },
            capital: new HexCoord(0, 0));

        Assert.Equal(4, TreeRules.CountNonTreeTiles(territory, grid));
    }
}
