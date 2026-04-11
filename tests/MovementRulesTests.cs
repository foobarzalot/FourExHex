using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class MovementRulesTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

    /// <summary>
    /// Build a grid where every tile in a rectangular shape exists, and all
    /// tiles default to a neutral color. Tests override specific tiles.
    /// </summary>
    private static HexGrid BuildGrid(int cols, int rows, Color fillColor) =>
        TestHelpers.BuildRectGrid(cols, rows, fillColor);

    private static void SetTile(HexGrid grid, HexCoord coord, Color color)
    {
        HexTile? tile = grid.Get(coord);
        if (tile != null) tile.Color = color;
    }

    // --- ValidTargets -----------------------------------------------------

    [Fact]
    public void ValidTargets_IncludesEmptyOwnTerritoryTiles()
    {
        // Red occupies three hexes in a row; reposition targets should
        // include the two non-capital tiles.
        HexGrid grid = BuildGrid(5, 1, Blue);
        var coords = new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) };
        foreach (var c in coords) SetTile(grid, c, Red);
        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(red, grid, territories);

        Assert.Contains(red.Coords, c => targets.Contains(c) && c != red.Capital);
        Assert.DoesNotContain(red.Capital!.Value, targets);
    }

    [Fact]
    public void ValidTargets_ExcludesOwnCapital()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        var coords = new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) };
        foreach (var c in coords) SetTile(grid, c, Red);
        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(red, grid, territories);

        Assert.DoesNotContain(red.Capital!.Value, targets);
    }

    [Fact]
    public void ValidTargets_ExcludesOccupiedOwnTiles()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        var coords = new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) };
        foreach (var c in coords) SetTile(grid, c, Red);

        // Put a peasant on tile (1,0); it should no longer be a valid target.
        HexTile? occupied = grid.Get(HexCoord.FromOffset(1, 0));
        occupied!.Occupant = new Unit(Red);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(1, 0), targets);
    }

    [Fact]
    public void ValidTargets_IncludesAdjacentZeroDefenseEnemyTile()
    {
        // Red at (0,1) and (1,1) in a 5x2 Blue grid. Blue's capital is
        // (0,0) (row-0 lex-min), so (2,0) is a non-capital Blue tile
        // adjacent to Red's (1,1) — capturable by a peasant.
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void ValidTargets_ExcludesAdjacentEnemyCapital()
    {
        // Red at (0,0). Blue's only tiles are (1,0) and (2,0); Blue's
        // capital is (1,0) (lex-min of its two coords). A peasant (level 1)
        // can't capture a capital (defense 1).
        HexGrid grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), Blue));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), Blue));

        // Red needs to be a multi-hex territory to have a capital; add a
        // second red tile above (0,0).
        grid.Add(new HexTile(HexCoord.FromOffset(0, 1), Red));

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);
        Territory blue = territories.First(t => t.Owner == Blue);

        var targets = MovementRules.ValidTargets(red, grid, territories);

        // Confirm the test fixture: (1,0) really is Blue's capital.
        Assert.Equal(HexCoord.FromOffset(1, 0), blue.Capital);
        Assert.DoesNotContain(HexCoord.FromOffset(1, 0), targets);
    }

    [Fact]
    public void ValidTargets_ExcludesAdjacentEnemyTileWithDefender()
    {
        // Same 5x2 fixture as the capture-included test. Blue's capital
        // is (0,0); put a Blue peasant defender on (2,0) so defense == 1
        // and a Red peasant (also level 1) can't capture it.
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Blue);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(red, grid, territories);

        // Sanity: confirm (2,0) is not Blue's capital in this fixture.
        Territory blue = territories.First(t => t.Coords.Contains(HexCoord.FromOffset(2, 0)));
        Assert.NotEqual(HexCoord.FromOffset(2, 0), blue.Capital);
        Assert.DoesNotContain(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void ValidTargets_ExcludesNonAdjacentEnemyTile()
    {
        // Red at (0,0). Blue at (5,5). Red cannot reach (5,5).
        HexGrid grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 1), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(5, 5), Blue));
        grid.Add(new HexTile(HexCoord.FromOffset(6, 5), Blue));

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(5, 5), targets);
        Assert.DoesNotContain(HexCoord.FromOffset(6, 5), targets);
    }

    [Fact]
    public void ValidTargets_ExcludesSameColorSiblingTerritory()
    {
        // Two disconnected red territories. A unit in one can't hop to the
        // other through the non-red hexes between them.
        HexGrid grid = BuildGrid(10, 1, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 0), Red);
        SetTile(grid, HexCoord.FromOffset(1, 0), Red);
        SetTile(grid, HexCoord.FromOffset(8, 0), Red);
        SetTile(grid, HexCoord.FromOffset(9, 0), Red);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory leftRed = territories.First(t => t.Owner == Red && t.Coords.Contains(HexCoord.FromOffset(0, 0)));

        var targets = MovementRules.ValidTargets(leftRed, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(8, 0), targets);
        Assert.DoesNotContain(HexCoord.FromOffset(9, 0), targets);
    }

    // --- Move + PlaceNew execution ----------------------------------------

    [Fact]
    public void Move_WithinTerritory_Relocates_ButDoesNotConsumeAction()
    {
        // Repositioning within your own territory does not mark the unit
        // as moved — only captures (or destroying trees/graves) consume
        // the unit's single action per turn.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }

        var unit = new Unit(Red);
        HexTile? srcTile = grid.Get(HexCoord.FromOffset(1, 0));
        srcTile!.Occupant = unit;

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            source: HexCoord.FromOffset(1, 0),
            destination: HexCoord.FromOffset(2, 0),
            grid: grid,
            attackerTerritory: red);

        Assert.False(result.WasCapture);
        Assert.Null(grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
        Assert.Same(unit, grid.Get(HexCoord.FromOffset(2, 0))!.Unit);
        Assert.False(unit.HasMovedThisTurn);
    }

    [Fact]
    public void Move_WithinTerritoryTwice_InSameTurn_Works()
    {
        // Since reposition doesn't consume the action, a unit can be
        // repositioned repeatedly in the same turn.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0) })
        {
            SetTile(grid, c, Red);
        }

        var unit = new Unit(Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = unit;

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MovementRules.Move(HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0), grid, red);
        Assert.False(unit.HasMovedThisTurn);

        MovementRules.Move(HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0), grid, red);
        Assert.False(unit.HasMovedThisTurn);
        Assert.Same(unit, grid.Get(HexCoord.FromOffset(3, 0))!.Unit);
    }

    [Fact]
    public void Move_Capture_TransfersOwnershipAndMovesUnit()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 0), Red);
        SetTile(grid, HexCoord.FromOffset(1, 0), Red);
        // (2,0) is Blue — the capture target.

        var unit = new Unit(Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = unit;

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            source: HexCoord.FromOffset(1, 0),
            destination: HexCoord.FromOffset(2, 0),
            grid: grid,
            attackerTerritory: red);

        Assert.True(result.WasCapture);

        HexTile captured = grid.Get(HexCoord.FromOffset(2, 0))!;
        Assert.Equal(Red, captured.Color);
        Assert.Same(unit, captured.Unit);
        Assert.True(unit.HasMovedThisTurn);

        Assert.Null(grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
    }

    [Fact]
    public void PlaceNew_Capture_TransfersOwnershipAndPlacesUnit()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 0), Red);
        SetTile(grid, HexCoord.FromOffset(1, 0), Red);
        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);
        var unit = new Unit(Red);

        MoveResult result = MovementRules.PlaceNew(
            unit: unit,
            destination: HexCoord.FromOffset(2, 0),
            grid: grid,
            attackerTerritory: red);

        Assert.True(result.WasCapture);
        HexTile captured = grid.Get(HexCoord.FromOffset(2, 0))!;
        Assert.Equal(Red, captured.Color);
        Assert.Same(unit, captured.Unit);
        Assert.True(unit.HasMovedThisTurn);
    }

    [Fact]
    public void PlaceNew_OnOwnEmptyTile_DoesNotConsumeAction()
    {
        // Buying a peasant and placing it on an empty own-territory tile
        // doesn't consume its action — the fresh peasant can still move
        // and capture this turn.
        HexGrid grid = BuildGrid(5, 1, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 0), Red);
        SetTile(grid, HexCoord.FromOffset(1, 0), Red);
        SetTile(grid, HexCoord.FromOffset(2, 0), Red);
        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);
        var unit = new Unit(Red);

        MoveResult result = MovementRules.PlaceNew(
            unit: unit,
            destination: HexCoord.FromOffset(2, 0),
            grid: grid,
            attackerTerritory: red);

        Assert.False(result.WasCapture);
        Assert.Same(unit, grid.Get(HexCoord.FromOffset(2, 0))!.Unit);
        Assert.False(unit.HasMovedThisTurn);
    }
}
