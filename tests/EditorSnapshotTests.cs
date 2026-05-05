using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class EditorSnapshotTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

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
        grid.Get(HexCoord.FromOffset(0, 0))!.Color = Blue;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = null;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();

        snap.ApplyTo(grid, water);

        Assert.Equal(Red, grid.Get(HexCoord.FromOffset(0, 0))!.Color);
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

        tile.Color = Blue;
        tile.Occupant = null;

        // Apply onto a fresh grid+water and check what we get.
        var freshGrid = new HexGrid();
        var freshWater = new HashSet<HexCoord>();
        snap.ApplyTo(freshGrid, freshWater);

        Assert.Equal(Red, freshGrid.Get(HexCoord.FromOffset(0, 0))!.Color);
        Assert.IsType<Tree>(freshGrid.Get(HexCoord.FromOffset(0, 0))!.Occupant);
    }
}
