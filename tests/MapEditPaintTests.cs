using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class MapEditPaintTests
{
    private const int Cols = 10;
    private const int Rows = 10;

    private static (HexGrid grid, HashSet<HexCoord> water) MakeBlankBoard()
    {
        var grid = new HexGrid();
        var water = new HashSet<HexCoord>();
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                water.Add(HexCoord.FromOffset(col, row));
            }
        }
        return (grid, water);
    }

    private static int CountCapitals(HexGrid grid) =>
        grid.Tiles.Count(t => t.Occupant is Capital);

    [Fact]
    public void PaintLand_OnWater_AddsTileAndRemovesFromWater()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);

        MapEditPaint.PaintLand(grid, water, new List<Territory>(), Cols, Rows, coord, color);

        Assert.True(grid.Contains(coord));
        Assert.Equal(color, grid.Get(coord)!.Owner);
        Assert.DoesNotContain(coord, water);
    }

    [Fact]
    public void PaintLand_OutOfBounds_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();
        int waterBefore = water.Count;

        territories = MapEditPaint.PaintLand(
            grid, water, territories, Cols, Rows,
            HexCoord.FromOffset(-1, 0), PlayerId.FromIndex(0));

        Assert.Equal(0, grid.Count);
        Assert.Equal(waterBefore, water.Count);
        Assert.Empty(territories);
    }

    [Fact]
    public void PaintWater_OnLand_RemovesTileAndAddsToWater()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        MapEditPaint.PaintWater(grid, water, territories, Cols, Rows, coord);

        Assert.False(grid.Contains(coord));
        Assert.Contains(coord, water);
    }

    [Fact]
    public void PaintTreeToggle_OnEmptyLand_PlacesTree()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTreeToggle_OnExistingTree_RemovesTree()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.Null(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTreeToggle_OnWater_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();
        var coord = HexCoord.FromOffset(2, 2);

        IReadOnlyList<Territory> after = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.Same(territories, after);
        Assert.False(grid.Contains(coord));
    }

    [Fact]
    public void PaintTreeToggle_OnTileWithCapital_DoesNotReplaceCapital()
    {
        // A capital is gameplay state placed by CapitalReconciler. The tree
        // palette mustn't trash it — only empty land or existing trees.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 3; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        // After three adjacent same-color paints we have one capital
        // somewhere in the row.
        HexCoord capitalCoord = territories[0].Capital!.Value;

        IReadOnlyList<Territory> after = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, capitalCoord);

        Assert.Same(territories, after);
        Assert.IsType<Capital>(grid.Get(capitalCoord)!.Occupant);
    }

    [Fact]
    public void PaintCapital_OnWater_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();
        var coord = HexCoord.FromOffset(2, 2);

        IReadOnlyList<Territory> after = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, coord);

        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintCapital_OutOfBounds_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();

        IReadOnlyList<Territory> after = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, HexCoord.FromOffset(-1, 0));

        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintCapital_OnSingletonTerritory_IsNoop()
    {
        // A 1-tile territory has no capital and can't have one — gameplay
        // rule: capitals only exist on territories of size >= 2.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        IReadOnlyList<Territory> after = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, coord);

        Assert.Same(territories, after);
        Assert.Null(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintCapital_OnExistingCapital_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 3; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        HexCoord capitalCoord = territories[0].Capital!.Value;

        IReadOnlyList<Territory> after = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, capitalCoord);

        Assert.Same(territories, after);
        Assert.IsType<Capital>(grid.Get(capitalCoord)!.Occupant);
    }

    [Fact]
    public void PaintCapital_OnNonCapitalTileInMultiHexTerritory_MovesCapital()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 3; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        HexCoord oldCapital = territories[0].Capital!.Value;
        // Pick a coord in the territory that ISN'T the current capital.
        HexCoord target = HexCoord.FromOffset(0, 0);
        if (target == oldCapital) target = HexCoord.FromOffset(2, 0);

        IReadOnlyList<Territory> after = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, target);

        Assert.NotSame(territories, after);
        Assert.IsType<Capital>(grid.Get(target)!.Occupant);
        Assert.Null(grid.Get(oldCapital)!.Occupant);
        Assert.Equal(target, after[0].Capital);
    }

    [Fact]
    public void PaintCapital_OnTree_RemovesTreeAndPlacesCapital()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 3; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        HexCoord oldCapital = territories[0].Capital!.Value;
        // Find a non-capital coord in the territory and plant a tree on it.
        HexCoord treeCoord = HexCoord.FromOffset(0, 0);
        if (treeCoord == oldCapital) treeCoord = HexCoord.FromOffset(2, 0);
        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, treeCoord);
        Assert.IsType<Tree>(grid.Get(treeCoord)!.Occupant);

        IReadOnlyList<Territory> after = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, treeCoord);

        Assert.IsType<Capital>(grid.Get(treeCoord)!.Occupant);
        Assert.Null(grid.Get(oldCapital)!.Occupant);
        Assert.Equal(treeCoord, after[0].Capital);
    }

    // -- Tower toggle --

    [Fact]
    public void PaintTowerToggle_OutOfBounds_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();

        IReadOnlyList<Territory> after = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, HexCoord.FromOffset(-1, 0));

        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintTowerToggle_OnWater_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();
        var coord = HexCoord.FromOffset(2, 2);

        IReadOnlyList<Territory> after = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.Same(territories, after);
        Assert.False(grid.Contains(coord));
    }

    [Fact]
    public void PaintTowerToggle_OnEmptyLand_PlacesTower()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTowerToggle_OnSingletonLand_PlacesTower()
    {
        // Towers can sit on a 1-tile territory (unlike capitals).
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(5, 5);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTowerToggle_OnExistingTower_RemovesTower()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);

        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.Null(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTowerToggle_OnTree_ReplacesTreeWithTower()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);
        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);

        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTowerToggle_OnCapital_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 3; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        HexCoord capitalCoord = territories[0].Capital!.Value;

        IReadOnlyList<Territory> after = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, capitalCoord);

        Assert.Same(territories, after);
        Assert.IsType<Capital>(grid.Get(capitalCoord)!.Occupant);
    }

    // -- Tree toggle additions for tower interaction + singleton --

    [Fact]
    public void PaintTreeToggle_OnTower_ReplacesTowerWithTree()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);
        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);

        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTreeToggle_OnSingletonLand_PlacesTree()
    {
        // Trees can sit on a 1-tile territory (unlike capitals).
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(5, 5);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);
    }

    // -- Capital placement on tower --

    [Fact]
    public void PaintCapital_OnTower_RemovesTowerAndPlacesCapital()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 3; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        HexCoord oldCapital = territories[0].Capital!.Value;
        HexCoord towerCoord = HexCoord.FromOffset(0, 0);
        if (towerCoord == oldCapital) towerCoord = HexCoord.FromOffset(2, 0);
        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, towerCoord);
        Assert.IsType<Tower>(grid.Get(towerCoord)!.Occupant);

        IReadOnlyList<Territory> after = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, towerCoord);

        Assert.IsType<Capital>(grid.Get(towerCoord)!.Occupant);
        Assert.Null(grid.Get(oldCapital)!.Occupant);
        Assert.Equal(towerCoord, after[0].Capital);
    }

    [Fact]
    public void PaintLand_FourAdjacentSameColorTiles_LeavesExactlyOneCapital()
    {
        // Reproduces the editor's duplicate-capital bug: each paint
        // reconciles, and without threading the previous territory list
        // back in, CapitalReconciler doesn't recognize the existing
        // Capital occupant as inherited and places a fresh one without
        // clearing the old one. After painting a strip of same-color
        // tiles the grid ends up with multiple Capital occupants.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();

        for (int col = 0; col < 4; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }

        Assert.Equal(1, CountCapitals(grid));
    }

    // --- PaintNeutral (issue #39) -----------------------------------------

    [Fact]
    public void PaintNeutral_OnWater_AddsUnownedTileAndRemovesFromWater()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var coord = HexCoord.FromOffset(2, 3);

        MapEditPaint.PaintNeutral(grid, water, new List<Territory>(), Cols, Rows, coord);

        Assert.True(grid.Contains(coord));
        Assert.True(grid.Get(coord)!.Owner.IsNone);
        Assert.DoesNotContain(coord, water);
    }

    [Fact]
    public void PaintNeutral_OnOwnedLand_SetsOwnerNone()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
    }

    [Fact]
    public void PaintNeutral_OutOfBounds_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();
        int waterBefore = water.Count;

        territories = MapEditPaint.PaintNeutral(
            grid, water, territories, Cols, Rows, HexCoord.FromOffset(-1, 0));

        Assert.Equal(0, grid.Count);
        Assert.Equal(waterBefore, water.Count);
        Assert.Empty(territories);
    }

    [Fact]
    public void PaintNeutral_OverCapitalTile_ClearsOccupant_AndReconcileDoesNotThrow()
    {
        // Paint a 2-hex owned region so a capital is placed, then paint the
        // capital tile neutral. PaintNeutral must clear the occupant so the
        // "no capital on neutral land" invariant holds and the internal
        // Reconcile call does not throw.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 2; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        HexCoord capital = territories[0].Capital!.Value;
        Assert.IsType<Capital>(grid.Get(capital)!.Occupant);

        IReadOnlyList<Territory> after = MapEditPaint.PaintNeutral(
            grid, water, territories, Cols, Rows, capital);

        Assert.True(grid.Get(capital)!.Owner.IsNone);
        Assert.Null(grid.Get(capital)!.Occupant);
        Assert.DoesNotContain(after, t => t.Owner.IsNone && t.HasCapital);
    }

    [Fact]
    public void PaintNeutral_RoundTripsThroughEditorSnapshot()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, territories);
        // Mutate away, then restore.
        MapEditPaint.PaintLand(grid, water, territories, Cols, Rows, coord, color);
        snap.ApplyTo(grid, water);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
    }

    // --- PaintGoldToggle (issue #45) -------------------------------------

    [Fact]
    public void PaintGoldToggle_OnLand_SetsGold()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsGold);
    }

    [Fact]
    public void PaintGoldToggle_Twice_TogglesBackOff()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        territories = MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);
        MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);

        Assert.False(grid.Get(coord)!.IsGold);
    }

    [Fact]
    public void PaintGoldToggle_PreservesOwnerAndOccupant()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        grid.Get(coord)!.Occupant = new Tower();

        MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);

        Assert.Equal(color, grid.Get(coord)!.Owner);
        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);
        Assert.True(grid.Get(coord)!.IsGold);
    }

    [Fact]
    public void PaintGoldToggle_OnNeutralLand_SetsGold()
    {
        // Gold must be allowed on neutral (unowned) land (issue #45 acceptance).
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
        Assert.True(grid.Get(coord)!.IsGold);
    }

    [Fact]
    public void PaintGoldToggle_OnWater_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var coord = HexCoord.FromOffset(2, 2);

        MapEditPaint.PaintGoldToggle(grid, water, new List<Territory>(), Cols, Rows, coord);

        Assert.False(grid.Contains(coord));
        Assert.Contains(coord, water);
    }

    // --- Mountain brush (issue #37) --------------------------------------

    [Fact]
    public void PaintMountainToggle_OnEmptyLand_SetsMountain()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsMountain);
    }

    [Fact]
    public void PaintMountainToggle_OnExistingMountain_ClearsIt()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        territories = MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);
        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        Assert.False(grid.Get(coord)!.IsMountain);
    }

    [Fact]
    public void PaintMountainToggle_OverTree_ClearsTreeAndSetsMountain()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTreeToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsMountain);
        Assert.Null(grid.Get(coord)!.Occupant);   // tree cleared
    }

    [Fact]
    public void PaintMountainToggle_OnCapital_IsNoop()
    {
        // Build a 2-tile territory so a capital can exist, move the capital
        // onto the target tile, then try to paint it a mountain.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var a = HexCoord.FromOffset(2, 2);
        var b = HexCoord.FromOffset(3, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, a, color);
        territories = MapEditPaint.PaintLand(grid, water, territories, Cols, Rows, b, color);
        territories = MapEditPaint.PaintCapital(grid, water, territories, Cols, Rows, a);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, a);

        Assert.False(grid.Get(a)!.IsMountain);              // refused
        Assert.IsType<Capital>(grid.Get(a)!.Occupant);      // capital intact
    }

    [Fact]
    public void PaintMountainToggle_PreservesGoldAndOwner()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(2);
        var coord = HexCoord.FromOffset(4, 4);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsMountain);
        Assert.True(grid.Get(coord)!.IsGold);     // gold independent of mountain
        Assert.Equal(color, grid.Get(coord)!.Owner);
    }

    [Fact]
    public void PaintTreeToggle_OverMountain_ClearsMountain()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintTreeToggle(grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);
        Assert.False(grid.Get(coord)!.IsMountain);   // mountain cleared
    }

    [Fact]
    public void PaintTowerToggle_OverMountain_KeepsMountain()
    {
        // Towers and mountains now coexist (the +1 high-ground bonus): placing a
        // tower on a mountain leaves the terrain flag set.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintTowerToggle(grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);
        Assert.True(grid.Get(coord)!.IsMountain);   // mountain retained
    }

    [Fact]
    public void PaintMountainToggle_OverTower_KeepsTower()
    {
        // Symmetric to the above: turning a mountain ON under a tower leaves the
        // tower in place (only trees are mutually exclusive with mountains).
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTowerToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsMountain);
        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);   // tower retained
    }

    [Fact]
    public void PaintCapital_OnMountain_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var a = HexCoord.FromOffset(2, 2);
        var b = HexCoord.FromOffset(3, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, a, color);
        territories = MapEditPaint.PaintLand(grid, water, territories, Cols, Rows, b, color);
        // Reconcile auto-placed the capital on one tile; mountain-ify the other.
        HexCoord capCoord = territories.Single(t => t.Owner == color).Capital!.Value;
        HexCoord mountainCoord = capCoord == a ? b : a;
        territories = MapEditPaint.PaintMountainToggle(
            grid, water, territories, Cols, Rows, mountainCoord);

        territories = MapEditPaint.PaintCapital(
            grid, water, territories, Cols, Rows, mountainCoord);

        Assert.Null(grid.Get(mountainCoord)!.Occupant);   // no capital placed on the mountain
        Assert.True(grid.Get(mountainCoord)!.IsMountain);
        // The capital stayed put on its original tile.
        Assert.Equal(capCoord, territories.Single(t => t.Owner == color).Capital!.Value);
    }

    [Fact]
    public void PaintMountainToggle_OnWater_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var coord = HexCoord.FromOffset(2, 2);

        MapEditPaint.PaintMountainToggle(grid, water, new List<Territory>(), Cols, Rows, coord);

        Assert.False(grid.Contains(coord));
        Assert.Contains(coord, water);
    }
}
