// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Round-boundary regression for Tutorial Preview: the neutral seat is a
/// real turn in the rotation, so a recorded tutorial carries one
/// <see cref="ReplayEndTurnBeat"/> per neutral turn (stamped with the
/// RECORDING roster's seat index, which need not match the play-time
/// roster). The neutral seat's live turn is driven by
/// <c>VikingAi</c>, never by the injected chooser, so
/// <see cref="ReplayDrivenAi"/> never gets asked for its beat — without
/// an explicit consumer the shared cursor parks on it forever, every
/// player-0 input is rejected as a cursor desync, and the tutorial
/// soft-locks at the start of the player's second turn.
/// </summary>
public class TutorialPreviewNeutralSeatTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);
    private static readonly PlayerId Green = PlayerId.FromIndex(2);

    /// <summary>The neutral seat index as stamped by a 6-slot recording
    /// roster (the tutorial builder's). Play-time rosters are trimmed to
    /// the colors that own land, so this deliberately does NOT equal the
    /// live seat index.</summary>
    private const int RecordedNeutralActor = 6;

    /// <summary>
    /// PreviewPane's wiring, minus Godot: a scripted chooser for the
    /// opponents, the human-side validator, the narration driver, and the
    /// cues, all sharing one cursor.
    /// </summary>
    private sealed class PreviewHarness
    {
        public GameState State { get; }
        public MockHudView Hud { get; } = new();
        public MockHexMapView Map { get; } = new();
        public TutorialPreview Preview { get; }
        public ReplayDrivenAi ReplayAi { get; }
        public GameController Controller { get; }

        public PreviewHarness(IReadOnlyList<ReplayBeat> script)
        {
            var roster = new List<Player>
            {
                new("Red", Red, PlayerKind.Human),
                new("Blue", Blue, PlayerKind.Computer),
                new("Green", Green, PlayerKind.Computer),
            };
            var grid = new HexGrid();
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    PlayerId owner = c < 2 ? Red : c < 4 ? Blue : Green;
                    grid.Add(new HexTile(HexCoord.FromOffset(c, r), owner));
                }
            }
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, roster,
                new TurnState(roster), new Treasury());

            var cursor = new ScriptCursor();
            ReplayAi = new ReplayDrivenAi(script, roster, cursor);
            Preview = new TutorialPreview(script, State, cursor);
            var session = new SessionState();

            TutorialNarrationDriver? narrationRef = null;
            TutorialPreviewCues? cuesRef = null;
            Controller = new GameController(
                State,
                session,
                Map,
                Hud,
                seed: 1,
                aiChooser: (s, c, v, ru, r) => ReplayAi.ChooseNextAction(s, c, v, r),
                aiPacer: new SynchronousAiPacer(),
                humanActionValidator: Preview.TryAccept,
                buyLevelValidator: Preview.AllowBuyLevel,
                previewMode: true,
                isReplayPaused: () => narrationRef?.IsPresenting == true,
                autoSelectFirstTerritory: false,
                onAfterRefresh: () =>
                {
                    // Mirrors PreviewPane's chain: drop the beats no live
                    // actor will consume (elapsed neutral-seat turns) first,
                    // so the narration driver sees the beat behind them.
                    ReplayAi.ConsumeElapsedNeutralSeatBeats(State);
                    narrationRef?.Tick();
                    cuesRef?.Apply();
                });

            var narration = new TutorialNarrationDriver(
                script, cursor, Hud,
                () =>
                {
                    Controller.RefreshViewsForTutorial();
                    Controller.ResumeAiTurnsAfterReplayPause();
                });
            narrationRef = narration;

            var cues = new TutorialPreviewCues(
                Preview, State, session, Hud, Map, Red,
                t => Controller.SelectTerritoryForTutorial(t),
                () => Controller.CancelActionForTutorial());
            cues.SetNarrationDriver(narration);
            cuesRef = cues;

            Controller.StartGame();
        }
    }

    /// <summary>
    /// One full round: Red ends turn, the two scripted opponents end
    /// theirs, the neutral seat takes its (unscripted, no-op) turn, and
    /// control returns to Red. The script's neutral-seat EndTurn beat must
    /// be consumed on the way through so the turn-2 narration presents and
    /// Red's next action is accepted.
    /// </summary>
    [Fact]
    public void NeutralSeatEndTurnBeat_IsConsumed_SoPlayerTurnTwoUnblocks()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            new ReplayEndTurnBeat { Index = 1, Turn = 1, Actor = 1 },
            new ReplayEndTurnBeat { Index = 2, Turn = 1, Actor = 2 },
            new ReplayEndTurnBeat { Index = 3, Turn = 1, Actor = RecordedNeutralActor },
            new ReplayDisplayTextBeat { Index = 4, Turn = 2, Actor = -1, Text = "Your second turn." },
            new ReplayEndTurnBeat { Index = 5, Turn = 2, Actor = 0 },
        };
        var h = new PreviewHarness(script);

        h.Hud.ClickEndTurn();

        // The whole round drains inline through SynchronousAiPacer.
        Assert.Equal(0, h.State.Turns.CurrentPlayerIndex);
        Assert.Equal(2, h.State.Turns.TurnNumber);

        // Turn-2 narration is up (not the stale opponents indicator).
        Assert.Equal("Your second turn.", h.Hud.CurrentTutorialMessage);
        Assert.True(h.Hud.TutorialMessageTappable);

        // Tapping it through leaves Red's own End Turn beat as the next
        // expected action, and the controller accepts it.
        h.Hud.RaiseTutorialMessageTapped();
        Assert.IsType<ReplayEndTurnBeat>(h.Preview.NextPlayer0Beat);
        Assert.True(h.Preview.TryAccept(new ReplayEndTurnBeat()));
    }

    /// <summary>
    /// The consumer only fires once the neutral seat's turn has actually
    /// passed: while the seat is current, its beat stays on the cursor
    /// (a viking-mode script's neutral beats must not be skipped out from
    /// under the seat that is still acting).
    /// </summary>
    [Fact]
    public void NeutralSeatBeat_NotConsumedWhileSeatIsCurrent()
    {
        var roster = new List<Player>
        {
            new("Red", Red, PlayerKind.Human),
            new("Blue", Blue, PlayerKind.Computer),
            new("Green", Green, PlayerKind.Computer),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        var state = new GameState(grid, TestHelpers.BuildTerritoriesFromGrid(grid),
            roster, new TurnState(roster), new Treasury());
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = RecordedNeutralActor },
        };
        var cursor = new ScriptCursor();
        var ai = new ReplayDrivenAi(script, roster, cursor);

        // Park the rotation on the neutral seat (roster of 3 → seat index 3).
        while (!state.Turns.IsNeutralSeat) state.Turns.EndTurn();
        ai.ConsumeElapsedNeutralSeatBeats(state);
        Assert.Equal(0, cursor.Index);

        // Seat's turn over: now the beat is elapsed and gets consumed.
        state.Turns.EndTurn();
        ai.ConsumeElapsedNeutralSeatBeats(state);
        Assert.Equal(1, cursor.Index);
    }

    /// <summary>
    /// A beat still owned by a live player is never skipped — only the
    /// seat past the end of the roster is. Guards against the consumer
    /// papering over a genuine cursor desync.
    /// </summary>
    [Fact]
    public void PlayerBeat_IsNeverConsumed()
    {
        var roster = new List<Player>
        {
            new("Red", Red, PlayerKind.Human),
            new("Blue", Blue, PlayerKind.Computer),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        var state = new GameState(grid, TestHelpers.BuildTerritoriesFromGrid(grid),
            roster, new TurnState(roster), new Treasury());
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 1 },
        };
        var cursor = new ScriptCursor();
        var ai = new ReplayDrivenAi(script, roster, cursor);

        ai.ConsumeElapsedNeutralSeatBeats(state);

        Assert.Equal(0, cursor.Index);
    }
}
