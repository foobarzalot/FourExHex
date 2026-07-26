// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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

    // --- PaintNeutral ---

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
    public void PaintNeutral_OverTreeTile_PreservesTree()
    {
        // Painting a tile neutral must NOT wipe a tree — neutral ground
        // legitimately holds trees (they spread there). Only
        // the owner changes.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);
        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);

        MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintNeutral_OverTowerTile_PreservesTower()
    {
        // Painting a tile neutral must NOT wipe a tower.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);
        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);

        MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintNeutral_OverGraveTile_PreservesGrave()
    {
        // Graves are owner-agnostic terrain; a neutral grave is valid
        // (it rots to a tree), so neutral paint keeps it.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        grid.Get(coord)!.Occupant = new Grave();

        MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
        Assert.IsType<Grave>(grid.Get(coord)!.Occupant);
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

    // --- PaintGoldToggle ---

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
        // Gold must be allowed on neutral (unowned) land.
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

    // --- Mountain brush ---

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
    public void PaintMountainToggle_OverTree_KeepsTree()
    {
        // Trees and mountains coexist: painting a mountain
        // onto a treed tile leaves the tree in place.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTreeToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsMountain);
        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);   // tree kept
    }

    [Fact]
    public void PaintMountainToggle_OnCapital_SetsMountainKeepsCapital()
    {
        // Capitals coexist with mountains: painting a
        // mountain onto a capital tile sets the flag and leaves the capital.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var a = HexCoord.FromOffset(2, 2);
        var b = HexCoord.FromOffset(3, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, a, color);
        territories = MapEditPaint.PaintLand(grid, water, territories, Cols, Rows, b, color);
        territories = MapEditPaint.PaintCapital(grid, water, territories, Cols, Rows, a);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, a);

        Assert.True(grid.Get(a)!.IsMountain);               // mountain set
        Assert.IsType<Capital>(grid.Get(a)!.Occupant);      // capital intact
    }

    [Fact]
    public void PaintMountainToggle_OverGold_ClearsGold()
    {
        // Gold and mountain are mutually exclusive: painting a
        // mountain onto a gold tile clears the gold. Owner is preserved.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(2);
        var coord = HexCoord.FromOffset(4, 4);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsMountain);
        Assert.False(grid.Get(coord)!.IsGold);    // gold cleared (mutual exclusion)
        Assert.Equal(color, grid.Get(coord)!.Owner);
    }

    [Fact]
    public void PaintGoldToggle_OverMountain_ClearsMountain()
    {
        // The symmetric case: painting gold onto a mountain clears the mountain.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(2);
        var coord = HexCoord.FromOffset(4, 4);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintGoldToggle(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.IsGold);
        Assert.False(grid.Get(coord)!.IsMountain);   // mountain cleared (mutual exclusion)
        Assert.Equal(color, grid.Get(coord)!.Owner);
    }

    [Fact]
    public void PaintTreeToggle_OverMountain_KeepsMountain()
    {
        // Trees and mountains coexist: a tree painted onto a
        // mountain leaves the terrain flag in place.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintMountainToggle(grid, water, territories, Cols, Rows, coord);

        MapEditPaint.PaintTreeToggle(grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);
        Assert.True(grid.Get(coord)!.IsMountain);   // mountain kept
    }

    [Fact]
    public void PaintTowerToggle_OverMountain_KeepsMountain()
    {
        // Towers and mountains coexist (the +1 high-ground bonus): placing a
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
    public void PaintCapital_OnMountain_PlacesCapitalKeepsMountain()
    {
        // Capitals sit on mountains: painting a capital
        // onto a mountain tile places it and the mountain flag stays.
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

        Assert.IsType<Capital>(grid.Get(mountainCoord)!.Occupant);   // capital placed on the mountain
        Assert.True(grid.Get(mountainCoord)!.IsMountain);            // mountain kept
        // The capital moved to the mountain tile.
        Assert.Equal(mountainCoord, territories.Single(t => t.Owner == color).Capital!.Value);
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

    // --- PaintUnitToggle -------------------------------------------------

    /// <summary>
    /// Paint a horizontal run of <paramref name="count"/> tiles for
    /// <paramref name="owner"/> starting at offset (col, row), then pin the
    /// capital to the first tile so the remaining coords are free for units.
    /// </summary>
    private static IReadOnlyList<Territory> PaintRun(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> territories,
        PlayerId owner,
        int col,
        int row,
        int count)
    {
        for (int i = 0; i < count; i++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col + i, row), owner);
        }
        if (!owner.IsNone && count > 1)
        {
            territories = MapEditPaint.PaintCapital(
                grid, water, territories, Cols, Rows, HexCoord.FromOffset(col, row));
        }
        return territories;
    }

    [Fact]
    public void PaintUnitToggle_OnOwnedTile_PlacesUnitOwnedByTileOwner()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);

        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Soldier);

        var unit = Assert.IsType<Unit>(grid.Get(coord)!.Occupant);
        Assert.Equal(color, unit.Owner);
        Assert.Equal(UnitLevel.Soldier, unit.Level);
        Assert.False(unit.HasMovedThisTurn);
    }

    [Fact]
    public void PaintUnitToggle_OnNeutralTile_PlacesVikingRaider()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), PlayerId.None, 2, 3, 3);

        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Captain);

        var unit = Assert.IsType<Unit>(grid.Get(coord)!.Occupant);
        Assert.True(unit.Owner.IsNone);
        Assert.Equal(UnitLevel.Captain, unit.Level);
    }

    [Fact]
    public void PaintUnitToggle_CommanderOnNeutral_IsRejected()
    {
        // Viking waves are never Commander (VikingRaidersRules.WaveComposition),
        // so there is no valid level-4 neutral unit to create.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), PlayerId.None, 2, 3, 3);

        IReadOnlyList<Territory> after = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Commander);

        Assert.Null(grid.Get(coord)!.Occupant);
        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintUnitToggle_OnNonNeutralSingleton_IsRejected()
    {
        // A singleton has no capital, so no treasury — a unit there would
        // bankrupt-grave on the first upkeep tick.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        IReadOnlyList<Territory> after = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Recruit);

        Assert.Null(grid.Get(coord)!.Occupant);
        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintUnitToggle_OnNeutralSingleton_PlacesViking()
    {
        // Vikings are upkeep-exempt, so the singleton guard doesn't apply.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintNeutral(
            grid, water, new List<Territory>(), Cols, Rows, coord);

        MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Recruit);

        var unit = Assert.IsType<Unit>(grid.Get(coord)!.Occupant);
        Assert.True(unit.Owner.IsNone);
    }

    [Fact]
    public void PaintUnitToggle_OnWater_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = new List<Territory>();

        IReadOnlyList<Territory> after = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Recruit);

        Assert.False(grid.Contains(coord));
        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintUnitToggle_OutOfBounds_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), PlayerId.FromIndex(0), 2, 3, 3);

        IReadOnlyList<Territory> after = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows,
            HexCoord.FromOffset(-1, 0), UnitLevel.Recruit);

        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintUnitToggle_OverCapital_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        HexCoord capital = territories.Single(t => t.Owner == color).Capital!.Value;

        IReadOnlyList<Territory> after = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, capital, UnitLevel.Recruit);

        Assert.IsType<Capital>(grid.Get(capital)!.Occupant);
        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintUnitToggle_OverTower_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintTowerToggle(
            grid, water, territories, Cols, Rows, coord);

        IReadOnlyList<Territory> after = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Recruit);

        Assert.IsType<Tower>(grid.Get(coord)!.Occupant);
        Assert.Same(territories, after);
    }

    [Fact]
    public void PaintUnitToggle_SameLevelTwice_ClearsUnit()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Soldier);

        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Soldier);

        Assert.Null(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintUnitToggle_DifferentLevel_ReplacesUnit()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Recruit);

        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Commander);

        var unit = Assert.IsType<Unit>(grid.Get(coord)!.Occupant);
        Assert.Equal(UnitLevel.Commander, unit.Level);
    }

    [Fact]
    public void PaintUnitToggle_OverTree_ReplacesTree()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Recruit);

        Assert.IsType<Unit>(grid.Get(coord)!.Occupant);
    }

    // --- unit validity reconcile ----------------------------------------

    [Fact]
    public void PaintLand_OverUnitTile_ReownsUnitToNewOwner()
    {
        // Ownership is derived from the territory, continuously — a recolor
        // flips the garrison rather than deleting it.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var red = PlayerId.FromIndex(0);
        var blue = PlayerId.FromIndex(1);
        var unitCoord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), red, 1, 3, 3);
        territories = PaintRun(grid, water, territories, blue, 4, 3, 2);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, unitCoord, UnitLevel.Soldier);

        // Recolor the garrisoned tile into the adjacent blue territory.
        territories = MapEditPaint.PaintLand(
            grid, water, territories, Cols, Rows, unitCoord, blue);

        var unit = Assert.IsType<Unit>(grid.Get(unitCoord)!.Occupant);
        Assert.Equal(blue, unit.Owner);
        Assert.Equal(UnitLevel.Soldier, unit.Level);
    }

    [Fact]
    public void PaintNeutral_OverUnitTile_ConvertsToVikingRaider()
    {
        // Neutral land legitimately holds a unit — it becomes a viking raider.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Soldier);

        MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
        var unit = Assert.IsType<Unit>(grid.Get(coord)!.Occupant);
        Assert.True(unit.Owner.IsNone);
        Assert.Equal(UnitLevel.Soldier, unit.Level);
    }

    [Fact]
    public void PaintNeutral_OverCommanderTile_RemovesUnit()
    {
        // No level-4 viking exists, so the Commander can't be re-owned.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Commander);

        MapEditPaint.PaintNeutral(grid, water, territories, Cols, Rows, coord);

        Assert.True(grid.Get(coord)!.Owner.IsNone);
        Assert.Null(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintWater_OverUnitTile_RemovesTileAndUnit()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Soldier);

        MapEditPaint.PaintWater(grid, water, territories, Cols, Rows, coord);

        Assert.False(grid.Contains(coord));
        Assert.Contains(coord, water);
    }

    [Fact]
    public void PaintWater_StrandingUnitInNonNeutralSingleton_RemovesUnit()
    {
        // Cutting the middle out of a 3-tile run leaves two singletons; the
        // garrison on one of them is no longer sustainable.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var unitCoord = HexCoord.FromOffset(4, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, unitCoord, UnitLevel.Soldier);
        Assert.IsType<Unit>(grid.Get(unitCoord)!.Occupant);

        MapEditPaint.PaintWater(
            grid, water, territories, Cols, Rows, HexCoord.FromOffset(3, 3));

        Assert.Null(grid.Get(unitCoord)!.Occupant);
    }

    [Fact]
    public void PaintUnitToggle_RoundTripsThroughEditorSnapshot()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = PlayerId.FromIndex(0);
        var coord = HexCoord.FromOffset(3, 3);
        IReadOnlyList<Territory> territories =
            PaintRun(grid, water, new List<Territory>(), color, 2, 3, 3);
        EditorSnapshot pre = EditorSnapshot.Capture(grid, water, territories);
        territories = MapEditPaint.PaintUnitToggle(
            grid, water, territories, Cols, Rows, coord, UnitLevel.Captain);
        EditorSnapshot post = EditorSnapshot.Capture(grid, water, territories);

        Assert.True(pre.DiffersFromGrid(grid, water));

        // Undo back to the pre-paint board, then redo.
        pre.ApplyTo(grid, water);
        Assert.Null(grid.Get(coord)!.Occupant);
        post.ApplyTo(grid, water);
        var unit = Assert.IsType<Unit>(grid.Get(coord)!.Occupant);
        Assert.Equal(UnitLevel.Captain, unit.Level);
        Assert.Equal(color, unit.Owner);
    }
}
