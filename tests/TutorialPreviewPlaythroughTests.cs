// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Playability gate for the SHIPPED tutorial in Preview mode — the mode a
/// player actually experiences via Play Tutorial (
/// <c>PlayTutorialScene</c> → <c>PreviewPane</c>), as opposed to the
/// hands-free replay drain in <see cref="TutorialReplayFidelityTests"/>.
///
/// <para>
/// The test drives the human side as real UI input (tile clicks, Buy /
/// Build Tower / End Turn presses, narration taps) taking each action
/// from the recording, while the opponents run through the standard AI
/// step machine off the same script. If any beat can't be performed — the
/// script cursor parking on a beat nobody consumes, an input the
/// validator rejects — the loop runs out of actionable work and fails
/// with the stalled cursor's context, instead of the player discovering
/// a soft-lock mid-tutorial.
/// </para>
/// </summary>
public class TutorialPreviewPlaythroughTests
{
    [Fact]
    public void ShippedTutorial_PlaysThroughInPreview_WithoutStalling()
    {
        string json = File.ReadAllText(
            Path.Combine(TutorialsDir(), "full_tutorial.json"));
        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.NotNull(loaded.Tutorial);
        Tutorial tutorial = loaded.Tutorial!;
        IReadOnlyList<ReplayBeat> script = tutorial.Replay.Beats;

        // Mirrors PlayTutorialScene: trim the save's roster to the colors
        // that own land, then PreviewPane's kinds-by-position (0 Human,
        // rest Computer). The trim is what makes the recording's
        // neutral-seat actor index differ from the live one.
        List<Player> owning = MapRosterRules.ActivePlayersForTerritories(
            loaded.State.Players, loaded.State.Territories);
        var roster = new List<Player>(owning.Count);
        for (int i = 0; i < owning.Count; i++)
        {
            roster.Add(new Player(owning[i].Name, owning[i].Id,
                i == 0 ? PlayerKind.Human : PlayerKind.Computer));
        }
        Assert.True(roster.Count < loaded.State.Players.Count,
            "fixture expects the play roster to be narrower than the recording's");

        var state = new GameState(
            loaded.State.Grid, loaded.State.Territories, roster,
            new TurnState(roster), loaded.State.Treasury,
            waterCoords: loaded.State.WaterCoords, mode: loaded.State.Mode);

        var hud = new MockHudView();
        var map = new MockHexMapView();
        // Queued, not synchronous: the real game's pacer defers every AI
        // beat to a later frame, so the input handler's own closing
        // RefreshViews lands FIRST. Draining inline instead would let an
        // opponent's turn run before the narration it sits behind is ever
        // presented — a pacing artifact the player never sees.
        var pacer = new QueuedAiPacer();
        var cursor = new ScriptCursor();
        var replayAi = new ReplayDrivenAi(script, roster, cursor);
        var preview = new TutorialPreview(script, state, cursor);
        var rejections = new List<string>();
        preview.PlayerActionRejected += (_, reason) => rejections.Add(reason);
        var session = new SessionState();

        TutorialNarrationDriver? narrationRef = null;
        TutorialPreviewCues? cuesRef = null;
        var controller = new GameController(
            state, session, map, hud,
            seed: loaded.MasterSeed,
            aiChooser: (s, c, v, ru, r) => preview.IsComplete
                ? null
                : replayAi.ChooseNextAction(s, c, v, r),
            aiPacer: pacer,
            maxTurnNumber: loaded.MaxTurnNumber,
            humanActionValidator: preview.TryAccept,
            buyLevelValidator: preview.AllowBuyLevel,
            previewMode: true,
            loadedReplay: tutorial.Replay,
            isReplayPaused: () => narrationRef?.IsPresenting == true,
            autoSelectFirstTerritory: false,
            onAfterRefresh: () =>
            {
                replayAi.ConsumeElapsedNeutralSeatBeats(state);
                narrationRef?.Tick();
                cuesRef?.Apply();
            });

        var narration = new TutorialNarrationDriver(
            script, cursor, hud,
            () =>
            {
                controller.RefreshViewsForTutorial();
                controller.ResumeAiTurnsAfterReplayPause();
            });
        narrationRef = narration;
        var cues = new TutorialPreviewCues(
            preview, state, session, hud, map, roster[0].Id,
            t => controller.SelectTerritoryForTutorial(t),
            () => controller.CancelActionForTutorial());
        cues.SetNarrationDriver(narration);
        cuesRef = cues;

        PreviewSetup.Apply(map, state, tutorial);
        controller.StartGame();

        // Play the script out as a human would. Each pass does exactly one
        // thing: acknowledge narration, or perform the next player-0 beat
        // (the opponents' queued beats drain first). A pass with
        // nothing to do is the soft-lock this gate exists to catch.
        int played = 0;
        for (int pass = 0; !preview.IsComplete && pass < script.Count * 8; pass++)
        {
            if (pacer.HasPending)
            {
                // Let the opponents' scheduled beats land, as the frame
                // timer would between the player's clicks.
                pacer.DrainAll();
                continue;
            }
            if (hud.TutorialMessageTappable)
            {
                hud.RaiseTutorialMessageTapped();
                continue;
            }
            ReplayBeat? next = preview.NextPlayer0Beat;
            if (next == null) break;
            PerformAsHuman(next, hud, map, state);
            played++;
        }

        Assert.True(preview.IsComplete, Diagnose(script, cursor, state, rejections, played));
        Assert.Empty(rejections);
    }

    /// <summary>Drive one recorded player-0 beat through the same view
    /// events the real HUD / map raise.</summary>
    private static void PerformAsHuman(
        ReplayBeat beat, MockHudView hud, MockHexMapView map, GameState state)
    {
        switch (beat)
        {
            case ReplayEndTurnBeat _:
                hud.ClickEndTurn();
                break;
            case ReplayBuyBeat bu:
                hud.ClickBuyUnit(bu.Level);
                map.SimulateClick(state.Grid.Get(bu.To));
                break;
            case ReplayBuildTowerBeat bt:
                hud.ClickBuildTower();
                map.SimulateClick(state.Grid.Get(bt.To));
                break;
            case ReplayMoveBeat mv:
                map.SimulateClick(state.Grid.Get(mv.From));
                map.SimulateClick(state.Grid.Get(mv.To));
                break;
            case ReplayClaimVictoryBeat _:
                hud.ClickClaimVictoryWinNow();
                break;
            case ReplayDismissClaimBeat _:
                hud.ClickClaimVictoryContinue();
                break;
            case ReplayDismissDefeatBeat _:
                hud.ClickDefeatContinue();
                break;
            default:
                throw new InvalidOperationException(
                    $"Playthrough can't drive beat kind {beat.GetType().Name} as human input.");
        }
    }

    /// <summary>Failure text that names where the script parked and who
    /// owned the beat nobody consumed.</summary>
    private static string Diagnose(
        IReadOnlyList<ReplayBeat> script, ScriptCursor cursor, GameState state,
        IReadOnlyList<string> rejections, int played)
    {
        var sb = new StringBuilder();
        sb.Append($"tutorial stalled after {played} player actions: cursor ")
          .Append($"{cursor.Index}/{script.Count}, turn {state.Turns.TurnNumber}, ")
          .Append($"currentPlayerIndex {state.Turns.CurrentPlayerIndex} ")
          .Append($"(neutralSeat={state.Turns.IsNeutralSeat}, roster {state.Players.Count})");
        if (cursor.Index < script.Count)
        {
            ReplayBeat parked = script[cursor.Index];
            sb.Append($"; parked on #{parked.Index} {parked.GetType().Name} ")
              .Append($"actor {parked.Actor} turn {parked.Turn}");
        }
        foreach (string r in rejections) sb.Append($"; rejected: {r}");
        return sb.ToString();
    }

    /// <summary>The repo's tutorials/ directory, located by walking up
    /// from the test assembly (bin/Debug/net8.0/...).</summary>
    private static string TutorialsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "tutorials");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the repo tutorials/ directory above " +
            AppContext.BaseDirectory);
    }
}
