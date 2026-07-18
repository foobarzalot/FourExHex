// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Automate: AI finishes the human's turn (#111) --------------------

    /// <summary>
    /// Scripted automate-chooser: returns the given actions one per
    /// call, then null (the "nothing left to do" terminal signal the
    /// automate loop stops on).
    /// </summary>
    private static Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, DeterministicRng, AiAction?> AutomateScript(
        params AiAction[] actions)
    {
        int index = 0;
        return (s, c, visited, ru, rng) => index >= actions.Length ? null : actions[index++];
    }

    /// <summary>
    /// The canonical TestGame board (Red human at (0,1)/(1,1), capital
    /// (0,1)) with Red's treasury topped up to 100 so the three-action
    /// script in <see cref="ThreeMoveScript"/> is affordable.
    /// </summary>
    private static ControllerHarness BuildAutomateGame(
        AiAction[] script,
        IAiPacer? aiPacer = null,
        bool instant = false,
        int? seed = null)
    {
        ControllerHarness h = TestHelpers.BuildControllerGame(
            aiPacer: aiPacer,
            automateChooser: AutomateScript(script),
            automateIsInstantMode: () => instant,
            seed: seed);
        h.State.Treasury.SetGold(RedCap(h), 100);
        return h;
    }

    private static HexCoord RedCap(ControllerHarness h) =>
        h.State.Territories.First(t => t.Owner == h.Players[0].Id).Capital!.Value;

    /// <summary>
    /// Three legal moves on the canonical fixture: buy a Recruit onto
    /// own empty (1,1); move it to (2,1) capturing a Blue tile; buy a
    /// second Recruit onto the now-empty (1,1). Costs 10+0+10 gold.
    /// </summary>
    private static AiAction[] ThreeMoveScript() => new AiAction[]
    {
        new AiBuyUnitAction(HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1), UnitLevel.Recruit),
        new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1)),
        new AiBuyUnitAction(HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1), UnitLevel.Recruit),
    };

    private static HexTile TileAt(ControllerHarness h, int col, int row) =>
        h.State.Grid.Get(HexCoord.FromOffset(col, row))!;

    [Fact]
    public void Automate_PlaysAllScriptedMovesAndLeavesTurnOpen()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        int turnBefore = h.State.Turns.TurnNumber;

        h.Hud.ClickAutomate();

        // All three actions landed: (2,1) captured with the first
        // recruit on it, a second recruit back on (1,1), 20 gold spent.
        Assert.Equal(h.Players[0].Id, TileAt(h, 2, 1).Owner);
        Assert.NotNull(TileAt(h, 2, 1).Unit);
        Assert.NotNull(TileAt(h, 1, 1).Unit);
        Assert.Equal(80, h.State.Treasury.GetGold(RedCap(h)));
        // The turn is NOT ended — same turn, same (human) player, and
        // the undo stack was not cleared by any end-of-turn path.
        Assert.Equal(turnBefore, h.State.Turns.TurnNumber);
        Assert.Same(h.Players[0], h.State.Turns.CurrentPlayer);
        // Loop stopped on the chooser's null and toggled itself off.
        Assert.False(h.Controller.IsAutomating);
        // One undo entry per automated move, plus one for the loop's
        // initial selection of the acting territory (all three moves
        // act from the same Red territory, so one selection entry).
        Assert.Equal(4, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void Automate_MakeWayTowerBuild_IsTwoDiscreteUndoableMoves()
    {
        // A tower intent on Red's free recruit at (2,1) is lowered into
        // TWO first-class automate moves: the make-way reposition to
        // (1,1), then the build on the vacated tile — each with its own
        // undo entry, peelable one at a time. The recruit's move is
        // never consumed (repositions don't touch HasMovedThisTurn).
        HexCoord unitTile = HexCoord.FromOffset(2, 1);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            ownerOverrides: new[]
            {
                (0, 1, PlayerId.FromIndex(0)),
                (1, 1, PlayerId.FromIndex(0)),
                (2, 1, PlayerId.FromIndex(0)),
            },
            beforeStart: s => s.Grid.Get(unitTile)!.Occupant =
                new Unit(PlayerId.FromIndex(0)),
            automateChooser: AutomateScript(
                new AiBuildTowerAction(HexCoord.FromOffset(0, 1), unitTile)));
        h.State.Treasury.SetGold(RedCap(h), 100);

        h.Hud.ClickAutomate();

        Assert.IsType<Tower>(TileAt(h, 2, 1).Occupant);
        Unit pushed = Assert.IsType<Unit>(TileAt(h, 1, 1).Occupant);
        Assert.False(pushed.HasMovedThisTurn);
        Assert.False(h.Controller.IsAutomating);
        // Selection entry + make-way move + build = three undo entries.
        Assert.Equal(3, h.Session.Undo.UndoCount);

        // Undo peels the build first — the unit stays aside...
        h.Hud.ClickUndoLast();
        Assert.Null(TileAt(h, 2, 1).Occupant);
        Assert.IsType<Unit>(TileAt(h, 1, 1).Occupant);
        // ...then the make-way move — the unit is back on its tile.
        h.Hud.ClickUndoLast();
        Assert.IsType<Unit>(TileAt(h, 2, 1).Occupant);
        Assert.Null(TileAt(h, 1, 1).Occupant);
    }

    [Fact]
    public void Automate_UndoWalksBackMoveByMove_RedoReapplies()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        string before = GameStateChecksum.Stringify(h.State);

        h.Hud.ClickAutomate();
        Assert.Equal(4, h.Session.Undo.UndoCount);

        // Walk back one automated move at a time.
        h.Hud.ClickUndoLast(); // undo buy #2
        Assert.Null(TileAt(h, 1, 1).Unit);
        Assert.Equal(90, h.State.Treasury.GetGold(RedCap(h)));
        h.Hud.ClickUndoLast(); // undo the capture move
        Assert.Equal(h.Players[1].Id, TileAt(h, 2, 1).Owner);
        Assert.NotNull(TileAt(h, 1, 1).Unit);
        h.Hud.ClickUndoLast(); // undo buy #1 — game state fully restored,
        // the loop's selection entry remains on the stack
        Assert.Equal(1, h.Session.Undo.UndoCount);
        Assert.Equal(before, GameStateChecksum.Stringify(h.State));
        h.Hud.ClickUndoLast(); // undo the acting-territory selection
        Assert.Equal(0, h.Session.Undo.UndoCount);
        Assert.Null(h.Session.SelectedTerritory);

        // Redo re-applies the whole sequence.
        h.Hud.ClickRedoAll();
        Assert.Equal(4, h.Session.Undo.UndoCount);
        Assert.Equal(h.Players[0].Id, TileAt(h, 2, 1).Owner);
        Assert.NotNull(TileAt(h, 1, 1).Unit);
        Assert.Equal(80, h.State.Treasury.GetGold(RedCap(h)));
    }

    [Fact]
    public void Automate_InterruptAfterOneMove_StopsBetweenMoves()
    {
        // Manual pacing so the test controls each beat: every automated
        // move is a preview beat (choose + highlight) then an execute
        // beat (mutate + push undo entry).
        var timers = new ManualTimerFactory();
        ControllerHarness h = BuildAutomateGame(
            ThreeMoveScript(), aiPacer: new GodotAiPacer(timers));

        h.Hud.ClickAutomate();
        Assert.True(h.Controller.IsAutomating);
        Assert.True(h.Hud.AutomateRunning);

        timers.FireAll(); // preview beat: choose move #1 (+ selection entry)
        timers.FireAll(); // execute beat: apply move #1
        Assert.Equal(2, h.Session.Undo.UndoCount);

        // Toggle off = interrupt. Halts BETWEEN moves.
        h.Hud.ClickAutomate();
        Assert.False(h.Controller.IsAutomating);
        Assert.False(h.Hud.AutomateRunning);

        // Any stale scheduled beats no-op: no further moves land.
        timers.FireAll();
        timers.FireAll();
        timers.FireAll();
        Assert.Equal(2, h.Session.Undo.UndoCount);
        Assert.Equal(90, h.State.Treasury.GetGold(RedCap(h)));
        Assert.Equal(h.Players[1].Id, TileAt(h, 2, 1).Owner); // move #2 never ran

        // The executed move stays individually undoable (the selection
        // entry beneath it remains).
        h.Hud.ClickUndoLast();
        Assert.Equal(1, h.Session.Undo.UndoCount);
        Assert.Null(TileAt(h, 1, 1).Unit);
    }

    [Fact]
    public void Automate_ManualInputMidRun_StopsAutomationAndHandlesInput()
    {
        var timers = new ManualTimerFactory();
        ControllerHarness h = BuildAutomateGame(
            ThreeMoveScript(), aiPacer: new GodotAiPacer(timers));

        h.Hud.ClickAutomate();
        timers.FireAll(); // preview #1 (+ selection entry)
        timers.FireAll(); // execute #1
        Assert.Equal(2, h.Session.Undo.UndoCount);

        // A TrackHandler-wrapped human input between beats interrupts
        // automation AND is handled normally.
        h.Map.SimulateClick(TileAt(h, 0, 1));
        Assert.False(h.Controller.IsAutomating);
        Assert.NotNull(h.Session.SelectedTerritory);
        Assert.Equal(h.Players[0].Id, h.Session.SelectedTerritory!.Owner);

        // Remaining scheduled beats no-op.
        timers.FireAll();
        timers.FireAll();
        Assert.Equal(90, h.State.Treasury.GetGold(RedCap(h)));
        Assert.Equal(h.Players[1].Id, TileAt(h, 2, 1).Owner);
    }

    [Fact]
    public void Automate_OnAiTurn_NoOp()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            startGame: false,
            automateChooser: AutomateScript(ThreeMoveScript()));

        h.Hud.ClickAutomate();

        Assert.False(h.Controller.IsAutomating);
        Assert.Equal(0, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void Automate_NoLegalActions_StopsImmediately_NoGameMutations()
    {
        ControllerHarness h = BuildAutomateGame(Array.Empty<AiAction>());

        h.Hud.ClickAutomate();

        Assert.False(h.Controller.IsAutomating);
        Assert.False(h.Hud.AutomateRunning);
        // No game mutation — but running to completion marks the
        // still-actionable territory visited (#126: exhausted automation
        // always lights End Turn), and that session change is one
        // undoable step.
        Assert.Equal(100, h.State.Treasury.GetGold(RedCap(h)));
        Assert.Equal(1, h.Session.Undo.UndoCount);
        Assert.Contains(RedCap(h), h.Session.VisitedThisTurnCapitals);
        Assert.True(h.Hud.EndTurnCtaActive);

        // Undo unwinds the marking like any other step.
        h.Hud.ClickUndoLast();
        Assert.Empty(h.Session.VisitedThisTurnCapitals);
        Assert.False(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void Automate_ButtonState_EnabledOnHumanTurnWithActions()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());

        // After StartGame's refresh: human turn, actions available.
        Assert.True(h.Hud.AutomateEnabled);
        Assert.False(h.Hud.AutomateRunning);
    }

    [Fact]
    public void Automate_BeatStacksStayInSyncWithUndoStack()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());

        h.Hud.ClickAutomate();
        Assert.Equal(4, h.Session.Undo.UndoCount);
        Assert.Equal(4, h.Controller.UndoBeatBatchDepth);

        h.Hud.ClickUndoLast();
        Assert.Equal(3, h.Controller.UndoBeatBatchDepth);
        Assert.Equal(1, h.Controller.RedoBeatBatchDepth);

        h.Hud.ClickRedoLast();
        Assert.Equal(4, h.Controller.UndoBeatBatchDepth);
        Assert.Equal(0, h.Controller.RedoBeatBatchDepth);
    }

    [Fact]
    public void Automate_ExhaustingMoves_DisablesButton()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());

        h.Hud.ClickAutomate(); // runs all 3 moves, stops on chooser null

        // Exhaustion latches the button disabled — re-pressing would
        // no-op, so it greys out even though the player still has
        // affordable manual actions (80 gold remain).
        Assert.False(h.Hud.AutomateEnabled);
        Assert.False(h.Hud.AutomateRunning);
    }

    [Fact]
    public void Automate_UserInterrupt_DoesNotDisableButton()
    {
        var timers = new ManualTimerFactory();
        ControllerHarness h = BuildAutomateGame(
            ThreeMoveScript(), aiPacer: new GodotAiPacer(timers));

        h.Hud.ClickAutomate();
        timers.FireAll(); // preview #1
        timers.FireAll(); // execute #1
        h.Hud.ClickAutomate(); // user stop — moves remain, so no latch

        Assert.True(h.Hud.AutomateEnabled);
    }

    [Fact]
    public void Automate_UndoAfterExhaustion_ReenablesButton()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        h.Hud.ClickAutomate();
        Assert.False(h.Hud.AutomateEnabled);

        h.Hud.ClickUndoLast();

        Assert.True(h.Hud.AutomateEnabled);
    }

    [Fact]
    public void Automate_EndTurnAfterExhaustion_ReenablesButton()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        h.Hud.ClickAutomate();
        Assert.False(h.Hud.AutomateEnabled);

        h.Hud.ClickEndTurn(); // advances to Blue (human)

        Assert.True(h.Hud.AutomateEnabled);
        Assert.False(h.Hud.AutomateRunning);
    }

    [Fact]
    public void Automate_ManualGameMutationAfterExhaustion_ReenablesButton()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        h.Hud.ClickAutomate();
        Assert.False(h.Hud.AutomateEnabled);

        // A manual state-changing move (buy a recruit onto the capital's
        // territory) invalidates the "nothing left to automate" verdict.
        h.Map.SimulateClick(TileAt(h, 0, 1));
        h.Hud.ClickBuyRecruit();
        h.Map.SimulateClick(TileAt(h, 3, 1)); // border-adjacent buy-capture

        Assert.True(h.Hud.AutomateEnabled);
    }

    [Fact]
    public void Automate_InTutorialPreviewMode_ButtonHiddenAndInert()
    {
        ControllerHarness h = TestHelpers.BuildControllerGame(
            previewMode: true,
            automateChooser: AutomateScript(ThreeMoveScript()));

        // Hidden — not drawn at all during the player-facing tutorial.
        Assert.False(h.Hud.AutomateVisible);
        // And inert even if the event fires anyway.
        h.Hud.ClickAutomate();
        Assert.False(h.Controller.IsAutomating);
        Assert.Equal(0, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void Automate_InTutorialRecordingMode_ButtonHiddenAndInert()
    {
        ControllerHarness h = TestHelpers.BuildControllerGame(
            recordingMode: true,
            automateChooser: AutomateScript(ThreeMoveScript()));

        Assert.False(h.Hud.AutomateVisible);
        h.Hud.ClickAutomate();
        Assert.False(h.Controller.IsAutomating);
        Assert.Equal(0, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void Automate_NormalGame_ButtonVisible()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());

        Assert.True(h.Hud.AutomateVisible);
    }

    [Fact]
    public void Automate_WithSelectedTerritory_SelectionTracksRecomputedTerritory()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        h.Map.SimulateClick(TileAt(h, 0, 1)); // select Red before automating
        Assert.NotNull(h.Session.SelectedTerritory);

        h.Hud.ClickAutomate();

        // The capture at (2,1) recomputed the territory list; the
        // selection must track the SAME logical territory (its capital)
        // onto the rebuilt object — not go stale.
        Territory? sel = h.Session.SelectedTerritory;
        Assert.NotNull(sel);
        Assert.Contains(sel!, h.State.Territories);
        Assert.Contains(HexCoord.FromOffset(2, 1), sel!.Coords);
        // And the on-map selection border shows exactly that territory.
        Assert.Same(sel, h.Map.LastHighlight);
    }

    [Fact]
    public void Automate_WithPendingBuyMode_CancelsIntentThenAutomates()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        h.Map.SimulateClick(TileAt(h, 0, 1)); // select Red
        h.Hud.ClickBuyRecruit();              // enter BuyingRecruit
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, h.Session.Mode);

        h.Hud.ClickAutomate();

        // The pending intent was cancelled (same as Esc), then the
        // whole script played out.
        Assert.Equal(SessionState.ActionMode.None, h.Session.Mode);
        Assert.Equal(h.Players[0].Id, TileAt(h, 2, 1).Owner);
        Assert.Equal(80, h.State.Treasury.GetGold(RedCap(h)));
        Assert.False(h.Controller.IsAutomating);
    }

    // --- Automate followups: camera pan + Instant track (#112) ------------

    [Fact]
    public void Automate_Paced_CentersCameraOnActingTerritory()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        int centersBefore = h.Map.CenterCount;

        h.Hud.ClickAutomate();

        // One pan when the acting territory is first selected — same
        // look/feel as pressing Next Territory. All three scripted moves
        // act from the SAME Red territory, so the camera eases there once
        // and then stays put (no re-pan while the selection is unchanged).
        Assert.Equal(centersBefore + 1, h.Map.CenterCount);
        Assert.NotNull(h.Map.LastCenteredTerritory);
        Assert.Equal(h.Players[0].Id, h.Map.LastCenteredTerritory!.Owner);
    }

    [Fact]
    public void Automate_Paced_RepansWhenActingTerritoryChanges()
    {
        // Red owns two disconnected territories: {(0,1),(1,1)} and
        // {(3,0),(4,0)}. The chooser buys a Recruit in each in turn, so the
        // acting territory CHANGES between the two automate steps — the
        // camera eases once per distinct territory (guards against an
        // over-fix that never re-pans).
        PlayerId red = PlayerId.FromIndex(0);
        int step = 0;
        ControllerHarness h = TestHelpers.BuildControllerGame(
            ownerOverrides: new[]
            {
                (0, 1, red), (1, 1, red),
                (3, 0, red), (4, 0, red),
            },
            beforeStart: s =>
            {
                foreach (Territory t in s.Territories)
                {
                    if (t.Owner == red) s.Treasury.SetGold(t.Capital!.Value, 100);
                }
            },
            automateChooser: (s, c, visited, ru, rng) =>
            {
                var reds = s.Territories
                    .Where(t => t.Owner == red)
                    .OrderBy(t => t.Capital!.Value)
                    .ToList();
                if (step >= reds.Count) return null;
                Territory terr = reds[step++];
                HexCoord cap = terr.Capital!.Value;
                HexCoord dest = terr.Coords.First(
                    x => !x.Equals(cap) && s.Grid.Get(x)!.Occupant == null);
                return new AiBuyUnitAction(cap, dest, UnitLevel.Recruit);
            });
        int centersBefore = h.Map.CenterCount;

        h.Hud.ClickAutomate();

        // Two distinct acting territories → two eases.
        Assert.Equal(centersBefore + 2, h.Map.CenterCount);
    }

    [Fact]
    public void Automate_Instant_NoCameraPan()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript(), instant: true);
        int centersBefore = h.Map.CenterCount;

        h.Hud.ClickAutomate();

        // The instant track fast-forwards with no per-move presentation:
        // the camera never moves. The moves still all landed.
        Assert.Equal(centersBefore, h.Map.CenterCount);
        Assert.Equal(80, h.State.Treasury.GetGold(RedCap(h)));
        Assert.False(h.Controller.IsAutomating);
    }

    [Fact]
    public void Automate_Instant_NoSounds_PacedStillAudible()
    {
        ControllerHarness instant = BuildAutomateGame(ThreeMoveScript(), instant: true);
        instant.Hud.ClickAutomate();
        // Two buys + a move ran, but the cue gate dropped every sound.
        Assert.Empty(instant.Map.UnitPlacedSounds);
        // Silence lifts once the batch ends.
        Assert.False(instant.Map.SilentMode);

        // A human turn at any paced speed keeps its per-move sounds.
        ControllerHarness paced = BuildAutomateGame(ThreeMoveScript());
        paced.Hud.ClickAutomate();
        Assert.NotEmpty(paced.Map.UnitPlacedSounds);
        Assert.False(paced.Map.SilentMode);
    }

    [Fact]
    public void Automate_Instant_ViewSilentFlagLifecycle()
    {
        var timers = new ManualTimerFactory();
        ControllerHarness h = BuildAutomateGame(
            ThreeMoveScript(), aiPacer: new GodotAiPacer(timers), instant: true);

        h.Hud.ClickAutomate();
        // Dispatch synced the view silent before the first instant tick.
        Assert.True(h.Map.SilentMode);

        // Drain the batch (well under the per-tick budget, so one tick).
        timers.FireAll();
        timers.FireAll();
        timers.FireAll();
        Assert.False(h.Map.SilentMode);
        Assert.False(h.Controller.IsAutomating);
        Assert.Equal(80, h.State.Treasury.GetGold(RedCap(h)));
    }

    [Fact]
    public void Automate_Instant_SameFinalStateAndUndoDepthAsPaced()
    {
        ControllerHarness paced = BuildAutomateGame(ThreeMoveScript(), seed: 42);
        ControllerHarness instant = BuildAutomateGame(ThreeMoveScript(), seed: 42, instant: true);

        paced.Hud.ClickAutomate();
        instant.Hud.ClickAutomate();

        // The instant track is a fast-forward of the paced one, not a
        // different game: identical board and identical per-move undo
        // (beat lockstep included).
        Assert.Equal(
            GameStateChecksum.Stringify(paced.State),
            GameStateChecksum.Stringify(instant.State));
        Assert.Equal(paced.Session.Undo.UndoCount, instant.Session.Undo.UndoCount);
        Assert.Equal(paced.Controller.UndoBeatBatchDepth, instant.Controller.UndoBeatBatchDepth);
    }

    [Fact]
    public void Automate_Instant_UndoWalksBackMoveByMove()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript(), instant: true);
        string before = GameStateChecksum.Stringify(h.State);

        h.Hud.ClickAutomate();
        Assert.Equal(4, h.Session.Undo.UndoCount);

        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast(); // the selection entry
        Assert.Equal(0, h.Session.Undo.UndoCount);
        Assert.Equal(before, GameStateChecksum.Stringify(h.State));
    }

    [Fact]
    public void Automate_SelectsActingTerritory_SelectionUndoable()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        Assert.Null(h.Session.SelectedTerritory);

        h.Hud.ClickAutomate();

        // The loop selected each acting territory before its move, like
        // the player pressing Next Territory; the last selection
        // persists after the loop stops. All three moves act from the
        // same Red territory, so there is one selection entry.
        Territory? sel = h.Session.SelectedTerritory;
        Assert.NotNull(sel);
        Assert.Equal(h.Players[0].Id, sel!.Owner);
        Assert.Equal(4, h.Session.Undo.UndoCount);

        // Undo past the moves: the selection entry is its own step and
        // restores the pre-automate (empty) selection.
        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast();
        Assert.NotNull(h.Session.SelectedTerritory);
        h.Hud.ClickUndoLast();
        Assert.Null(h.Session.SelectedTerritory);
        Assert.Equal(0, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void Automate_Instant_SelectsActingTerritory_SelectionUndoable()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript(), instant: true);
        Assert.Null(h.Session.SelectedTerritory);

        h.Hud.ClickAutomate();

        // Same selection + undo entries as the paced track — the
        // instant drain skips the visuals, not the bookkeeping.
        Assert.NotNull(h.Session.SelectedTerritory);
        Assert.Equal(4, h.Session.Undo.UndoCount);

        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast();
        h.Hud.ClickUndoLast();
        Assert.Null(h.Session.SelectedTerritory);
        Assert.Equal(0, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void Automate_Instant_OverlayThenGameOver_StopsAndLiftsSilence()
    {
        // Blue reduced to two tiles; the script buy-captures both. Move
        // #1 destroys Blue's capital, so the defeat overlay halts the
        // batch mid-run; after dismissal, re-automating plays move #2,
        // which dominates the board and ends the game. The script list
        // is filled after construction because the capital coord isn't
        // known until reconciliation.
        var script = new List<AiAction>();
        int index = 0;
        ControllerHarness h = TestHelpers.BuildControllerGame(
            seed: 42,
            defaultOwner: PlayerId.FromIndex(0),
            ownerOverrides: new[]
            {
                (3, 0, PlayerId.FromIndex(1)),
                (4, 0, PlayerId.FromIndex(1)),
            },
            automateChooser: (s, c, visited, ru, rng) =>
                index >= script.Count ? null : script[index++],
            automateIsInstantMode: () => true);
        h.State.Treasury.SetGold(RedCap(h), 100);
        // Soldiers, not Recruits: Blue's capital defends its two tiles
        // at level 1, so a Recruit buy-capture is rejected as illegal.
        script.Add(new AiBuyUnitAction(
            RedCap(h), HexCoord.FromOffset(3, 0), UnitLevel.Soldier));
        script.Add(new AiBuyUnitAction(
            RedCap(h), HexCoord.FromOffset(4, 0), UnitLevel.Soldier));

        h.Hud.ClickAutomate();

        // Move #1 defeated Blue: the overlay halts the batch between
        // moves, with silence lifted so the overlay paints.
        Assert.Equal(h.Players[1].Id, h.Session.PendingDefeatScreen);
        Assert.False(h.Controller.IsAutomating);
        Assert.False(h.Map.SilentMode);
        Assert.Equal(h.Players[0].Id, TileAt(h, 3, 0).Owner);
        Assert.Equal(h.Players[1].Id, TileAt(h, 4, 0).Owner);

        // Dismiss and re-automate: move #2 wins by domination; the
        // game-over stop also leaves the view unsilenced.
        h.Hud.ClickDefeatContinue();
        h.Hud.ClickAutomate();

        Assert.True(h.Session.IsGameOver);
        Assert.Equal(h.Players[0].Id, h.Session.Winner);
        Assert.False(h.Controller.IsAutomating);
        Assert.False(h.Map.SilentMode);
    }
}
