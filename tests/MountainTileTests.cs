using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Mountain hex tiles: a per-tile <see cref="HexTile.IsMountain"/>
/// flag — high ground that gives no defense on its own, but grants a unit or
/// tower standing on it a <see cref="DefenseRules.MountainBonus"/> (+1) defense
/// bonus that radiates to same-owner neighbors like any other defender.
/// Passable, capturable (an empty mountain is defenseless), retains its terrain
/// when captured, leaves no grave on death, blocks tree spread, and carries no
/// income behavior of its own. Towers may be built on mountains; capitals never.
/// </summary>
public class MountainTileTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static HexGrid BuildRow(int qMin, int qMax, PlayerId owner)
    {
        var grid = new HexGrid();
        for (int q = qMin; q <= qMax; q++)
        {
            grid.Add(new HexTile(new HexCoord(q, 0), owner));
        }
        return grid;
    }

    private static Territory RowTerritory(int qMin, int qMax, PlayerId owner, HexCoord? capital)
    {
        var coords = new List<HexCoord>();
        for (int q = qMin; q <= qMax; q++) coords.Add(new HexCoord(q, 0));
        return new Territory(owner, coords, capital);
    }

    // --- Defense: empty mountain gives no benefit -----------------------

    [Fact]
    public void Defense_EmptyNeutralMountain_IsZero()
    {
        // A lone empty mountain gives no defense on its own.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), PlayerId.None));
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        var neutral = new Territory(PlayerId.None, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, neutral));
    }

    [Fact]
    public void Defense_EmptyTileWithoutMountain_IsZero()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), PlayerId.None));
        var neutral = new Territory(PlayerId.None, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, neutral));
    }

    [Fact]
    public void Defense_EmptyOwnedMountain_RadiatesNothing()
    {
        // Red owns (0,0) empty mountain and (1,0) empty, same territory. The
        // empty mountain confers no protection on its neighbor.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(0, 0));

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(1, 0), grid, red));
    }

    [Fact]
    public void Defense_EmptyMountainAdjacentToUnit_NoBonus()
    {
        // A Soldier on plain (0,0) next to an empty mountain (1,0): the empty
        // mountain adds nothing, so the soldier's tile defends at its base 2.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;
        Territory red = RowTerritory(0, 1, Red, capital: null);

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, red));
    }

    // --- Defense: occupant +1 bonus and its radiation -------------------

    [Fact]
    public void Defense_SoldierOnMountain_IsThree()
    {
        // Soldier (2) on a mountain gets +1 → 3.
        HexGrid grid = BuildRow(0, 0, Red);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsMountain = true;
        tile.Occupant = new Unit(Red, UnitLevel.Soldier);
        Territory red = RowTerritory(0, 0, Red, capital: null);

        Assert.Equal(3, DefenseRules.Defense(new HexCoord(0, 0), grid, red));
    }

    [Fact]
    public void Defense_TowerOnMountain_IsThree()
    {
        // Tower (2) on a mountain gets +1 → 3.
        HexGrid grid = BuildRow(0, 0, Red);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsMountain = true;
        tile.Occupant = new Tower();
        Territory red = RowTerritory(0, 0, Red, capital: null);

        Assert.Equal(3, DefenseRules.Defense(new HexCoord(0, 0), grid, red));
    }

    [Fact]
    public void Defense_CommanderOnMountain_IsFive()
    {
        // Commander (4) on a mountain gets +1 → 5 (the bonus adds, the max with
        // the plain occupant value is still 5).
        HexGrid grid = BuildRow(0, 0, Red);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsMountain = true;
        tile.Occupant = new Unit(Red, UnitLevel.Commander);
        Territory red = RowTerritory(0, 0, Red, capital: null);

        Assert.Equal(5, DefenseRules.Defense(new HexCoord(0, 0), grid, red));
    }

    [Fact]
    public void Defense_CapitalOnMountain_IsTwo()
    {
        // A capital (1) on a mountain gets the +1 high-ground bonus like any
        // other defender → 2.
        HexGrid grid = BuildRow(0, 0, Red);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsMountain = true;
        tile.Occupant = new Capital();
        Territory red = RowTerritory(0, 0, Red, capital: new HexCoord(0, 0));

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, red));
    }

    [Fact]
    public void Defense_CapitalOnMountain_RadiatesBoostedValueToNeighbor()
    {
        // Capital on mountain (0,0) → 2; the boosted value radiates to the
        // same-territory empty neighbor (1,0).
        HexGrid grid = BuildRow(0, 1, Red);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsMountain = true;
        tile.Occupant = new Capital();
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(0, 0));

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(1, 0), grid, red));
    }

    [Fact]
    public void Defense_UnitOnMountain_RadiatesBoostedValueToNeighbor()
    {
        // Soldier on mountain (0,0) → 3; the boosted value radiates to the
        // same-territory empty neighbor (1,0).
        HexGrid grid = BuildRow(0, 1, Red);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsMountain = true;
        tile.Occupant = new Unit(Red, UnitLevel.Soldier);
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(1, 0));

        Assert.Equal(3, DefenseRules.Defense(new HexCoord(1, 0), grid, red));
    }

    [Fact]
    public void Defense_NeutralMountain_DoesNotRadiateToPlayerNeighbor()
    {
        // Soldier on a neutral-territory mountain at (0,0); Red owns (1,0). The
        // mountain tile is not in Red's territory, so its bonus does not reach
        // Red's tile.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), PlayerId.None));
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        Territory red = new Territory(Red, new[] { new HexCoord(1, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(1, 0), grid, red));
    }

    // --- Capture thresholds ---------------------------------------------

    [Fact]
    public void ValidTargets_RecruitCanCaptureEmptyNeutralMountain()
    {
        // Red unit at (0,0); empty neutral mountain at (1,0). An empty mountain
        // has defense 0, so even a Recruit can take it.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), PlayerId.None));
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;

        Territory red = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);
        Territory neutral = new Territory(PlayerId.None, new[] { new HexCoord(1, 0) }, capital: null);
        var all = new List<Territory> { red, neutral };

        List<HexCoord> recruitTargets = MovementRules.ValidTargets(UnitLevel.Recruit, red, grid, all);

        Assert.Contains(new HexCoord(1, 0), recruitTargets);
    }

    [Fact]
    public void ValidTargets_DefendedMountain_RaisesCaptureThreshold()
    {
        // Neutral mountain at (1,0) holding a Soldier (defense 2+1=3). A Captain
        // (3) cannot take it (needs strictly greater), a Commander (4) can.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), PlayerId.None));
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Soldier);

        Territory red = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);
        Territory neutral = new Territory(PlayerId.None, new[] { new HexCoord(1, 0) }, capital: null);
        var all = new List<Territory> { red, neutral };

        List<HexCoord> captainTargets = MovementRules.ValidTargets(UnitLevel.Captain, red, grid, all);
        List<HexCoord> commanderTargets = MovementRules.ValidTargets(UnitLevel.Commander, red, grid, all);

        Assert.DoesNotContain(new HexCoord(1, 0), captainTargets);
        Assert.Contains(new HexCoord(1, 0), commanderTargets);
    }

    [Fact]
    public void Capture_CaptainTakesMountain_OwnerChangesFlagPersistsUnitOccupies()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        grid.Add(new HexTile(new HexCoord(1, 0), PlayerId.None));
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;

        Territory red = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        MoveResult result = MovementRules.Move(new HexCoord(0, 0), new HexCoord(1, 0), grid, red);

        HexTile captured = grid.Get(new HexCoord(1, 0))!;
        Assert.True(result.WasCapture);
        Assert.Equal(Red, captured.Owner);
        Assert.True(captured.IsMountain);                 // terrain persists
        Assert.IsType<Unit>(captured.Occupant);
        Assert.Equal(UnitLevel.Captain, ((Unit)captured.Occupant!).Level);
    }

    // --- Tree spread -----------------------------------------------------

    [Fact]
    public void TreeSpread_OntoMountainTile()
    {
        // (0,0) and (2,0) trees, (1,0) empty mountain between them: with two
        // tree neighbors a tree spreads onto the mountain — trees and
        // mountains coexist.
        HexGrid grid = BuildRow(0, 2, Red);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;

        TreeRules.RunStartOfTurnGrowth(grid, Red, new HashSet<HexCoord>());

        Assert.IsType<Tree>(grid.Get(new HexCoord(1, 0))!.Occupant);
        Assert.True(grid.Get(new HexCoord(1, 0))!.IsMountain);   // terrain kept
    }

    // --- Tower placement -------------------------------------------------

    [Fact]
    public void IsValidTowerLocation_TrueOnEmptyMountain()
    {
        // Towers may now be built on mountains (the +1 high-ground bonus is the
        // whole point). An occupied mountain is still rejected.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(1, 0));

        Assert.True(PurchaseRules.IsValidTowerLocation(
            grid.Get(new HexCoord(0, 0))!, red, grid));

        // sanity: an occupied mountain is not a valid tower site.
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        Assert.False(PurchaseRules.IsValidTowerLocation(
            grid.Get(new HexCoord(0, 0))!, red, grid));
    }

    // --- Grave suppression on death --------------------------------------

    [Fact]
    public void Upkeep_UnitDyingOnMountain_LeavesGrave()
    {
        // Bankrupt territory: a unit dying on a mountain now leaves a grave,
        // same as plain land — graves and mountains coexist.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(1, 0));
        var treasury = new Treasury();            // no gold → bankrupt

        bool solvent = UpkeepRules.ApplyUpkeep(red, grid, treasury, Difficulty.Soldier);

        Assert.False(solvent);
        Assert.IsType<Grave>(grid.Get(new HexCoord(0, 0))!.Occupant); // grave on mountain
        Assert.True(grid.Get(new HexCoord(0, 0))!.IsMountain);        // terrain kept
        Assert.IsType<Grave>(grid.Get(new HexCoord(1, 0))!.Occupant); // grave on plain land
    }

    // --- Capital placement allowed on mountains ------

    [Fact]
    public void CapitalPlacer_PlacesOnMountainTiles()
    {
        // Capitals now sit on mountains like any other terrain. With a mountain
        // at (0,0) and plain at (1,0), both empty, the lex-min (0,0) is chosen.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        var coords = new[] { new HexCoord(0, 0), new HexCoord(1, 0) };

        HexCoord? chosen = CapitalPlacer.Choose(coords, grid);

        Assert.Equal(new HexCoord(0, 0), chosen);
    }

    [Fact]
    public void CapitalPlacer_AllMountains_PlacesCapital()
    {
        // An all-mountain region is now a normal territory: it gets a capital
        // (lex-min empty tile), not null.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;
        var coords = new[] { new HexCoord(0, 0), new HexCoord(1, 0) };

        Assert.Equal(new HexCoord(0, 0), CapitalPlacer.Choose(coords, grid));
    }

    [Fact]
    public void Reconcile_MountainsOnlyRegion_FormsCapital()
    {
        // A 2-tile owned region made entirely of mountains is now a real
        // territory and gets a capital like any other.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;

        IReadOnlyList<Territory> reconciled = TerritoryFinder.Recompute(
            grid, new List<Territory>(), treasury: null);

        Territory redRegion = reconciled.Single(t => t.Owner == Red);
        Assert.True(redRegion.HasCapital);
        Assert.Equal(2, redRegion.Size);
        // A capital occupant was placed on the mountain, which keeps its flag.
        HexTile capTile = grid.Get(redRegion.Capital!.Value)!;
        Assert.IsType<Capital>(capTile.Occupant);
        Assert.True(capTile.IsMountain);
    }

    // --- Snapshot deep-copy (undo/redo) ----------------------------------

    [Fact]
    public void GameStateSnapshot_RoundTrips_IsMountain()
    {
        HexGrid grid = BuildRow(0, 2, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        var territories = new List<Territory> { RowTerritory(0, 2, Red, capital: new HexCoord(1, 0)) };
        var treasury = new Treasury();

        GameStateSnapshot snap = GameStateSnapshot.Capture(grid, treasury, territories);

        grid.Get(new HexCoord(0, 0))!.IsMountain = false;
        grid.Get(new HexCoord(2, 0))!.IsMountain = true;

        snap.ApplyTo(grid, treasury);

        Assert.True(grid.Get(new HexCoord(0, 0))!.IsMountain);
        Assert.False(grid.Get(new HexCoord(2, 0))!.IsMountain);
    }

    [Fact]
    public void EditorSnapshot_RoundTrips_IsMountain()
    {
        HexGrid grid = BuildRow(0, 2, Red);
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;
        var water = new HashSet<HexCoord>();
        var territories = new List<Territory> { RowTerritory(0, 2, Red, capital: new HexCoord(0, 0)) };

        EditorSnapshot snap = EditorSnapshot.Capture(grid, water, territories);

        grid.Get(new HexCoord(1, 0))!.IsMountain = false;

        snap.ApplyTo(grid, water);

        Assert.True(grid.Get(new HexCoord(1, 0))!.IsMountain);
    }
}
