using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class GameStateSnapshotTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static HexGrid BuildTwoTileRedGrid()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        return grid;
    }

    private static IReadOnlyList<Territory> TerritoriesFor(HexGrid grid) =>
        TestHelpers.BuildTerritoriesFromGrid(grid);

    // --- Capture ----------------------------------------------------------

    [Fact]
    public void Capture_IncludesEveryTileColor()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        // Mutate the live grid; restore should undo it.
        grid.Get(new HexCoord(0, 0))!.Owner = Blue;

        snap.ApplyTo(grid, treasury);

        Assert.Equal(Red, grid.Get(new HexCoord(0, 0))!.Owner);
    }

    [Fact]
    public void ApplyTo_RestoresTileRemovedSinceCapture()
    {
        // Rising Tides submerges tiles by REMOVING them from the grid. A replay
        // rewind restores the initial snapshot onto the shrunken grid, so
        // ApplyTo must RE-ADD a tile that no longer exists — not silently skip
        // it (the cause of the Rising Tides replay desync).
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);
        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        grid.Remove(new HexCoord(1, 0)); // tile "sinks"
        Assert.Equal(1, grid.Count);

        snap.ApplyTo(grid, treasury);

        Assert.Equal(2, grid.Count);
        Assert.True(grid.Contains(new HexCoord(1, 0)));
        Assert.Equal(Red, grid.Get(new HexCoord(1, 0))!.Owner);
    }

    [Fact]
    public void Capture_PreservesTreeOccupants()
    {
        // Undo must be able to restore a tree that was on a tile —
        // e.g., undoing a unit move that cleared a tree should put
        // the tree back.
        HexGrid grid = BuildTwoTileRedGrid();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);
        grid.Get(new HexCoord(0, 0))!.Occupant = null;
        snap.ApplyTo(grid, treasury);

        Assert.IsType<Tree>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    [Fact]
    public void Capture_PreservesTowerOccupants()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);
        grid.Get(new HexCoord(0, 0))!.Occupant = null;
        snap.ApplyTo(grid, treasury);

        Assert.IsType<Tower>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    [Fact]
    public void Capture_PreservesGraveOccupants()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Grave();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);
        grid.Get(new HexCoord(0, 0))!.Occupant = null;
        snap.ApplyTo(grid, treasury);

        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 0))!.Occupant);
    }

    [Fact]
    public void Capture_IncludesUnitLevel()
    {
        // Regression: the clone used to drop Unit.Level, turning higher-
        // level units into recruits after undo.
        HexGrid grid = BuildTwoTileRedGrid();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);
        grid.Get(new HexCoord(0, 0))!.Occupant = null;
        snap.ApplyTo(grid, treasury);

        Unit? restored = grid.Get(new HexCoord(0, 0))!.Unit;
        Assert.NotNull(restored);
        Assert.Equal(UnitLevel.Captain, restored!.Level);
    }

    [Fact]
    public void Capture_IncludesUnitOwnerAndMovedFlag()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red)
        {
            HasMovedThisTurn = true,
        };
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        // Corrupt the live unit state.
        grid.Get(new HexCoord(0, 0))!.Unit!.HasMovedThisTurn = false;
        grid.Get(new HexCoord(0, 0))!.Occupant = null;

        snap.ApplyTo(grid, treasury);

        Unit? restored = grid.Get(new HexCoord(0, 0))!.Unit;
        Assert.NotNull(restored);
        Assert.Equal(Red, restored!.Owner);
        Assert.True(restored.HasMovedThisTurn);
    }

    [Fact]
    public void Capture_IncludesTreasuryBalances()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);
        HexCoord capital = territories[0].Capital!.Value;
        treasury.SetGold(capital, 42);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        treasury.SetGold(capital, 0);
        snap.ApplyTo(grid, treasury);

        Assert.Equal(42, treasury.GetGold(capital));
    }

    [Fact]
    public void Capture_IncludesTerritories()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);
        IReadOnlyList<Territory> returned = snap.ApplyTo(grid, treasury);

        Assert.Equal(territories.Count, returned.Count);
        Assert.Equal(territories[0].Capital, returned[0].Capital);
    }

    [Fact]
    public void Capture_IsDeepCopy_MutatingLiveTileDoesNotAffectSnapshot()
    {
        // If Capture stored direct references, restoring would be a no-op.
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        // Now mutate every live tile to a different color.
        grid.Get(new HexCoord(0, 0))!.Owner = Blue;
        grid.Get(new HexCoord(1, 0))!.Owner = Blue;

        snap.ApplyTo(grid, treasury);

        Assert.Equal(Red, grid.Get(new HexCoord(0, 0))!.Owner);
        Assert.Equal(Red, grid.Get(new HexCoord(1, 0))!.Owner);
    }

    // --- Apply ------------------------------------------------------------

    [Fact]
    public void Apply_RestoresUnits_RemovesOnesCreatedSinceSnapshot()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        // Buy a recruit after the snapshot was taken.
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        snap.ApplyTo(grid, treasury);

        Assert.Null(grid.Get(new HexCoord(1, 0))!.Unit);
    }

    [Fact]
    public void Apply_RestoresUnits_ReaddsOnesRemovedSinceSnapshot()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        // Remove the unit after the snapshot.
        grid.Get(new HexCoord(0, 0))!.Occupant = null;

        snap.ApplyTo(grid, treasury);

        Assert.NotNull(grid.Get(new HexCoord(0, 0))!.Unit);
        Assert.Equal(Red, grid.Get(new HexCoord(0, 0))!.Unit!.Owner);
    }

    [Fact]
    public void Apply_RestoresTreasuryBalances()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);
        HexCoord capital = territories[0].Capital!.Value;
        treasury.SetGold(capital, 100);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        treasury.SetGold(capital, 5);

        snap.ApplyTo(grid, treasury);

        Assert.Equal(100, treasury.GetGold(capital));
    }
}
