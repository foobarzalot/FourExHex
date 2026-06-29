using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class EditorSnapshotTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    [Fact]
    public void Capture_ThenApply_RestoresColorsAndOccupants()
    {
        var grid = new HexGrid();
        var water = new HashSet<HexCoord>();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red) { Occupant = new Tree() });
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red) { Occupant = new Capital() });
        IReadOnlyList<Territory> territories = new List<Territory>();

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, territories);

        // Mutate the live state.
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Blue;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = null;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();

        snap.ApplyTo(grid, water);

        Assert.Equal(Red, grid.Get(HexCoord.FromOffset(0, 0))!.Owner);
        Assert.IsType<Tree>(grid.Get(HexCoord.FromOffset(0, 0))!.Occupant);
        Assert.IsType<Capital>(grid.Get(HexCoord.FromOffset(1, 0))!.Occupant);
    }

    [Fact]
    public void ApplyTo_RestoresTilesThatWereRemovedAfterCapture()
    {
        // Editor case: paint water onto a land tile, then undo.
        var grid = new HexGrid();
        var water = new HashSet<HexCoord> { HexCoord.FromOffset(2, 0) };
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        grid.Remove(HexCoord.FromOffset(1, 0));
        water.Add(HexCoord.FromOffset(1, 0));

        snap.ApplyTo(grid, water);

        Assert.True(grid.Contains(HexCoord.FromOffset(1, 0)));
        Assert.DoesNotContain(HexCoord.FromOffset(1, 0), water);
        Assert.Contains(HexCoord.FromOffset(2, 0), water);
    }

    [Fact]
    public void ApplyTo_RemovesTilesThatWereAddedAfterCapture()
    {
        // Editor case: paint a land tile onto water, then undo.
        var grid = new HexGrid();
        var water = new HashSet<HexCoord> { HexCoord.FromOffset(0, 0) };

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        water.Remove(HexCoord.FromOffset(0, 0));

        snap.ApplyTo(grid, water);

        Assert.False(grid.Contains(HexCoord.FromOffset(0, 0)));
        Assert.Contains(HexCoord.FromOffset(0, 0), water);
        Assert.Equal(0, grid.Count);
    }

    [Fact]
    public void ApplyTo_ReturnsTerritoryListFromCapture()
    {
        var grid = new HexGrid();
        var water = new HashSet<HexCoord>();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        var t1 = new Territory(Red, new[] { HexCoord.FromOffset(0, 0) }, capital: null);
        IReadOnlyList<Territory> territories = new List<Territory> { t1 };

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, territories);

        IReadOnlyList<Territory> restored = snap.ApplyTo(grid, water);

        Assert.Single(restored);
        Assert.Equal(Red, restored[0].Owner);
    }

    [Fact]
    public void Capture_DeepCopiesTilesSoLaterMutationDoesntLeakIn()
    {
        var grid = new HexGrid();
        var water = new HashSet<HexCoord>();
        var tile = new HexTile(HexCoord.FromOffset(0, 0), Red) { Occupant = new Tree() };
        grid.Add(tile);

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        tile.Owner = Blue;
        tile.Occupant = null;

        // Apply onto a fresh grid+water and check what we get.
        var freshGrid = new HexGrid();
        var freshWater = new HashSet<HexCoord>();
        snap.ApplyTo(freshGrid, freshWater);

        Assert.Equal(Red, freshGrid.Get(HexCoord.FromOffset(0, 0))!.Owner);
        Assert.IsType<Tree>(freshGrid.Get(HexCoord.FromOffset(0, 0))!.Occupant);
    }

    // --- DiffersFromGrid: change detection for the editor undo push ------
    // The editor pushes a stroke onto the undo stack iff the grid actually
    // changed. Flag-only paints (gold, mountain) don't touch the
    // territory partition, so detection must compare grid state, not the
    // territory-list reference.

    private static (HexGrid grid, HashSet<HexCoord> water) MakeBoard()
    {
        var grid = new HexGrid();
        var water = new HashSet<HexCoord>();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Red));
        return (grid, water);
    }

    [Fact]
    public void DiffersFromGrid_NoChange_ReturnsFalse()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBoard();
        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        Assert.False(snap.DiffersFromGrid(grid, water));
    }

    [Fact]
    public void DiffersFromGrid_MountainToggled_ReturnsTrue()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBoard();
        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        grid.Get(HexCoord.FromOffset(0, 0))!.IsMountain = true;

        Assert.True(snap.DiffersFromGrid(grid, water));
    }

    [Fact]
    public void DiffersFromGrid_GoldToggled_ReturnsTrue()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBoard();
        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        grid.Get(HexCoord.FromOffset(0, 0))!.IsGold = true;

        Assert.True(snap.DiffersFromGrid(grid, water));
    }

    [Fact]
    public void DiffersFromGrid_OwnerChanged_ReturnsTrue()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBoard();
        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Blue;

        Assert.True(snap.DiffersFromGrid(grid, water));
    }

    [Fact]
    public void DiffersFromGrid_OccupantChanged_ReturnsTrue()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBoard();
        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Tree();

        Assert.True(snap.DiffersFromGrid(grid, water));
    }

    [Fact]
    public void DiffersFromGrid_WaterPaintedOverLand_ReturnsTrue()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBoard();
        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        grid.Remove(HexCoord.FromOffset(1, 0));
        water.Add(HexCoord.FromOffset(1, 0));

        Assert.True(snap.DiffersFromGrid(grid, water));
    }

    [Fact]
    public void DiffersFromGrid_ToggledThenReverted_ReturnsFalse()
    {
        // A drag that flips a flag on and back off nets no change → no undo.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBoard();
        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, new List<Territory>());

        HexTile tile = grid.Get(HexCoord.FromOffset(0, 0))!;
        tile.IsMountain = true;
        tile.IsMountain = false;

        Assert.False(snap.DiffersFromGrid(grid, water));
    }
}
