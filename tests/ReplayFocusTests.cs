// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class ReplayFocusTests
{
    private static readonly HexCoord A = new HexCoord(2, 3);
    private static readonly HexCoord B = new HexCoord(5, 1);

    public static IEnumerable<object[]> BeatsWithFocus() => new[]
    {
        new object[] { new ReplayMoveBeat { From = A, To = B }, B },
        new object[] { new ReplayRejectedMoveBeat { From = A, To = B }, B },
        new object[] { new ReplayBuyBeat { Capital = A, To = B }, B },
        new object[] { new ReplayBuildTowerBeat { Capital = A, To = B }, B },
        new object[] { new ReplayLongPressRallyBeat { Target = A }, A },
        new object[] { new ReplayVikingMoveBeat { From = A, To = B }, B },
        new object[] { new ReplayVikingDisembarkBeat { Sea = A, Land = B }, B },
        new object[] { new ReplayVikingPerishBeat { Sea = A }, A },
        new object[] { new ReplaySelectTerritoryBeat { Anchor = A }, A },
    };

    [Theory]
    [MemberData(nameof(BeatsWithFocus))]
    public void FocusCoord_LocationBeats_ReturnTheEffectTile(ReplayBeat beat, HexCoord expected)
    {
        Assert.Equal(expected, ReplayFocus.FocusCoord(beat));
    }

    [Fact]
    public void FocusCoord_NoLocationBeats_ReturnNull()
    {
        Assert.Null(ReplayFocus.FocusCoord(new ReplayEndTurnBeat()));
        Assert.Null(ReplayFocus.FocusCoord(new ReplayClaimVictoryBeat()));
        Assert.Null(ReplayFocus.FocusCoord(new ReplayDismissClaimBeat()));
        Assert.Null(ReplayFocus.FocusCoord(new ReplayDismissDefeatBeat()));
        Assert.Null(ReplayFocus.FocusCoord(new ReplayVikingTurnEndBeat()));
        Assert.Null(ReplayFocus.FocusCoord(new ReplayDemoStartBeat()));
    }

    [Fact]
    public void ReplayBeatPreviewing_FiresPerBeat_InRecordedOrder()
    {
        // Mini record → replay round trip (the ReplayFidelityTests shape):
        // an all-AI game produces a beat log; a fresh replay-only
        // controller plays it back on the paced track (SynchronousAiPacer
        // drains inline) and must preview every beat in log order.
        var players = new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer),
            new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer),
        };
        MapGenResult mapGen = MapGenerator.BuildInitialGrid(10, 8, players, seed: 777);
        IReadOnlyList<Territory> territories = CapitalReconciler.Reconcile(
            TerritoryFinder.FindAll(mapGen.Grid), new List<Territory>(), mapGen.Grid);
        var liveState = new GameState(mapGen.Grid, territories, players,
            new TurnState(players), new Treasury(), mapGen.WaterCoords);
        var liveController = new GameController(liveState, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            seed: 777,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: 6);
        liveController.StartGame();
        Assert.True(liveController.ReplayBeats.Count > 0);

        var replay = new Replay(
            liveController.InitialReplaySnapshot!,
            liveController.InitialReplayTurnNumber,
            liveController.InitialReplayCurrentPlayerIndex,
            liveController.ReplayBeats);
        var replayController = new GameController(liveState, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            seed: 777,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: 6,
            loadedReplay: replay);
        var previewed = new List<ReplayBeat>();
        replayController.ReplayBeatPreviewing += previewed.Add;

        replayController.BeginReplay();

        Assert.Equal(replay.Beats, previewed);
    }
}
