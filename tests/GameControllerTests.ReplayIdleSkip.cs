// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Demo/Instructions idle-turn fast-forward ------------------------

    // Record a short hot-seat session with one real action and three
    // idle turns: Red builds a tower and ends, Blue and Green each end
    // with no action, Red ends again with no action. Every slot stays
    // under the 50% claim-victory threshold (8/8/2 of 18 tiles) so no
    // overlay interrupts the EndTurn clicks. Beat log:
    //   [BuildTower, EndTurn(Red), EndTurn(Blue), EndTurn(Green)]
    private static (Replay Replay, GameState State) RecordTowerThenIdleTurns()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(9, 2, PlayerId.FromIndex(2));
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 4; col++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = PlayerId.FromIndex(0);
            for (int col = 4; col < 8; col++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = PlayerId.FromIndex(1);
        }
        var players = new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human),
            new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human),
            new Player("Green", PlayerId.FromIndex(2), PlayerKind.Human),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players), new Treasury());
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var session = new SessionState();
        var controller = new GameController(state, session,
            map, hud, aiPacer: new SynchronousAiPacer());
        controller.StartGame();

        // Red: build a tower on an owned empty tile, then end the turn.
        map.SimulateClick(state.Grid.Get(HexCoord.FromOffset(0, 0))!);
        HexCoord cap = session.SelectedTerritory!.Capital!.Value;
        state.Treasury.SetGold(cap, 30);
        HexCoord towerCoord = default;
        foreach ((int c, int r) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            HexCoord coord = HexCoord.FromOffset(c, r);
            if (coord != cap && state.Grid.Get(coord)!.Occupant == null)
            {
                towerCoord = coord;
                break;
            }
        }
        hud.ClickBuildTower();
        map.SimulateClick(state.Grid.Get(towerCoord)!);
        hud.ClickEndTurn();        // Red's acted turn ends
        hud.ClickEndTurn();        // Blue: idle turn
        hud.ClickEndTurn();        // Green: idle turn
        Assert.Equal(
            new[] { typeof(ReplayBuildTowerBeat), typeof(ReplayEndTurnBeat),
                    typeof(ReplayEndTurnBeat), typeof(ReplayEndTurnBeat) },
            controller.ReplayBeats.Select(b => b.GetType()));

        var replay = new Replay(
            controller.InitialReplaySnapshot!,
            controller.InitialReplayTurnNumber,
            controller.InitialReplayCurrentPlayerIndex,
            controller.ReplayBeats);
        return (replay, state);
    }

    private static List<ReplayBeat> PreviewedDuringReplay(
        Replay replay, GameState state, bool fastForwardIdleTurns)
    {
        var controller = new GameController(state, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            aiPacer: new SynchronousAiPacer(),
            loadedReplay: replay,
            previewMode: true,
            replayFastForwardsIdleTurns: fastForwardIdleTurns);
        var previewed = new List<ReplayBeat>();
        controller.ReplayBeatPreviewing += previewed.Add;
        controller.BeginReplay();
        return previewed;
    }

    [Fact]
    public void Replay_FastForwardIdleTurns_SkipsPreviewOfEveryTurnEnd()
    {
        (Replay replay, GameState state) = RecordTowerThenIdleTurns();

        List<ReplayBeat> previewed = PreviewedDuringReplay(
            replay, state, fastForwardIdleTurns: true);

        // Turn-end beats are pure transition — in demo playback they play
        // with no preview at all, so only the action beats are previewed
        // and idle turns flick past.
        Assert.Equal(
            new[] { typeof(ReplayBuildTowerBeat) },
            previewed.Select(b => b.GetType()));
    }

    [Fact]
    public void Replay_WithoutFastForward_PreviewsEveryBeat()
    {
        (Replay replay, GameState state) = RecordTowerThenIdleTurns();

        List<ReplayBeat> previewed = PreviewedDuringReplay(
            replay, state, fastForwardIdleTurns: false);

        Assert.Equal(replay.Beats.Count, previewed.Count);
    }
}
