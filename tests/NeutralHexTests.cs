using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Feature-level coverage for neutral (unowned, <see cref="PlayerId.None"/>)
/// hexes (issue #39): a land tile owned by no player, capturable by any
/// adjacent player exactly like enemy territory, but generating no income
/// and belonging to no player's territory while neutral. Placement is
/// editor-only (see <see cref="MapEditPaintTests"/>); these tests pin the
/// downstream behavior in territory finding, capture, income, AI, and
/// serialization.
/// </summary>
public class NeutralHexTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>
    /// Red strip cols 0..3 on row 0, with a single neutral tile at col 4.
    /// Col 3 (Red) borders the neutral tile, so a Red unit there can
    /// capture it.
    /// </summary>
    private static HexGrid BuildRedStripWithNeutral()
    {
        var grid = new HexGrid();
        for (int col = 0; col <= 3; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), PlayerId.None));
        return grid;
    }

    private static GameState BuildState(HexGrid grid, params Player[] players)
    {
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var list = players.ToList();
        return new GameState(grid, territories, list, new TurnState(list), new Treasury());
    }

    // --- Territory finding -------------------------------------------------

    [Fact]
    public void TerritoryFinder_GroupsNeutralTilesIntoOwnNoneTerritory()
    {
        HexGrid grid = BuildRedStripWithNeutral();

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        Territory neutral = Assert.Single(territories.Where(t => t.Owner.IsNone));
        Assert.Contains(HexCoord.FromOffset(4, 0), neutral.Coords);
        Assert.False(neutral.HasCapital);

        Territory red = territories.First(t => t.Owner == Red);
        Assert.DoesNotContain(HexCoord.FromOffset(4, 0), red.Coords);
    }

    // --- Defense + capture -------------------------------------------------

    [Fact]
    public void EmptyNeutralHex_HasZeroDefense()
    {
        HexGrid grid = BuildRedStripWithNeutral();
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory neutral = territories.First(t => t.Owner.IsNone);

        int defense = DefenseRules.Defense(HexCoord.FromOffset(4, 0), grid, neutral);

        Assert.Equal(0, defense);
    }

    [Fact]
    public void RecruitCanCaptureAdjacentNeutralHex()
    {
        HexGrid grid = BuildRedStripWithNeutral();
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        var targets = MovementRules.ValidTargets(UnitLevel.Recruit, red, grid, territories);

        Assert.Contains(HexCoord.FromOffset(4, 0), targets);
    }

    [Fact]
    public void CapturingNeutralHex_FlipsOwnerToAttacker_AndMergesTerritory()
    {
        HexGrid grid = BuildRedStripWithNeutral();
        // Put a Red recruit on the border tile (col 3) and let it capture.
        var source = HexCoord.FromOffset(3, 0);
        var neutral = HexCoord.FromOffset(4, 0);
        grid.Get(source)!.Occupant = new Unit(Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        MoveResult result = MovementRules.Move(source, neutral, grid, red);

        Assert.True(result.WasCapture);
        Assert.Equal(Red, grid.Get(neutral)!.Owner);

        IReadOnlyList<Territory> after = TerritoryFinder.Recompute(grid, territories);
        Assert.DoesNotContain(after, t => t.Owner.IsNone);
        Territory redAfter = after.First(t => t.Owner == Red);
        Assert.Contains(neutral, redAfter.Coords);
    }

    // --- Income ------------------------------------------------------------

    [Fact]
    public void NeutralHex_ContributesNoIncome_ButDoesAfterCapture()
    {
        HexGrid grid = BuildRedStripWithNeutral();
        var source = HexCoord.FromOffset(3, 0);
        var neutral = HexCoord.FromOffset(4, 0);
        grid.Get(source)!.Occupant = new Unit(Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory red = territories.First(t => t.Owner == Red);

        int incomeBefore = IncomeRules.IncomeFor(red, grid);
        // The neutral tile is not part of Red's territory, so it can't
        // contribute to Red's income while neutral.
        Assert.DoesNotContain(neutral, red.Coords);

        MovementRules.Move(source, neutral, grid, red);
        IReadOnlyList<Territory> after = TerritoryFinder.Recompute(grid, territories);
        Territory redAfter = after.First(t => t.Owner == Red);

        int incomeAfter = IncomeRules.IncomeFor(redAfter, grid);
        Assert.Equal(incomeBefore + 1, incomeAfter);
    }

    // --- AI ----------------------------------------------------------------

    [Fact]
    public void AiEnumerate_OffersNeutralHexAsCaptureCandidate()
    {
        HexGrid grid = BuildRedStripWithNeutral();
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(Red);
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer),
            new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer));
        Territory red = state.Territories.First(t => t.Owner == Red);

        List<AiCandidate> candidates = AiCommon.Enumerate(red, state).ToList();

        Assert.Contains(candidates, c =>
            c.Kind == AiActionKind.Capture
            && c.Action is AiMoveAction move
            && move.Destination == HexCoord.FromOffset(4, 0));
    }

    [Fact]
    public void AiScorerAndSimulator_RunOnMapWithNeutralTiles()
    {
        HexGrid grid = BuildRedStripWithNeutral();
        GameState state = BuildState(grid,
            new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer),
            new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer));

        // Neither must throw on a map containing a None-owned territory.
        int score = AiStateScorer.Score(state, Red);
        GameState clone = AiSimulator.Clone(state);

        Assert.Contains(clone.Territories, t => t.Owner.IsNone);
        // Score is a deterministic int; just assert it computed.
        Assert.Equal(score, AiStateScorer.Score(state, Red));
    }

    // --- Serialization -----------------------------------------------------

    [Fact]
    public void Serialization_RoundTripsNeutralTiles()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = BuildRedStripWithNeutral();
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players), new Treasury());

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        HexTile? reloaded = loaded.State.Grid.Get(HexCoord.FromOffset(4, 0));
        Assert.NotNull(reloaded);
        Assert.True(reloaded!.Owner.IsNone);
    }

    // --- Towers on neutral hexes ------------------------------------------

    /// <summary>
    /// Row 0: Red {0,1,2}, neutral {3,4}, Red {5,6}. A tower sits on the
    /// neutral hex at col 3. The two neutral hexes form one neutral
    /// territory, so the tower should protect its own hex AND radiate to
    /// the adjacent neutral hex at col 4.
    /// </summary>
    private static (HexGrid grid, IReadOnlyList<Territory> territories) BuildNeutralRegionWithTower()
    {
        var grid = new HexGrid();
        for (int col = 0; col <= 2; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Add(new HexTile(HexCoord.FromOffset(3, 0), PlayerId.None));
        grid.Add(new HexTile(HexCoord.FromOffset(4, 0), PlayerId.None));
        for (int col = 5; col <= 6; col++)
            grid.Add(new HexTile(HexCoord.FromOffset(col, 0), Red));
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Tower();

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return (grid, territories);
    }

    [Fact]
    public void TowerOnNeutralHex_ProtectsItsOwnHex()
    {
        (HexGrid grid, IReadOnlyList<Territory> territories) = BuildNeutralRegionWithTower();
        Territory neutral = territories.First(
            t => t.Owner.IsNone && t.Coords.Contains(HexCoord.FromOffset(3, 0)));

        int defense = DefenseRules.Defense(HexCoord.FromOffset(3, 0), grid, neutral);

        Assert.Equal(2, defense); // Tower contributes 2.
    }

    [Fact]
    public void TowerOnNeutralHex_RadiatesToAdjacentNeutralHex()
    {
        (HexGrid grid, IReadOnlyList<Territory> territories) = BuildNeutralRegionWithTower();
        Territory neutral = territories.First(
            t => t.Owner.IsNone && t.Coords.Contains(HexCoord.FromOffset(4, 0)));

        int defense = DefenseRules.Defense(HexCoord.FromOffset(4, 0), grid, neutral);

        Assert.Equal(2, defense); // Radiated from the tower on col 3.
    }

    [Fact]
    public void TowerDefendedNeutralHex_NeedsCaptainToCapture()
    {
        (HexGrid grid, IReadOnlyList<Territory> territories) = BuildNeutralRegionWithTower();
        // Left Red territory {0,1,2} borders the tower hex at col 3.
        Territory leftRed = territories.First(
            t => t.Owner == Red && t.Coords.Contains(HexCoord.FromOffset(2, 0)));
        var towerHex = HexCoord.FromOffset(3, 0);

        var soldierTargets = MovementRules.ValidTargets(UnitLevel.Soldier, leftRed, grid, territories);
        var captainTargets = MovementRules.ValidTargets(UnitLevel.Captain, leftRed, grid, territories);

        Assert.DoesNotContain(towerHex, soldierTargets); // defense 2, soldier (2) can't.
        Assert.Contains(towerHex, captainTargets);        // captain (3) > 2 can.
    }

    [Fact]
    public void TowerRadiation_ProtectsAdjacentNeutralHexFromSoldierCapture()
    {
        (HexGrid grid, IReadOnlyList<Territory> territories) = BuildNeutralRegionWithTower();
        // Right Red territory {5,6} borders the radiated neutral hex at col 4.
        Territory rightRed = territories.First(
            t => t.Owner == Red && t.Coords.Contains(HexCoord.FromOffset(5, 0)));
        var radiatedHex = HexCoord.FromOffset(4, 0);

        var soldierTargets = MovementRules.ValidTargets(UnitLevel.Soldier, rightRed, grid, territories);
        var captainTargets = MovementRules.ValidTargets(UnitLevel.Captain, rightRed, grid, territories);

        Assert.DoesNotContain(radiatedHex, soldierTargets); // radiated defense 2 blocks soldier.
        Assert.Contains(radiatedHex, captainTargets);
    }

    // --- Controller capture path + instrumentation -------------------------

    [Fact]
    public void HumanCaptureOfNeutralHex_FlipsOwnership_AndLogsNeutralCapture()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };

        // 5x2 board: Red owns (0,1)/(1,1); (2,1) is neutral; Blue elsewhere.
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(2, 1))!.Owner = PlayerId.None;
        var unit = new Unit(red.Id);
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = unit;

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var controller = new GameController(state, session, map, new MockHudView());
        controller.StartGame();

        var captured = new List<string>();
        Action<string>? savedSink = Log.Sink;
        try
        {
            Log.ResetLevels();
            Log.SetLevel(Log.LogCategory.Capture, Log.LogLevel.Debug);
            Log.Sink = captured.Add;

            // Select the Red unit, then click the neutral tile to capture it.
            map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(1, 1)));
            map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(2, 1)));
        }
        finally
        {
            Log.Sink = savedSink;
            Log.ResetLevels();
        }

        Assert.Equal(red.Id, grid.Get(HexCoord.FromOffset(2, 1))!.Owner);
        Assert.Contains(captured, line => line.Contains("[capture] neutral hex"));
    }
}
