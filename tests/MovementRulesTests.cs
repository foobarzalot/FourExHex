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

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

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

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

        Assert.DoesNotContain(red.Capital!.Value, targets);
    }

    [Fact]
    public void ValidTargets_ExcludesOccupiedOwnTile_WhenNotCombinable()
    {
        // Place a Baron on the target tile — a Peasant attacker can't
        // combine with it (1 + 4 = 5 > Baron cap), so the tile is NOT a
        // valid target.
        HexGrid grid = BuildGrid(5, 1, Blue);
        var coords = new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) };
        foreach (var c in coords) SetTile(grid, c, Red);

        HexTile? occupied = grid.Get(HexCoord.FromOffset(1, 0));
        occupied!.Occupant = new Unit(Red, UnitLevel.Baron);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(1, 0), targets);
    }

    [Fact]
    public void ValidTargets_IncludesOccupiedOwnTile_WhenCombinable()
    {
        // Same fixture but with a Peasant on the target — 1 + 1 = 2,
        // valid combine, so the tile IS a valid target.
        HexGrid grid = BuildGrid(5, 1, Blue);
        var coords = new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) };
        foreach (var c in coords) SetTile(grid, c, Red);

        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(1, 0), targets);
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

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

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

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

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

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

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

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

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

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, leftRed, grid, territories);

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
    public void Move_CaptureEmptyEnemyTile_ReportsNullDestroyed()
    {
        // Capturing an empty enemy tile is a flag-flip — nothing was
        // displaced, so MoveResult.Destroyed is null. Target (2,1) is
        // a non-capital Blue tile (Blue's capital is lex-min (0,0)).
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);

        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1), grid, red);

        Assert.True(result.WasCapture);
        Assert.Null(result.Destroyed);
    }

    [Fact]
    public void Move_CaptureEnemyUnit_ReportsDestroyedUnit()
    {
        // Capturing a tile occupied by an enemy unit reports the displaced
        // unit so the view can play a destruction effect.
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        var defender = new Unit(Blue, UnitLevel.Peasant);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = defender;

        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(Red, UnitLevel.Spearman);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 0), grid, red);

        Assert.True(result.WasCapture);
        Assert.Same(defender, result.Destroyed);
    }

    [Fact]
    public void Move_CaptureEnemyTower_ReportsDestroyedTower()
    {
        // Capturing a tile occupied by an enemy tower reports the
        // displaced tower so the FX can be tower-shaped.
        HexGrid grid = BuildGrid(5, 1, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 0), Red);
        SetTile(grid, HexCoord.FromOffset(1, 0), Red);
        var tower = new Tower();
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = tower;

        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Knight);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0), grid, red);

        Assert.True(result.WasCapture);
        Assert.Same(tower, result.Destroyed);
    }

    [Fact]
    public void Move_Reposition_ReportsNullDestroyed()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0), grid, red);

        Assert.False(result.WasCapture);
        Assert.Null(result.Destroyed);
    }

    [Fact]
    public void Move_OntoOwnTree_ReportsDestroyedTree()
    {
        // Chopping a tree displaces the Tree occupant — reported so the
        // view can play a tree-fall effect.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        var tree = new Tree();
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = tree;

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(1, 0), grid, red);

        Assert.False(result.WasCapture);
        Assert.Same(tree, result.Destroyed);
    }

    [Fact]
    public void Move_OntoOwnGrave_ReportsDestroyedGrave()
    {
        // Burying a grave displaces it. The rule layer reports it; the
        // view layer decides whether to render anything (currently no
        // FX for graves — the unit's footprint covers them).
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        var grave = new Grave();
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = grave;

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(1, 0), grid, red);

        Assert.False(result.WasCapture);
        Assert.Same(grave, result.Destroyed);
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

    // --- Combining --------------------------------------------------------

    [Fact]
    public void Move_OntoOwnPeasant_ProducesSpearman_InheritingDestMoveState()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }

        var mover = new Unit(Red); // unmoved source
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = mover;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red); // unmoved dest

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(1, 0), grid, red);

        Assert.False(result.WasCapture);
        Assert.Null(grid.Get(HexCoord.FromOffset(2, 0))!.Unit); // source empty
        Unit? combined = grid.Get(HexCoord.FromOffset(1, 0))!.Unit;
        Assert.NotNull(combined);
        Assert.Equal(UnitLevel.Spearman, combined!.Level);
        Assert.Equal(Red, combined.Owner);
        // Dest was unmoved, so the combined unit inherits unmoved state.
        Assert.False(combined.HasMovedThisTurn);
    }

    [Fact]
    public void Move_OntoOwnMovedPeasant_ProducesMovedSpearman()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }

        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
            new Unit(Red) { HasMovedThisTurn = true };

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MovementRules.Move(HexCoord.FromOffset(2, 0), HexCoord.FromOffset(1, 0), grid, red);

        Unit? combined = grid.Get(HexCoord.FromOffset(1, 0))!.Unit;
        Assert.NotNull(combined);
        Assert.Equal(UnitLevel.Spearman, combined!.Level);
        // Dest was moved, so the combined unit inherits that.
        Assert.True(combined.HasMovedThisTurn);
    }

    [Fact]
    public void Move_OntoOwnSpearman_ProducesKnight()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }

        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Spearman);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MovementRules.Move(HexCoord.FromOffset(2, 0), HexCoord.FromOffset(1, 0), grid, red);

        Unit? combined = grid.Get(HexCoord.FromOffset(1, 0))!.Unit;
        Assert.NotNull(combined);
        Assert.Equal(UnitLevel.Knight, combined!.Level);
    }

    // --- Higher-level capture rules ---------------------------------------

    [Fact]
    public void ValidTargets_Spearman_CanCaptureTileDefendedByPeasant()
    {
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        // Place a Blue peasant defender on (2,0) — defense 1.
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Blue);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        // Spearman (level 2) > defense 1 → capturable.
        var targets = MovementRules.ValidTargets(UnitLevel.Spearman, red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void ValidTargets_Peasant_CannotCaptureTileDefendedByPeasant()
    {
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Blue);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        // Peasant (level 1) vs defense 1 → not strictly greater → excluded.
        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void ValidTargets_Baron_CanCaptureTileDefendedByKnight()
    {
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Blue, UnitLevel.Knight);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Baron, red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void ValidTargets_Knight_CannotCaptureTileDefendedByKnight()
    {
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Blue, UnitLevel.Knight);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Knight, red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(2, 0), targets);
    }

    // --- Grave tiles are placeable ---------------------------------------

    [Fact]
    public void ValidTargets_IncludesOwnGraveTile_AsReposition()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        // Put a grave on (1,0) — a previously-dead unit.
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Grave();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(1, 0), targets);
    }

    [Fact]
    public void Move_OntoOwnGrave_ClearsGraveAndConsumesAction()
    {
        // Burying a grave with a living unit takes real work — the
        // unit spends its action for the turn, same as chopping a
        // tree. This prevents a "free reposition onto a grave" loophole.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        var unit = new Unit(Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = unit;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Grave();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(1, 0), grid, red);

        Assert.False(result.WasCapture);
        Assert.Same(unit, grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
        Assert.True(unit.HasMovedThisTurn);
    }

    [Fact]
    public void PlaceNew_OntoOwnGrave_ClearsGraveAndConsumesAction()
    {
        // Buy-and-place onto a grave tile: the fresh peasant buries the
        // grave and spends its action. Matches PlaceNew-onto-tree.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Grave();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);
        var fresh = new Unit(Red);

        MovementRules.PlaceNew(fresh, HexCoord.FromOffset(1, 0), grid, red);

        Assert.Same(fresh, grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
        Assert.True(fresh.HasMovedThisTurn);
    }

    [Fact]
    public void PlaceNew_OntoOwnPeasant_CombinesIntoSpearman()
    {
        // Regression: PlaceNew used to overwrite the destination's
        // occupant with the fresh unit, silently destroying any
        // existing unit instead of combining.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);
        var fresh = new Unit(Red);

        MoveResult result = MovementRules.PlaceNew(
            fresh, HexCoord.FromOffset(1, 0), grid, red);

        Assert.False(result.WasCapture);
        Unit? combined = grid.Get(HexCoord.FromOffset(1, 0))!.Unit;
        Assert.NotNull(combined);
        Assert.Equal(UnitLevel.Spearman, combined!.Level);
    }

    [Fact]
    public void PlaceNew_OntoOwnMovedPeasant_ProducesMovedSpearman()
    {
        // Buying a peasant and placing it on an already-moved friendly
        // peasant should produce a MOVED spearman (inherits dest state),
        // not a fresh peasant.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
            new Unit(Red) { HasMovedThisTurn = true };

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MovementRules.PlaceNew(new Unit(Red), HexCoord.FromOffset(1, 0), grid, red);

        Unit? combined = grid.Get(HexCoord.FromOffset(1, 0))!.Unit;
        Assert.NotNull(combined);
        Assert.Equal(UnitLevel.Spearman, combined!.Level);
        Assert.True(combined.HasMovedThisTurn);
    }

    // --- Tower tiles: block own placement, blockable-by-defense capture --

    [Fact]
    public void ValidTargets_ExcludesOwnTowerTile()
    {
        // Own tower tile is NEVER a valid target: can't reposition,
        // can't combine onto it, can't buy onto it.
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tower();
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(1, 0), targets);
    }

    [Fact]
    public void ValidTargets_Knight_CanCaptureEnemyTowerTile()
    {
        // Tower defends at 2; knight (3) is strictly greater → capturable.
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tower();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Knight, red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void ValidTargets_Spearman_CannotCaptureEnemyTowerTile()
    {
        // Tower defense 2 vs spearman level 2 — strict-less rule says no.
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tower();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Spearman, red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void ValidTargets_Spearman_CannotCaptureTileAdjacentToEnemyTower()
    {
        // (2,0) is empty but adjacent to a tower on (3,0) — radiation
        // gives it defense 2, blocking a spearman.
        HexGrid grid = BuildGrid(5, 2, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 1), Red);
        SetTile(grid, HexCoord.FromOffset(1, 1), Red);
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Tower();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Spearman, red, grid, territories);

        Assert.DoesNotContain(HexCoord.FromOffset(2, 0), targets);
    }

    [Fact]
    public void Move_Capture_OntoEnemyTower_DestroysTowerAndPlacesUnit()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        SetTile(grid, HexCoord.FromOffset(0, 0), Red);
        SetTile(grid, HexCoord.FromOffset(1, 0), Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tower();

        var knight = new Unit(Red, UnitLevel.Knight);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = knight;

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0), grid, red);

        Assert.True(result.WasCapture);
        HexTile captured = grid.Get(HexCoord.FromOffset(2, 0))!;
        Assert.Equal(Red, captured.Color);
        Assert.Same(knight, captured.Unit);
        // Tower is gone — overwritten by the arriving unit.
        Assert.IsNotType<Tower>(captured.Occupant);
        Assert.True(knight.HasMovedThisTurn);
    }

    // --- Tree tiles: clearable, consume action ---------------------------

    [Fact]
    public void ValidTargets_IncludesOwnTreeTile_AsReposition()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Peasant, red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(1, 0), targets);
    }

    [Fact]
    public void Move_OntoOwnTree_ClearsTreeAndConsumesAction()
    {
        // Chopping a tree costs the unit its turn (unlike burying a
        // grave, which is free).
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0) })
        {
            SetTile(grid, c, Red);
        }
        var unit = new Unit(Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = unit;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(1, 0), grid, red);

        Assert.False(result.WasCapture);
        Assert.Same(unit, grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
        Assert.True(unit.HasMovedThisTurn);
    }

    [Fact]
    public void PlaceNew_OntoOwnTree_ClearsTreeAndConsumesAction()
    {
        HexGrid grid = BuildGrid(5, 1, Blue);
        foreach (var c in new[] { HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0) })
        {
            SetTile(grid, c, Red);
        }
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Tree();

        var territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);
        var fresh = new Unit(Red);

        MovementRules.PlaceNew(fresh, HexCoord.FromOffset(1, 0), grid, red);

        Assert.Same(fresh, grid.Get(HexCoord.FromOffset(1, 0))!.Unit);
        Assert.True(fresh.HasMovedThisTurn);
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
