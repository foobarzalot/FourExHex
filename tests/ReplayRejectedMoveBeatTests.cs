// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="ReplayRejectedMoveBeat"/> — the tutorial-only
/// beat auto-recorded (recording mode only) when a human's unit-move
/// attempt is rejected, so authored Instructions demos can replay the
/// rejection flash ("a Recruit can't take a defended tile").
/// </summary>
public class ReplayRejectedMoveBeatTests
{
    private const int TowerCol = 3, TowerRow = 1;   // blue tower defending...
    private const int TargetCol = 2, TargetRow = 1; // ...the border tile red attacks

    /// <summary>5x2 fixture with a blue tower guarding the tile next to
    /// red's territory, so a recruit's capture attempt there is rejected.</summary>
    private static ControllerHarness BuildDefendedFixture(QueuedAiPacer pacer, bool recordingMode)
        => TestHelpers.BuildControllerGame(
            aiPacer: pacer,
            recordingMode: recordingMode,
            beforeStart: state =>
                state.Grid.Get(HexCoord.FromOffset(TowerCol, TowerRow))!.Occupant = new Tower());

    /// <summary>Buy a recruit on red's non-capital tile, pick it up, and
    /// click the defended tile — a rejected capture attempt.</summary>
    private static HexCoord DriveRejectedAttempt(ControllerHarness h, QueuedAiPacer pacer)
    {
        HexCoord capital = h.State.Territories
            .First(t => t.Owner == h.Players[0].Id).Capital!.Value;
        HexCoord from = HexCoord.FromOffset(0, 1).Equals(capital)
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);

        h.Map.SimulateClick(h.State.Grid.Get(capital)!);
        h.Hud.ClickBuyRecruit();
        h.Map.SimulateClick(h.State.Grid.Get(from)!);
        h.Map.SimulateClick(h.State.Grid.Get(from)!);   // pick the unit up
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(TargetCol, TargetRow))!);
        pacer.DrainAll();
        return from;
    }

    [Fact]
    public void RejectedMove_AutoRecordedInRecordingMode()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = BuildDefendedFixture(pacer, recordingMode: true);
        pacer.DrainAll();

        HexCoord from = DriveRejectedAttempt(h, pacer);

        ReplayBeat last = h.Controller.ReplayBeats[^1];
        var rejected = Assert.IsType<ReplayRejectedMoveBeat>(last);
        Assert.Equal(from, rejected.From);
        Assert.Equal(HexCoord.FromOffset(TargetCol, TargetRow), rejected.To);
        Assert.Equal(-1, rejected.Actor);
    }

    [Fact]
    public void RejectedMove_NotRecordedInNormalPlay()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = BuildDefendedFixture(pacer, recordingMode: false);
        pacer.DrainAll();

        DriveRejectedAttempt(h, pacer);

        Assert.DoesNotContain(h.Controller.ReplayBeats,
            b => b is ReplayRejectedMoveBeat);
    }

    [Fact]
    public void RejectedMove_RoundTripsThroughSerializer()
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
            new ReplayRejectedMoveBeat
            {
                Index = 0, Turn = 1, Actor = -1,
                From = HexCoord.FromOffset(0, 1),
                To = HexCoord.FromOffset(1, 0),
            },
        };
        var tutorial = new Tutorial
        {
            Title = "T",
            Replay = new Replay(snapshot, 1, 0, beats),
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 3, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        var rejected = Assert.IsType<ReplayRejectedMoveBeat>(
            loaded.Tutorial!.Replay!.Beats[0]);
        Assert.Equal(HexCoord.FromOffset(0, 1), rejected.From);
        Assert.Equal(HexCoord.FromOffset(1, 0), rejected.To);
    }

    [Fact]
    public void Replay_RejectedMoveBeat_ShowsPickupThenFlashesRejection()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = BuildDefendedFixture(pacer, recordingMode: true);
        pacer.DrainAll();

        HexCoord from = DriveRejectedAttempt(h, pacer);
        HexCoord target = HexCoord.FromOffset(TargetCol, TargetRow);
        Assert.Equal(2, h.Controller.ReplayBeats.Count);   // [Buy, RejectedMove]

        h.Map.Rejections.Clear();
        h.Controller.BeginReplay();

        pacer.StepOne();                                   // preview: Buy
        pacer.StepOne();                                   // execute: Buy
        pacer.StepOne();                                   // preview: RejectedMove
        Assert.Equal(from, h.Map.LastMoveSource);          // pickup pulse first
        Assert.Empty(h.Map.Rejections);                    // no flash yet
        pacer.StepOne();                                   // execute: RejectedMove
        Assert.NotNull(h.Map.LastRejection);
        Assert.Equal(target, h.Map.LastRejection!.Value.Target);
        Assert.NotEmpty(h.Map.LastRejection.Value.Defenders);  // defended rejection
        Assert.Null(h.Map.LastMoveSource);                 // pickup cleared
        // The rejected move mutates nothing: the recruit is still home.
        Assert.NotNull(h.State.Grid.Get(from)!.Unit);
        Assert.Null(h.State.Grid.Get(target)!.Unit);
    }
}
