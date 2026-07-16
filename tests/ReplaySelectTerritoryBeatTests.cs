// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="ReplaySelectTerritoryBeat"/> — the authored,
/// tutorial-only beat that selects/highlights a territory during
/// hands-free replay playback (the Instructions demo animations).
/// Covers serializer round-trip, Record-mode stamping, and playback
/// through the paced replay step machine.
/// </summary>
public class ReplaySelectTerritoryBeatTests
{
    // --- Serialization ------------------------------------------------

    private static (GameState, IReadOnlyList<Player>) BuildMinimalState()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turnState = new TurnState(players, currentPlayerIndex: 0, turnNumber: 0);
        var state = new GameState(grid, territories, players, turnState, new Treasury());
        return (state, players);
    }

    [Fact]
    public void SelectBeat_RoundTripsAnchorAndActorMinusOne()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);

        var beats = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            new ReplaySelectTerritoryBeat
            {
                Index = 1, Turn = 1, Actor = -1,
                Anchor = HexCoord.FromOffset(1, 1),
            },
        };
        var tutorial = new Tutorial
        {
            Title = "T",
            Replay = new Replay(snapshot, 1, 0, beats),
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        var select = Assert.IsType<ReplaySelectTerritoryBeat>(
            loaded.Tutorial!.Replay!.Beats[1]);
        Assert.Equal(HexCoord.FromOffset(1, 1), select.Anchor);
        Assert.Equal(-1, select.Actor);
        Assert.Equal(1, select.Index);
        Assert.Equal(1, select.Turn);
    }

    // --- Recording ------------------------------------------------------

    [Fact]
    public void RecordTutorialOnlyBeat_StampsSelectBeat()
    {
        ControllerHarness h = TestHelpers.BuildControllerGame();

        h.Controller.RecordTutorialOnlyBeat(new ReplaySelectTerritoryBeat
        {
            Anchor = HexCoord.FromOffset(0, 1),
        });

        ReplayBeat last = h.Controller.ReplayBeats[^1];
        var select = Assert.IsType<ReplaySelectTerritoryBeat>(last);
        Assert.Equal(HexCoord.FromOffset(0, 1), select.Anchor);
        Assert.Equal(-1, select.Actor);
        Assert.Equal(h.State.Turns.TurnNumber, select.Turn);
        Assert.Equal(h.Controller.ReplayBeats.Count - 1, select.Index);
    }

    // --- Playback -------------------------------------------------------

    [Fact]
    public void Replay_SelectBeat_HighlightsAndSelectsTerritory()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(aiPacer: pacer);
        pacer.DrainAll();

        // Author a select beat for the red territory (anchor on a
        // non-capital tile — any tile of the territory must resolve),
        // then a real EndTurn so the log is a valid playable script.
        HexCoord anchor = HexCoord.FromOffset(1, 1);
        PlayerId red = h.Players[0].Id;
        h.Controller.RecordTutorialOnlyBeat(new ReplaySelectTerritoryBeat { Anchor = anchor });
        h.Hud.ClickEndTurn();
        pacer.DrainAll();

        h.Controller.BeginReplay();

        // Step 1: the select beat's PREVIEW must already highlight the
        // target territory (no flicker between consecutive selects), and
        // its execute must be scheduled on the longer action delay so
        // authored selection reads slowly.
        pacer.StepOne();
        Assert.NotNull(h.Map.LastHighlight);
        Assert.True(h.Map.LastHighlight!.Contains(anchor));
        Assert.Equal(red, h.Map.LastHighlight.Owner);
        Assert.Equal(StepPacing.AiActionDelayMs, pacer.ScheduledDelaysMs[^1]);

        // Step 2: EXECUTE — the session selection anchors to the
        // territory, exactly like a live selection click.
        pacer.StepOne();
        Assert.NotNull(h.Session.SelectedTerritory);
        Assert.True(h.Session.SelectedTerritory!.Contains(anchor));

        // Run playback to completion: EndReplay clears the highlight.
        pacer.DrainAll();
        Assert.Null(h.Map.LastHighlight);
    }
}
