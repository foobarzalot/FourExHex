using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class GameStateSnapshotTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

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
        grid.Get(new HexCoord(0, 0))!.Color = Blue;

        snap.ApplyTo(grid, treasury);

        Assert.Equal(Red, grid.Get(new HexCoord(0, 0))!.Color);
    }

    [Fact]
    public void Capture_IncludesUnitLevel()
    {
        // Regression: the clone used to drop Unit.Level, turning higher-
        // level units into peasants after undo.
        HexGrid grid = BuildTwoTileRedGrid();
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Knight);
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);
        grid.Get(new HexCoord(0, 0))!.Occupant = null;
        snap.ApplyTo(grid, treasury);

        Unit? restored = grid.Get(new HexCoord(0, 0))!.Unit;
        Assert.NotNull(restored);
        Assert.Equal(UnitLevel.Knight, restored!.Level);
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
        grid.Get(new HexCoord(0, 0))!.Color = Blue;
        grid.Get(new HexCoord(1, 0))!.Color = Blue;

        snap.ApplyTo(grid, treasury);

        Assert.Equal(Red, grid.Get(new HexCoord(0, 0))!.Color);
        Assert.Equal(Red, grid.Get(new HexCoord(1, 0))!.Color);
    }

    // --- Apply ------------------------------------------------------------

    [Fact]
    public void Apply_RestoresUnits_RemovesOnesCreatedSinceSnapshot()
    {
        HexGrid grid = BuildTwoTileRedGrid();
        var treasury = new Treasury();
        var territories = TerritoriesFor(grid);

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        // Buy a peasant after the snapshot was taken.
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
