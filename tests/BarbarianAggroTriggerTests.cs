// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The live and simulated seams that flip barbarians aggro: a capture
/// compromising their territory (with undo restoring the passive state),
/// sim ↔ live parity of that flip, viking disembarks spawning aggro (and
/// spreading it on a neutral landing), and the Rising Tides cornered
/// check at the neutral seat's turn start.
/// </summary>
public class BarbarianAggroTriggerTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameOperations BuildOps(GameState state) =>
        new GameOperations(
            state,
            new SessionState(),
            new MockHexMapView(),
            new MockHudView(),
            recordingMode: false,
            previewMode: false,
            isReplayMode: () => false,
            aiSilentMode: () => false,
            isReplayInstantActive: () => false,
            clearUndoAndReplayBookkeeping: () => { },
            onGameEnded: () => { },
            onHumanTurnStarted: () => { },
            maxTurnNumber: 100,
            masterSeed: 1,
            onAfterRefresh: null);

    // --- capture compromise (human path + undo) ---------------------------

    [Fact]
    public void HumanCaptureIntoBarbarianTerritory_FlipsAggro_AndUndoRestores()
    {
        // 5x2 board: Red at (0,1)/(1,1), a neutral strip (2,1)-(4,1) with a
        // passive barbarian at (4,1), Blue on the top row. Red buys a
        // Recruit onto (2,1) — capturing barbarian ground — which must flip
        // the strip's units aggro; undoing the capture must restore them.
        ControllerHarness h = TestHelpers.BuildControllerGame(
            ownerOverrides: new[]
            {
                (0, 1, Red),
                (1, 1, Red),
                (2, 1, PlayerId.None),
                (3, 1, PlayerId.None),
                (4, 1, PlayerId.None),
            },
            beforeTerritories: grid =>
                grid.Get(HexCoord.FromOffset(4, 1))!.Occupant =
                    new Unit(PlayerId.None, UnitLevel.Recruit));

        Unit barbarian = h.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit!;
        Assert.False(barbarian.IsAggro);

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(0, 1))!);
        h.Hud.ClickBuyRecruit();
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(2, 1))!);

        Assert.Equal(Red, h.State.Grid.Get(HexCoord.FromOffset(2, 1))!.Owner);
        Assert.True(h.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit!.IsAggro);

        h.Hud.ClickUndoLast();

        Assert.Equal(PlayerId.None, h.State.Grid.Get(HexCoord.FromOffset(2, 1))!.Owner);
        Assert.False(h.State.Grid.Get(HexCoord.FromOffset(4, 1))!.Unit!.IsAggro);
    }

    // --- sim ↔ live parity ------------------------------------------------

    [Fact]
    public void CompromiseCapture_SimulatesIdenticallyToLive()
    {
        // Red Soldier captures the empty half of a barbarian territory.
        // The 1-ply simulator must produce the same post-flip state as the
        // real execute path, or AI lookahead scores a different world.
        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(4, 1, Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red, UnitLevel.Soldier);
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(PlayerId.None, UnitLevel.Recruit);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury());

        var action = new AiMoveAction(
            HexCoord.FromOffset(1, 0), HexCoord.FromOffset(2, 0));

        GameState sim = AiSimulator.Clone(state);
        AiSimulator.Apply(action, sim);

        GameState real = AiSimulator.Clone(state);
        BuildOps(real).ExecuteAiMove(action.Source, action.Destination);

        // The flip actually happened (guards the trigger)…
        Assert.True(real.Grid.Get(HexCoord.FromOffset(3, 0))!.Unit!.IsAggro);
        // …and both worlds agree byte-for-byte (guards the mirror).
        Assert.Equal(GameStateChecksum.Stringify(sim), GameStateChecksum.Stringify(real));
    }

    // --- disembark --------------------------------------------------------

    private static GameState BuildDisembarkState(
        out HexCoord sea, Action<HexGrid>? mutate = null)
    {
        var players = new List<Player>
        {
            new Player("Red", Red),
            new Player("Blue", Blue),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, Red);
        mutate?.Invoke(grid);
        sea = HexCoord.FromOffset(3, 1);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(
            grid, territories, players, new TurnState(players, 0, 4), new Treasury(),
            waterCoords: new HashSet<HexCoord> { sea },
            mode: GameMode.VikingRaiders);
    }

    [Fact]
    public void VikingDisembark_SpawnsAggroUnit()
    {
        GameState state = BuildDisembarkState(out HexCoord sea);
        state.Vikings.AddAtSea(new SeaViking(sea, UnitLevel.Captain));
        state.Vikings.LastSpawnRound = 3;
        HexCoord land = VikingRaidersRules
            .DisembarkTargets(state, sea, UnitLevel.Captain)[0];

        BuildOps(state).ExecuteVikingDisembark(sea, land);

        Unit raider = state.Grid.Get(land)!.Unit!;
        Assert.True(raider.IsAggro);
    }

    [Fact]
    public void VikingDisembark_NeutralLanding_SpreadsAggroToResidents()
    {
        // The landing tile (2,1) is an empty part of a passive barbarian
        // territory; the raider joining it flips the resident at (2,2).
        GameState state = BuildDisembarkState(out HexCoord sea, grid =>
        {
            grid.Get(HexCoord.FromOffset(2, 1))!.Owner = PlayerId.None;
            grid.Get(HexCoord.FromOffset(2, 2))!.Owner = PlayerId.None;
            grid.Get(HexCoord.FromOffset(2, 2))!.Occupant =
                new Unit(PlayerId.None, UnitLevel.Recruit);
        });
        state.Vikings.AddAtSea(new SeaViking(sea, UnitLevel.Recruit));
        state.Vikings.LastSpawnRound = 3;
        HexCoord land = HexCoord.FromOffset(2, 1);
        Assert.Contains(
            land, VikingRaidersRules.DisembarkTargets(state, sea, UnitLevel.Recruit));

        BuildOps(state).ExecuteVikingDisembark(sea, land);

        Assert.True(state.Grid.Get(land)!.Unit!.IsAggro);
        Assert.True(state.Grid.Get(HexCoord.FromOffset(2, 2))!.Unit!.IsAggro);
    }

    // --- Rising Tides cornered check --------------------------------------

    private static GameState BuildTideState(int neutralCols, int redCols)
    {
        var players = new List<Player>
        {
            new Player("Red", Red),
            new Player("Blue", Blue),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(neutralCols + redCols, 1, Red);
        for (int col = 0; col < neutralCols; col++)
            grid.Get(HexCoord.FromOffset(col, 0))!.Owner = PlayerId.None;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant =
            new Unit(PlayerId.None, UnitLevel.Recruit);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        // The NEUTRAL seat's turn (index == players.Count): the tide only
        // ever targets the current seat's tiles.
        return new GameState(
            grid, territories, players,
            new TurnState(players, players.Count, 3), new Treasury(),
            mode: GameMode.RisingTides);
    }

    [Fact]
    public void TideCorneredBarbarian_AggrosAtNeutralTurnStart()
    {
        // Single-tile neutral territory: the forecast dooms the barbarian's
        // tile and there is no in-territory escape — cornered, so the
        // territory aggros at the seat's turn start.
        GameState state = BuildTideState(neutralCols: 1, redCols: 2);

        BuildOps(state).StartPlayerTurn();

        Assert.Contains(state.PendingTide, step => step.Coord == HexCoord.FromOffset(0, 0));
        Assert.True(state.Grid.Get(HexCoord.FromOffset(0, 0))!.Unit!.IsAggro);
    }

    [Fact]
    public void TideDoomedBarbarian_WithEscape_StaysPassive()
    {
        // Two-tile neutral territory: the exposed end (0,0) is doomed but
        // (1,0) is an open retreat — not cornered, no aggro (the wander
        // beat will retreat instead).
        GameState state = BuildTideState(neutralCols: 2, redCols: 2);

        BuildOps(state).StartPlayerTurn();

        Assert.Contains(state.PendingTide, step => step.Coord == HexCoord.FromOffset(0, 0));
        Assert.False(state.Grid.Get(HexCoord.FromOffset(0, 0))!.Unit!.IsAggro);
    }
}
