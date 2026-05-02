using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TreeRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);
    private static readonly IReadOnlySet<HexCoord> NoWater = new HashSet<HexCoord>();

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

    // --- RunStartOfTurnGrowth: no-op cases --------------------------------

    [Fact]
    public void RunStartOfTurnGrowth_EmptyBoard_NoChange()
    {
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.Empty(grid.Tiles.Where(t => t.Occupant is Tree));
    }

    [Fact]
    public void RunStartOfTurnGrowth_IsolatedTrees_NoSpread()
    {
        // Two trees with no common neighbor — neither contributes a 2nd
        // tree-neighbor anywhere, so no empty cell qualifies.
        HexGrid grid = BuildAxialGrid(-2, 3, -2, 3);
        PlantTree(grid, new HexCoord(-2, 0));
        PlantTree(grid, new HexCoord(3, 0));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.Equal(2, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    [Fact]
    public void RunStartOfTurnGrowth_OneTreeNeighbor_DoesNotSpread()
    {
        // Single tree at (0,0). All its neighbors have only 1 tree
        // neighbor in the snapshot, so none of them flip.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.Equal(1, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    // --- RunStartOfTurnGrowth: coastal spread (1 tree + 1 water) ----------

    [Fact]
    public void RunStartOfTurnGrowth_OneTreeNeighborWithWater_Spreads()
    {
        // Single tree at (0,0). (1,-1) has just that one tree neighbor —
        // not enough for the inland >= 2 rule. Mark (2,-1) as water,
        // adjacent to (1,-1). Coastal rule (1+ tree AND 1+ water) fires.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        var water = new HashSet<HexCoord> { new HexCoord(2, -1) };

        TreeRules.RunStartOfTurnGrowth(grid, Red, water);

        Assert.True(IsTree(grid, new HexCoord(1, -1)));
    }

    [Fact]
    public void RunStartOfTurnGrowth_OneTreeNeighborNoWater_DoesNotSpread()
    {
        // Single tree at (0,0). (1,-1) has 1 tree neighbor and no
        // adjacent water — neither rule fires.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.Null(grid.Get(new HexCoord(1, -1))!.Occupant);
        Assert.Equal(1, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    [Fact]
    public void RunStartOfTurnGrowth_WaterButNoTreeNeighbor_DoesNotSpread()
    {
        // Water around (0,0) but no tree neighbors anywhere on the
        // board. Water alone never seeds a tree.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        var water = new HashSet<HexCoord>
        {
            new HexCoord(1, 0), new HexCoord(-1, 1), new HexCoord(0, -1),
        };

        TreeRules.RunStartOfTurnGrowth(grid, Red, water);

        Assert.Empty(grid.Tiles.Where(t => t.Occupant is Tree));
    }

    [Fact]
    public void RunStartOfTurnGrowth_OtherColorCellWithTreeAndWater_DoesNotSpread()
    {
        // (1,-1) has 1 tree neighbor (0,0) and 1 water neighbor (2,-1)
        // but is colored Blue — color filter still blocks the spread
        // when Red's turn starts.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        grid.Get(new HexCoord(1, -1))!.Color = Blue;
        PlantTree(grid, new HexCoord(0, 0));
        var water = new HashSet<HexCoord> { new HexCoord(2, -1) };

        TreeRules.RunStartOfTurnGrowth(grid, Red, water);

        Assert.Null(grid.Get(new HexCoord(1, -1))!.Occupant);
    }

    // --- RunStartOfTurnGrowth: spread basics ------------------------------

    [Fact]
    public void RunStartOfTurnGrowth_TwoTreeNeighbors_BecomesTree()
    {
        // Trees at (0,0) and (1,0). Cells (1,-1) and (0,1) are each
        // adjacent to BOTH trees, so under the new rule BOTH flip.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.True(IsTree(grid, new HexCoord(1, -1)));
        Assert.True(IsTree(grid, new HexCoord(0, 1)));
        // Originals + both common-neighbors.
        Assert.Equal(4, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    [Fact]
    public void RunStartOfTurnGrowth_ThreeTreeNeighbors_BecomesTree()
    {
        // (0,0) is adjacent to (1,0), (0,1), and (-1,1). Three tree
        // neighbors, well above the threshold of 2 — (0,0) flips.
        HexGrid grid = BuildAxialGrid(-2, 2, -2, 2);
        PlantTree(grid, new HexCoord(1, 0));
        PlantTree(grid, new HexCoord(0, 1));
        PlantTree(grid, new HexCoord(-1, 1));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.True(IsTree(grid, new HexCoord(0, 0)));
    }

    // --- RunStartOfTurnGrowth: color isolation ----------------------------

    [Fact]
    public void RunStartOfTurnGrowth_OtherColorCell_DoesNotBecomeTree()
    {
        // Empty cell (1,-1) has Blue color. Trees at (0,0) and (1,0)
        // make it a candidate by neighbor count, but the color filter
        // keeps it as-is when Red's turn starts.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        grid.Get(new HexCoord(1, -1))!.Color = Blue;
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.Null(grid.Get(new HexCoord(1, -1))!.Occupant);
        // (0,1) is still Red and still flips.
        Assert.True(IsTree(grid, new HexCoord(0, 1)));
    }

    [Fact]
    public void RunStartOfTurnGrowth_OnlyConvertsGravesOnOwnerColor()
    {
        // Two graves: one on Red tile, one on Blue tile. Only the Red
        // grave converts when Red's turn starts; Blue's grave persists.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        grid.Get(new HexCoord(0, 0))!.Color = Blue;
        grid.Get(new HexCoord(0, 0))!.Occupant = new Grave();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Grave(); // Red tile

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.IsType<Tree>(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    // --- RunStartOfTurnGrowth: occupied cells are skipped -----------------

    [Fact]
    public void RunStartOfTurnGrowth_DoesNotOverwriteUnit()
    {
        // (1,-1) holds a friendly unit and has 2 tree neighbors. The
        // spread rule MUST NOT replace the unit.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        grid.Get(new HexCoord(1, -1))!.Occupant = new Unit(Red);

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.IsType<Unit>(grid.Get(new HexCoord(1, -1))!.Occupant);
    }

    [Fact]
    public void RunStartOfTurnGrowth_DoesNotOverwriteCapital()
    {
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        grid.Get(new HexCoord(1, -1))!.Occupant = new Capital();

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.IsType<Capital>(grid.Get(new HexCoord(1, -1))!.Occupant);
    }

    [Fact]
    public void RunStartOfTurnGrowth_DoesNotOverwriteTower()
    {
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        grid.Get(new HexCoord(1, -1))!.Occupant = new Tower();

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.IsType<Tower>(grid.Get(new HexCoord(1, -1))!.Occupant);
    }

    [Fact]
    public void RunStartOfTurnGrowth_DoesNotDoubleSetExistingTree()
    {
        // Existing tree at (1,-1). It stays a tree (and only one tree
        // is counted there in the after-state). Sanity check that
        // re-spawning over a tree isn't silently happening.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));
        PlantTree(grid, new HexCoord(1, -1));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.IsType<Tree>(grid.Get(new HexCoord(1, -1))!.Occupant);
    }

    // --- RunStartOfTurnGrowth: snapshot semantics (no cascade) ------------

    [Fact]
    public void RunStartOfTurnGrowth_NewlyConvertedGraveDoesNotSeedSpread()
    {
        // Snapshot semantics: a grave that becomes a tree via rule 1
        // does NOT count toward another cell's "2+ tree neighbors"
        // tally. Setup: tree at (0,0), grave at (1,0) (Red), empty
        // cell (1,-1) adjacent to both. After rule 1 the grave is now
        // a tree, but the snapshot only contains (0,0). (1,-1) sees
        // 1 tree neighbor in snapshot → stays empty.
        HexGrid grid = BuildAxialGrid(-1, 2, -1, 2);
        PlantTree(grid, new HexCoord(0, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Grave();

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.IsType<Tree>(grid.Get(new HexCoord(1, 0))!.Occupant); // grave converted
        Assert.Null(grid.Get(new HexCoord(1, -1))!.Occupant);        // no cascade
    }

    [Fact]
    public void RunStartOfTurnGrowth_NewlySpreadTreeDoesNotSeedSpread()
    {
        // Snapshot semantics: a tree that appears via rule 2 does NOT
        // count for another rule-2 conversion in the same call.
        // Setup: trees at (-1,0) and (1,0). Their common neighbor (0,0)
        // is the only cell with 2 tree neighbors in the snapshot — it
        // flips. (1,-1) has only (1,0) in the snapshot. With cascade,
        // the new (0,0) tree would push it over the threshold; without
        // cascade, it stays empty.
        HexGrid grid = BuildAxialGrid(-2, 3, -2, 3);
        PlantTree(grid, new HexCoord(-1, 0));
        PlantTree(grid, new HexCoord(1, 0));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.True(IsTree(grid, new HexCoord(0, 0)));
        Assert.False(IsTree(grid, new HexCoord(1, -1)));
        Assert.False(IsTree(grid, new HexCoord(-1, 1)));
        // Originals + just (0,0).
        Assert.Equal(3, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    [Fact]
    public void RunStartOfTurnGrowth_DoesNotSpawnOffMap()
    {
        // Only three tiles exist. With trees at (0,0) and (1,0) the
        // only on-map common neighbor is (0,1).
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        grid.Add(new HexTile(new HexCoord(0, 1), Red));
        PlantTree(grid, new HexCoord(0, 0));
        PlantTree(grid, new HexCoord(1, 0));

        TreeRules.RunStartOfTurnGrowth(grid, Red, NoWater);

        Assert.True(IsTree(grid, new HexCoord(0, 1)));
        Assert.Equal(3, grid.Tiles.Count(t => t.Occupant is Tree));
    }

    // --- CountIncomeProducingTiles ---------------------------------------

    [Fact]
    public void CountIncomeProducingTiles_IgnoresTreeTilesInTerritory()
    {
        HexGrid grid = BuildAxialGrid(0, 3, 0, 0);
        PlantTree(grid, new HexCoord(1, 0));
        PlantTree(grid, new HexCoord(2, 0));

        var territory = new Territory(
            Red,
            new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0), new HexCoord(3, 0) },
            capital: new HexCoord(0, 0));

        Assert.Equal(2, TreeRules.CountIncomeProducingTiles(territory, grid));
    }

    [Fact]
    public void CountIncomeProducingTiles_IgnoresGraveTilesInTerritory()
    {
        // 7-tile territory with two graves should report 5 (the user's
        // worked example for the new rule).
        HexGrid grid = BuildAxialGrid(0, 6, 0, 0);
        grid.Get(new HexCoord(2, 0))!.Occupant = new Grave();
        grid.Get(new HexCoord(4, 0))!.Occupant = new Grave();

        var territory = new Territory(
            Red,
            new[]
            {
                new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
                new HexCoord(3, 0), new HexCoord(4, 0), new HexCoord(5, 0),
                new HexCoord(6, 0),
            },
            capital: new HexCoord(0, 0));

        Assert.Equal(5, TreeRules.CountIncomeProducingTiles(territory, grid));
    }

    [Fact]
    public void CountIncomeProducingTiles_OnlyTreesAndGravesAreExcluded()
    {
        // Mix of every occupant type. Trees and graves are the ONLY
        // income-blockers; units, capitals, and towers continue to pay
        // out. 6 tiles, 1 tree + 1 grave excluded → 4.
        HexGrid grid = BuildAxialGrid(0, 5, 0, 0);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(3, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(4, 0))!.Occupant = new Grave();
        // (5, 0) left empty.

        var territory = new Territory(
            Red,
            new[]
            {
                new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0),
                new HexCoord(3, 0), new HexCoord(4, 0), new HexCoord(5, 0),
            },
            capital: new HexCoord(0, 0));

        Assert.Equal(4, TreeRules.CountIncomeProducingTiles(territory, grid));
    }
}
