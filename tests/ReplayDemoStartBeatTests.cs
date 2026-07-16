// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="ReplayDemoStartBeat"/> — the authored
/// tutorial-only marker that makes paced replay playback fast-forward:
/// every beat before the marker executes instantly and silently (the
/// author's staging — banking gold, placing opposing pieces), and paced
/// playback begins at the marker. Looping re-runs the fast-forward, so
/// each Instructions loop restarts at the marked beat, not at turn 1.
/// </summary>
public class ReplayDemoStartBeatTests
{
    [Fact]
    public void DemoStartBeat_RoundTripsThroughSerializer()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players, 0, 0), new Treasury());
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);

        var beats = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            new ReplayDemoStartBeat { Index = 1, Turn = 2, Actor = -1 },
        };
        var tutorial = new Tutorial
        {
            Title = "T",
            Replay = new Replay(snapshot, 1, 0, beats),
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 5, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        var marker = Assert.IsType<ReplayDemoStartBeat>(
            loaded.Tutorial!.Replay!.Beats[1]);
        Assert.Equal(-1, marker.Actor);
        Assert.Equal(2, marker.Turn);
    }

    [Fact]
    public void Replay_FastForwardsToDemoStart_ThenPacesAndLoops()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(aiPacer: pacer);
        pacer.DrainAll();

        // Setup phase: Red buys a recruit and both players pass a turn.
        // Then the marker, then the demo proper: the recruit captures.
        HexCoord capital = h.State.Territories
            .First(t => t.Owner == h.Players[0].Id).Capital!.Value;
        HexCoord from = HexCoord.FromOffset(0, 1).Equals(capital)
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);
        HexCoord to = HexCoord.FromOffset(2, 1);

        h.Map.SimulateClick(h.State.Grid.Get(capital)!);
        h.Hud.ClickBuyRecruit();
        h.Map.SimulateClick(h.State.Grid.Get(from)!);
        h.Hud.ClickEndTurn();
        pacer.DrainAll();
        h.Hud.ClickEndTurn();   // Blue passes; back to Red, turn 2.
        pacer.DrainAll();
        h.Controller.RecordTutorialOnlyBeat(new ReplayDemoStartBeat());
        h.Map.SimulateClick(h.State.Grid.Get(from)!);
        h.Map.SimulateClick(h.State.Grid.Get(to)!);
        pacer.DrainAll();

        // [Buy, EndTurn, EndTurn, DemoStart, Move]
        Assert.Equal(5, h.Controller.ReplayBeats.Count);

        int ended = 0;
        h.Controller.ReplayEnded += () => ended++;

        h.Controller.BeginReplay();

        // The whole setup already applied — before any pacer step, the
        // recruit is back on its tile and it's Red's turn 2 again.
        Assert.NotNull(h.State.Grid.Get(from)!.Unit);
        Assert.Equal(2, h.State.Turns.TurnNumber);

        // First paced beat is the post-marker Move: its preview shows
        // the pickup pulse immediately.
        pacer.StepOne();
        Assert.Equal(from, h.Map.LastMoveSource);
        pacer.DrainAll();
        Assert.Equal(1, ended);
        Assert.NotNull(h.State.Grid.Get(to)!.Unit);   // capture landed

        // Loop: the second run fast-forwards through setup again.
        h.Controller.BeginReplay();
        Assert.NotNull(h.State.Grid.Get(from)!.Unit); // rewound + re-setup
        Assert.Null(h.State.Grid.Get(to)!.Unit);
        Assert.Equal(2, h.State.Turns.TurnNumber);
        pacer.StepOne();
        Assert.Equal(from, h.Map.LastMoveSource);
        pacer.DrainAll();
        Assert.Equal(2, ended);
    }
}
