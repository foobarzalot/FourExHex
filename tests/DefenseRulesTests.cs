using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class DefenseRulesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>
    /// Build a small grid consisting of the given coords, all colored
    /// <paramref name="color"/>. Returns the grid and the single territory
    /// covering all of them.
    /// </summary>
    private static (HexGrid grid, Territory territory) BuildBlob(
        PlayerId color,
        HexCoord? capital,
        params HexCoord[] coords)
    {
        var grid = new HexGrid();
        foreach (HexCoord c in coords)
        {
            grid.Add(new HexTile(c, color));
        }
        var territory = new Territory(color, coords, capital);
        return (grid, territory);
    }

    // --- Baseline --------------------------------------------------------

    [Fact]
    public void Defense_SingleEmptyTile_IsZero()
    {
        (HexGrid grid, Territory territory) = BuildBlob(Red, null, new HexCoord(0, 0));

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnCapital_IsOne()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnRecruit_IsOne()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnSoldier_IsTwo()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileWithOwnCommander_IsFour()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Commander);

        Assert.Equal(4, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    // --- Radiation -------------------------------------------------------

    [Fact]
    public void Defense_TileAdjacentToOwnCapital_RadiatesOne()
    {
        // Two-tile red territory; capital on (0,0). Defense of (1,0) should
        // be 1 (radiated from the capital), even though (1,0) is empty.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileAdjacentToOwnRecruit_RadiatesOne()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileAdjacentToOwnCaptain_RadiatesThree()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);

        Assert.Equal(3, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_MaxOverRecruitAndCommander_IsFour()
    {
        // Three-tile territory where (1,0) is between a recruit-held and
        // a commander-held tile. Defense of (1,0) = max(1, 4) = 4.
        var coords = new[]
        {
            new HexCoord(0, 0),   // W neighbor
            new HexCoord(1, 0),   // target
            new HexCoord(2, -1),  // NE neighbor
        };
        (HexGrid grid, Territory territory) = BuildBlob(Red, null, coords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red);
        grid.Get(new HexCoord(2, -1))!.Occupant = new Unit(Red, UnitLevel.Commander);

        Assert.Equal(4, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_AdjacentRecruitAndCapital_Max_IsOne()
    {
        // Every contributor is level 1 right now, so max(1, 1) = 1. The
        // point of this test is that the max function is applied at all
        // (and not, e.g., summed to 2).
        var coords = new[]
        {
            new HexCoord(0, 0),   // W neighbor of (1, 0)
            new HexCoord(1, 0),   // target
            new HexCoord(2, -1),  // NE neighbor of (1, 0)
        };
        (HexGrid grid, Territory territory) = BuildBlob(Red, new HexCoord(0, 0), coords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(2, -1))!.Occupant = new Unit(Red);

        Assert.Equal(1, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    // --- Territory scope -------------------------------------------------

    [Fact]
    public void Defense_AdjacentEnemyUnit_IsIgnored()
    {
        // Red tile at (0,0), Blue recruit at (1,0). The Blue unit does not
        // contribute to Red's defense of (0,0) because it's not in Red's
        // territory.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Blue);

        var redTerritory = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, redTerritory));
    }

    [Fact]
    public void Defense_AdjacentSameColorSiblingTerritory_IsIgnored()
    {
        // Same color, different territory. The sibling's unit doesn't
        // radiate because it isn't in leftRed.Coords.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Add(new HexTile(new HexCoord(2, 0), Red));
        grid.Add(new HexTile(new HexCoord(3, 0), Red));
        grid.Get(new HexCoord(2, 0))!.Occupant = new Unit(Red);

        var leftRed = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, leftRed));
    }

    // --- Towers ----------------------------------------------------------

    [Fact]
    public void Defense_TileWithOwnTower_IsTwo()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TileAdjacentToOwnTower_IsTwo_ViaRadiation()
    {
        // Tower at (0,0) radiates to same-territory neighbors, so the
        // empty (1,0) tile inherits defense 2 from its neighbor.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void Defense_AdjacentEnemyTower_DoesNotRadiateAcrossTerritories()
    {
        // Red at (0,0). Blue at (1,0) (adjacent, enemy). Blue tile has a
        // tower. A capture target computation treats (0,0)'s defense from
        // Red's perspective — the enemy tower on (1,0) MUST NOT radiate
        // into Red's territory, so Red's (0,0) is defense 0.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        var redTerritory = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        Assert.Equal(0, DefenseRules.Defense(new HexCoord(0, 0), grid, redTerritory));
    }

    [Fact]
    public void Defense_TowerPlusRecruitOnSameTile_IsTwo_NotThree()
    {
        // Contributions don't stack — the max wins. A recruit (1) plus a
        // tower (2) on overlapping coverage is still 2.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        // Adjacent recruit that radiates 1 into (0,0) — tower already
        // gives 2 so we expect 2, not 3.
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_CaptainNextToTower_TileIsThree_ViaCaptainMaxWins()
    {
        // A captain (3) on an adjacent same-territory tile beats the
        // tower's radiated 2 on the subject tile.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);

        Assert.Equal(3, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void Defense_TwoAdjacentTowers_DoNotStackBeyondTwo()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
        Assert.Equal(2, DefenseRules.Defense(new HexCoord(1, 0), grid, territory));
    }

    // --- CommittedDefense -------------------------------------------------
    //
    // Defense from occupants that are settled for the turn: towers,
    // capitals, and units that have already spent their move. Units with a
    // free move contribute nothing — they may march away. Used by AI tower
    // scoring to decide whether a border tile is already durably covered.

    [Fact]
    public void CommittedDefense_IgnoresFreeUnit()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);

        Assert.Equal(2, DefenseRules.Defense(new HexCoord(0, 0), grid, territory));
        Assert.Equal(0, DefenseRules.CommittedDefense(new HexCoord(0, 0), grid, territory));
        Assert.Equal(0, DefenseRules.CommittedDefense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void CommittedDefense_CountsLockedUnit_IncludingRadiation()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant =
            new Unit(Red, UnitLevel.Soldier) { HasMovedThisTurn = true };

        Assert.Equal(2, DefenseRules.CommittedDefense(new HexCoord(0, 0), grid, territory));
        Assert.Equal(2, DefenseRules.CommittedDefense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void CommittedDefense_LockedRecruitOnMountain_EarnsHighGround()
    {
        // Locked recruit (1) + mountain (+1) = 2; the same recruit with
        // its move intact contributes nothing, mountain or not.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.IsMountain = true;
        grid.Get(new HexCoord(0, 0))!.Occupant =
            new Unit(Red) { HasMovedThisTurn = true };

        Assert.Equal(2, DefenseRules.CommittedDefense(new HexCoord(0, 0), grid, territory));

        grid.Get(new HexCoord(0, 0))!.Unit!.HasMovedThisTurn = false;
        Assert.Equal(0, DefenseRules.CommittedDefense(new HexCoord(0, 0), grid, territory));
    }

    [Fact]
    public void CommittedDefense_CountsTowersAndCapitals()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(2, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();
        grid.Get(new HexCoord(2, 0))!.Occupant = new Tower();

        Assert.Equal(1, DefenseRules.CommittedDefense(new HexCoord(0, 0), grid, territory));
        Assert.Equal(2, DefenseRules.CommittedDefense(new HexCoord(1, 0), grid, territory));
    }

    [Fact]
    public void CommittedDefense_IgnoringCoord_ExcludesThatContribution()
    {
        // With the tower's coord ignored, nothing else defends: the
        // scorer uses this to keep a new tower from disqualifying its
        // own coverage tiles.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();

        Assert.Equal(0, DefenseRules.CommittedDefense(
            new HexCoord(1, 0), grid, territory, ignoring: new HexCoord(0, 0)));
        Assert.Equal(0, DefenseRules.CommittedDefense(
            new HexCoord(0, 0), grid, territory, ignoring: new HexCoord(0, 0)));
    }

    // --- BlockingDefenders ----------------------------------------------
    //
    // Identifies the specific defender coords whose contribution >= attacker
    // level, so the view layer can red-flash only those occupants when a
    // placement/movement is rejected for defense reasons. Mirrors the
    // iteration in Defense(...) but collects coords instead of taking a max.

    [Fact]
    public void BlockingDefenders_EmptyEnemyTile_NoDefenders_ReturnsEmpty()
    {
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));

        IReadOnlyList<HexCoord> blockers = DefenseRules
            .BlockingDefenders(new HexCoord(0, 0), UnitLevel.Recruit, grid, territory)
            .ToList();

        Assert.Empty(blockers);
    }

    [Fact]
    public void BlockingDefenders_TargetOccupiedByMatchingTower_IncludesTarget()
    {
        // Soldier (2) attacking a tile that itself holds a tower (2): the
        // tower's own contribution meets the attacker level, so it's the
        // blocker.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();

        IReadOnlyList<HexCoord> blockers = DefenseRules
            .BlockingDefenders(new HexCoord(0, 0), UnitLevel.Soldier, grid, territory)
            .ToList();

        Assert.Equal(new[] { new HexCoord(0, 0) }, blockers);
    }

    [Fact]
    public void BlockingDefenders_SoldierVsRecruitPlusAdjacentTower_OnlyTowerBlocks()
    {
        // The exact user-spec example. Target hex has a recruit; adjacent
        // same-territory hex has a tower; attacker is a Soldier (2). The
        // recruit contributes 1 — below the attacker level, so it does NOT
        // block. The tower contributes 2 — meets the attacker level, so it
        // blocks. Only the tower flashes.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Unit(Red); // recruit on target
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();   // adjacent tower

        IReadOnlyList<HexCoord> blockers = DefenseRules
            .BlockingDefenders(new HexCoord(0, 0), UnitLevel.Soldier, grid, territory)
            .ToList();

        Assert.Equal(new[] { new HexCoord(1, 0) }, blockers);
    }

    [Fact]
    public void BlockingDefenders_MultipleQualifyingDefenders_ReturnsAll()
    {
        // Recruit (1) attacking; target is between two same-territory
        // towers, each contributing 2. Both are blockers.
        var coords = new[]
        {
            new HexCoord(0, 0),   // W neighbor
            new HexCoord(1, 0),   // target
            new HexCoord(2, -1),  // NE neighbor
        };
        (HexGrid grid, Territory territory) = BuildBlob(Red, null, coords);
        grid.Get(new HexCoord(0, 0))!.Occupant = new Tower();
        grid.Get(new HexCoord(2, -1))!.Occupant = new Tower();

        IReadOnlyList<HexCoord> blockers = DefenseRules
            .BlockingDefenders(new HexCoord(1, 0), UnitLevel.Recruit, grid, territory)
            .ToList();

        Assert.Equal(2, blockers.Count);
        Assert.Contains(new HexCoord(0, 0), blockers);
        Assert.Contains(new HexCoord(2, -1), blockers);
    }

    [Fact]
    public void BlockingDefenders_DefenderInDifferentTerritory_Ignored()
    {
        // Red tile at (0,0). Blue tower at (1,0) (adjacent, enemy). When
        // computing blockers FOR Red's territory at (0,0), the Blue tower
        // does not contribute — different territory.
        var grid = new HexGrid();
        grid.Add(new HexTile(new HexCoord(0, 0), Red));
        grid.Add(new HexTile(new HexCoord(1, 0), Blue));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Tower();

        var redTerritory = new Territory(Red, new[] { new HexCoord(0, 0) }, capital: null);

        IReadOnlyList<HexCoord> blockers = DefenseRules
            .BlockingDefenders(new HexCoord(0, 0), UnitLevel.Soldier, grid, redTerritory)
            .ToList();

        Assert.Empty(blockers);
    }

    [Fact]
    public void BlockingDefenders_DefenderBelowAttackerLevel_Excluded()
    {
        // Adjacent recruit contributes 1; Soldier attacker (2) overpowers
        // it — recruit is NOT a blocker.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, null,
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(1, 0))!.Occupant = new Unit(Red);

        IReadOnlyList<HexCoord> blockers = DefenseRules
            .BlockingDefenders(new HexCoord(0, 0), UnitLevel.Soldier, grid, territory)
            .ToList();

        Assert.Empty(blockers);
    }

    [Fact]
    public void BlockingDefenders_TargetCapitalVsRecruitAttacker_Blocks()
    {
        // Capital contributes 1. Recruit attacker (level 1) needs strictly
        // greater than the defense, so 1 >= 1 means capital blocks. The
        // capital itself flashes.
        (HexGrid grid, Territory territory) = BuildBlob(
            Red, new HexCoord(0, 0),
            new HexCoord(0, 0), new HexCoord(1, 0));
        grid.Get(new HexCoord(0, 0))!.Occupant = new Capital();

        IReadOnlyList<HexCoord> blockers = DefenseRules
            .BlockingDefenders(new HexCoord(0, 0), UnitLevel.Recruit, grid, territory)
            .ToList();

        Assert.Equal(new[] { new HexCoord(0, 0) }, blockers);
    }
}
