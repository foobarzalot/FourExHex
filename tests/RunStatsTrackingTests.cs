// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Per-game run stats (<see cref="RunStats"/> on <see cref="GameState"/>):
/// which live code paths increment which counter, that undo rewinds them,
/// and that they round-trip saves. These are observation-only counters —
/// nothing here may perturb rules, AI, or the determinism checksum.
/// </summary>
public class RunStatsTrackingTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>
    /// One-row board: Red owns cols 0..2, Blue the rest (of 6), with a
    /// col-3 override available for neutral ground. Occupants are placed
    /// by <paramref name="setup"/> before territories are built so
    /// capitals land on unoccupied tiles.
    /// </summary>
    private static (GameState State, SessionState Session, MockHexMapView Map,
                    MockHudView Hud, GameController Controller)
        BuildGame(
            GameMode mode = GameMode.Freeform,
            PlayerId? colThreeOwner = null,
            System.Action<HexGrid>? setup = null)
    {
        var redP = new Player("Red", PlayerId.FromIndex(0));
        var blueP = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
        var players = new List<Player> { redP, blueP };

        HexGrid grid = TestHelpers.BuildRectGrid(6, 1, Blue);
        for (int col = 0; col <= 2; col++)
        {
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = Red;
        }
        if (colThreeOwner.HasValue)
        {
            grid.Get(HexCoord.FromOffset(3, 0))!.Owner = colThreeOwner.Value;
        }
        setup?.Invoke(grid);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            mode: mode);
        var session = new SessionState();
        foreach (Player p in players)
        {
            session.ClaimVictoryPromptedHighestThreshold[p.Id] = 90;
        }
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();
        return (state, session, map, hud, controller);
    }

    private static void Click((GameState State, SessionState Session, MockHexMapView Map,
        MockHudView Hud, GameController Controller) g, int col)
        => g.Map.SimulateClick(g.State.Grid.Get(HexCoord.FromOffset(col, 0)));

    // --- Units lost ---------------------------------------------------------

    [Fact]
    public void CaptureThatDestroysAUnit_CountsTheVictimsLoss()
    {
        var g = BuildGame(setup: grid =>
        {
            grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
            grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(Blue);
        });

        Click(g, 2);
        Click(g, 3);

        Assert.Equal(1, g.State.Stats.For(Blue).UnitsLost);
        Assert.Equal(0, g.State.Stats.For(Red).UnitsLost);
    }

    [Fact]
    public void BankruptUpkeep_CountsEveryDisbandedUnit()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 1, Red);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var treasury = new Treasury();
        var stats = new RunStats();

        bool paid = UpkeepRules.ApplyUpkeepFor(Red, territories, grid, treasury, stats);

        Assert.True(paid); // "any bankrupt" convention: true = someone went bankrupt
        Assert.Equal(2, stats.For(Red).UnitsLost);
    }

    [Fact]
    public void TideSubmerge_CountsTheDrownedUnit()
    {
        var redP = new Player("Red", PlayerId.FromIndex(0));
        var blueP = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { redP, blueP };
        HexGrid grid = TestHelpers.BuildRectGrid(4, 1, Blue);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            mode: GameMode.RisingTides);

        var plan = new[] { new TideStep(HexCoord.FromOffset(1, 0), DemoteOnly: false) };
        RisingTidesRules.ApplyForecast(state, Red, plan);

        Assert.Equal(1, state.Stats.For(Red).UnitsLost);
    }

    // --- Towers built -------------------------------------------------------

    [Fact]
    public void BuildTower_CountsForTheBuilder_AndUndoRewinds()
    {
        var g = BuildGame();
        Click(g, 0); // select Red's territory
        HexCoord capital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(capital, 20);

        g.Hud.ClickBuildTower();
        Click(g, 1); // empty own tile

        Assert.Equal(1, g.State.Stats.For(Red).TowersBuilt);

        g.Hud.ClickUndoLast();

        Assert.Equal(0, g.State.Stats.For(Red).TowersBuilt);
    }

    // --- Max unit level fielded --------------------------------------------

    [Fact]
    public void BuyRecruit_TracksMaxUnitLevelFielded()
    {
        var g = BuildGame();
        Click(g, 0);
        HexCoord capital = g.Session.SelectedTerritory!.Capital!.Value;
        g.State.Treasury.SetGold(capital, 25);

        g.Hud.ClickBuyRecruit();
        Click(g, 1);

        Assert.Equal((int)UnitLevel.Recruit, g.State.Stats.For(Red).MaxUnitLevelFielded);
    }

    [Fact]
    public void CombineToCommander_TracksLevelFour()
    {
        var g = BuildGame(setup: grid =>
        {
            grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Captain);
            grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red);
        });

        Click(g, 2);
        Click(g, 1); // Recruit onto Captain — combines into a Commander

        Assert.Equal((int)UnitLevel.Commander, g.State.Stats.For(Red).MaxUnitLevelFielded);
    }

    // --- Viking kills -------------------------------------------------------

    [Fact]
    public void KillingANeutralUnit_InVikingRaiders_CreditsTheKiller()
    {
        var g = BuildGame(
            mode: GameMode.VikingRaiders,
            colThreeOwner: PlayerId.None,
            setup: grid =>
            {
                grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
                grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(PlayerId.None);
            });

        Click(g, 2);
        Click(g, 3);

        Assert.Equal(1, g.State.Stats.For(Red).VikingKills);
        Assert.Equal(0, g.State.Stats.For(Red).UnitsLost);
    }

    [Fact]
    public void KillingANeutralUnit_OutsideVikingRaiders_CountsNothing()
    {
        // Barbarians are None-owned too; only Viking Raiders mode credits kills.
        var g = BuildGame(
            mode: GameMode.Freeform,
            colThreeOwner: PlayerId.None,
            setup: grid =>
            {
                grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
                grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(PlayerId.None);
            });

        Click(g, 2);
        Click(g, 3);

        Assert.Equal(0, g.State.Stats.For(Red).VikingKills);
    }

    // --- Replay + persistence ----------------------------------------------

    [Fact]
    public void Replay_DoesNotDoubleCountRunStats()
    {
        // BeginReplay zeroes the counters at rewind, then playback
        // re-executes the recorded beats — so after an (instant) replay the
        // counters mirror the recorded game rather than accumulating twice.
        var g = BuildGame(setup: grid =>
        {
            grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
            grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(Blue);
        });
        Click(g, 2);
        Click(g, 3);
        Assert.Equal(1, g.State.Stats.For(Blue).UnitsLost);

        g.Controller.BeginReplay();

        Assert.Equal(1, g.State.Stats.For(Blue).UnitsLost);
    }

    [Fact]
    public void SaveRoundTrip_PreservesRunStats()
    {
        var redP = new Player("Red", PlayerId.FromIndex(0));
        var blueP = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { redP, blueP };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 1, Blue);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        PlayerRunStats redStats = state.Stats.For(Red);
        redStats.UnitsLost = 2;
        redStats.TowersBuilt = 1;
        redStats.VikingKills = 3;
        redStats.MaxUnitLevelFielded = 4;

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);
        GameState loaded = SaveSerializer.Deserialize(json).State;

        Assert.Equal(2, loaded.Stats.For(Red).UnitsLost);
        Assert.Equal(1, loaded.Stats.For(Red).TowersBuilt);
        Assert.Equal(3, loaded.Stats.For(Red).VikingKills);
        Assert.Equal(4, loaded.Stats.For(Red).MaxUnitLevelFielded);
        Assert.Equal(0, loaded.Stats.For(Blue).UnitsLost);
    }

    [Fact]
    public void SaveWithAllZeroStats_OmitsTheField()
    {
        // Wire-format stability: a game where nothing happened serializes
        // byte-identically to the pre-stats format.
        var redP = new Player("Red", PlayerId.FromIndex(0));
        var blueP = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { redP, blueP };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 1, Blue);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);

        Assert.DoesNotContain("RunStats", json);
        GameState loaded = SaveSerializer.Deserialize(json).State;
        Assert.Equal(0, loaded.Stats.For(Red).UnitsLost);
    }
}
