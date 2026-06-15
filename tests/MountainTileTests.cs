using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Mountain hex tiles (issue #37): a per-tile <see cref="HexTile.IsMountain"/>
/// flag — defensive terrain that contributes tower-strength defense
/// (<see cref="DefenseRules.MountainDefense"/>) to itself and radiates it to
/// same-owner neighbors. Passable, capturable by Captain/Commander without
/// being destroyed, leaves no grave on death, blocks tree spread / tower /
/// capital placement, and carries no income behavior of its own.
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

    // --- Defense: self-defense ------------------------------------------

    [Fact]
    public void Defense_NeutralMountain_SelfDefendsAtTowerStrength()
    {
        // A lone neutral mountain defends only itself, at strength 2.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), PlayerId.None));
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        var neutral = new Territory(PlayerId.None, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, neutral));
    }

    [Fact]
    public void Defense_EmptyTileWithoutMountain_IsZero()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), PlayerId.None));
        var neutral = new Territory(PlayerId.None, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, neutral));
    }

    // --- Defense: radiation to friendly neighbors -----------------------

    [Fact]
    public void Defense_OwnedMountain_RadiatesToSameOwnerNeighbor()
    {
        // Red owns (0,0) mountain and (1,0) empty, same territory. The empty
        // tile is protected by the adjacent mountain.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(0, 0));

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(1, 0), grid, red));
    }

    [Fact]
    public void Defense_NeutralMountain_DoesNotRadiateToPlayerNeighbor()
    {
        // Neutral mountain at (0,0); Red owns (1,0). The neutral mountain is
        // not in Red's territory, so it does not protect Red's tile.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), PlayerId.None));
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Add(new HexTile(new HexCoord(1, 0), Red));
        Territory red = new Territory(Red, new[] { new HexCoord(1, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(1, 0), grid, red));
    }

    // --- Defense: non-cumulative (max) ----------------------------------

    [Fact]
    public void Defense_CommanderOnMountain_IsMaxNotSum()
    {
        // Commander (4) standing on a mountain (2): defense is max(4,2)=4, not 6.
        HexGrid grid = BuildRow(0, 0, Red);
        HexTile tile = grid.Get(new HexCoord(0, 0))!;
        tile.IsMountain = true;
        tile.Occupant = new Unit(Red, UnitLevel.Commander);
        Territory red = RowTerritory(0, 0, Red, capital: null);

        Assert.Equal(4, DefenseRules.Defense(new HexCoord(0, 0), grid, red));
    }

    // --- Capture thresholds ---------------------------------------------

    [Fact]
    public void ValidTargets_SoldierCannotCaptureNeutralMountain_CaptainCan()
    {
        // Red unit at (0,0); neutral mountain at (1,0). Mountain defense=2.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), PlayerId.None));
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;

        Territory red = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);
        Territory neutral = new Territory(PlayerId.None, new[] { new HexCoord(1, 0) }, capital: null);
        var all = new List<Territory> { red, neutral };

        List<HexCoord> soldierTargets = MovementRules.ValidTargets(UnitLevel.Soldier, red, grid, all);
        List<HexCoord> captainTargets = MovementRules.ValidTargets(UnitLevel.Captain, red, grid, all);

        Assert.DoesNotContain(new HexCoord(1, 0), soldierTargets);
        Assert.Contains(new HexCoord(1, 0), captainTargets);
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
    public void TreeSpread_SkipsMountainTile()
    {
        // (0,0) and (2,0) trees, (1,0) empty mountain between them: with two
        // tree neighbors it would normally spread, but the mountain blocks it.
        HexGrid grid = BuildRow(0, 2, Red);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tree();
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;

        TreeRules.RunStartOfTurnGrowth(grid, Red, new HashSet<HexCoord>());

        Assert.Null(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    // --- Tower placement -------------------------------------------------

    [Fact]
    public void IsValidTowerLocation_FalseOnEmptyMountain()
    {
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(1, 0));

        Assert.False(PurchaseRules.IsValidTowerLocation(
            grid.Get(new HexCoord(0, 0))!, red, grid));
        // sanity: a non-mountain empty tile in the same territory is valid.
        Assert.True(PurchaseRules.IsValidTowerLocation(
            grid.Get(new HexCoord(1, 0))!, red, grid));
    }

    // --- Grave suppression on death --------------------------------------

    [Fact]
    public void Upkeep_UnitDyingOnMountain_LeavesNoGrave()
    {
        // Bankrupt territory: unit on a mountain leaves the tile empty; unit
        // on plain land leaves a grave.
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        Territory red = RowTerritory(0, 1, Red, capital: new HexCoord(1, 0));
        var treasury = new Treasury();            // no gold → bankrupt

        bool solvent = UpkeepRules.ApplyUpkeep(red, grid, treasury, Difficulty.Soldier);

        Assert.False(solvent);
        Assert.Null(grid.Get(new HexCoord(0, 0))!.Occupant);          // no grave on mountain
        Assert.IsType<Grave>(grid.Get(new HexCoord(1, 0))!.Occupant); // grave on plain land
    }

    // --- Capital placement skips mountains -------------------------------

    [Fact]
    public void CapitalPlacer_SkipsMountainTiles()
    {
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;   // not a valid capital site
        var coords = new[] { new HexCoord(0, 0), new HexCoord(1, 0) };

        HexCoord? chosen = CapitalPlacer.Choose(coords, grid);

        Assert.Equal(new HexCoord(1, 0), chosen);
    }

    [Fact]
    public void CapitalPlacer_AllMountains_ReturnsNull()
    {
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;
        var coords = new[] { new HexCoord(0, 0), new HexCoord(1, 0) };

        Assert.Null(CapitalPlacer.Choose(coords, grid));
    }

    [Fact]
    public void Reconcile_MountainsOnlyRegion_HasNoCapital()
    {
        // A 2-tile owned region made entirely of mountains is not a real
        // territory: it stays capital-less (acts as singletons).
        HexGrid grid = BuildRow(0, 1, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;

        IReadOnlyList<Territory> reconciled = TerritoryFinder.Recompute(
            grid, new List<Territory>(), treasury: null);

        Territory redRegion = reconciled.Single(t => t.Owner == Red);
        Assert.False(redRegion.HasCapital);
        Assert.Equal(2, redRegion.Size);
        // No capital occupant was placed on the grid either.
        Assert.Null(grid.Get(new HexCoord(0, 0))!.Occupant);
        Assert.Null(grid.Get(new HexCoord(1, 0))!.Occupant);
    }

    [Fact]
    public void Reconcile_MountainRegionGainsLand_FormsCapital()
    {
        // Two mountains + one plain land tile: now there IS a legal capital
        // site, so a real territory forms.
        HexGrid grid = BuildRow(0, 2, Red);
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(1, 0))!.IsMountain = true;
        // (2,0) is plain land.

        IReadOnlyList<Territory> reconciled = TerritoryFinder.Recompute(
            grid, new List<Territory>(), treasury: null);

        Territory redRegion = reconciled.Single(t => t.Owner == Red);
        Assert.True(redRegion.HasCapital);
        Assert.Equal(new HexCoord(2, 0), redRegion.Capital!.Value);   // capital on the only land tile
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
