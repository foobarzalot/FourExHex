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
    private static Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?> AutomateScript(
        params AiAction[] actions)
    {
        int index = 0;
        return (s, c, visited, rng) => index >= actions.Length ? null : actions[index++];
    }

    /// <summary>
    /// The canonical TestGame board (Red human at (0,1)/(1,1), capital
    /// (0,1)) with Red's treasury topped up to 100 so the three-action
    /// script in <see cref="ThreeMoveScript"/> is affordable.
    /// </summary>
    private static ControllerHarness BuildAutomateGame(
        AiAction[] script,
        IAiPacer? aiPacer = null)
    {
        ControllerHarness h = TestHelpers.BuildControllerGame(
            aiPacer: aiPacer,
            automateChooser: AutomateScript(script));
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
        // One undo entry per automated move.
        Assert.Equal(3, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void Automate_UndoWalksBackMoveByMove_RedoReapplies()
    {
        ControllerHarness h = BuildAutomateGame(ThreeMoveScript());
        string before = GameStateChecksum.Stringify(h.State);

        h.Hud.ClickAutomate();
        Assert.Equal(3, h.Session.Undo.UndoCount);

        // Walk back one automated move at a time.
        h.Hud.ClickUndoLast(); // undo buy #2
        Assert.Null(TileAt(h, 1, 1).Unit);
        Assert.Equal(90, h.State.Treasury.GetGold(RedCap(h)));
        h.Hud.ClickUndoLast(); // undo the capture move
        Assert.Equal(h.Players[1].Id, TileAt(h, 2, 1).Owner);
        Assert.NotNull(TileAt(h, 1, 1).Unit);
        h.Hud.ClickUndoLast(); // undo buy #1
        Assert.Equal(0, h.Session.Undo.UndoCount);
        Assert.Equal(before, GameStateChecksum.Stringify(h.State));

        // Redo re-applies the whole sequence.
        h.Hud.ClickRedoAll();
        Assert.Equal(3, h.Session.Undo.UndoCount);
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

        timers.FireAll(); // preview beat: choose move #1
        timers.FireAll(); // execute beat: apply move #1
        Assert.Equal(1, h.Session.Undo.UndoCount);

        // Toggle off = interrupt. Halts BETWEEN moves.
        h.Hud.ClickAutomate();
        Assert.False(h.Controller.IsAutomating);
        Assert.False(h.Hud.AutomateRunning);

        // Any stale scheduled beats no-op: no further moves land.
        timers.FireAll();
        timers.FireAll();
        timers.FireAll();
        Assert.Equal(1, h.Session.Undo.UndoCount);
        Assert.Equal(90, h.State.Treasury.GetGold(RedCap(h)));
        Assert.Equal(h.Players[1].Id, TileAt(h, 2, 1).Owner); // move #2 never ran

        // The executed move stays individually undoable.
        h.Hud.ClickUndoLast();
        Assert.Equal(0, h.Session.Undo.UndoCount);
        Assert.Null(TileAt(h, 1, 1).Unit);
    }

    [Fact]
    public void Automate_ManualInputMidRun_StopsAutomationAndHandlesInput()
    {
        var timers = new ManualTimerFactory();
        ControllerHarness h = BuildAutomateGame(
            ThreeMoveScript(), aiPacer: new GodotAiPacer(timers));

        h.Hud.ClickAutomate();
        timers.FireAll(); // preview #1
        timers.FireAll(); // execute #1
        Assert.Equal(1, h.Session.Undo.UndoCount);

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
    public void Automate_NoLegalActions_StopsImmediately_NoUndoEntries()
    {
        ControllerHarness h = BuildAutomateGame(Array.Empty<AiAction>());

        h.Hud.ClickAutomate();

        Assert.False(h.Controller.IsAutomating);
        Assert.False(h.Hud.AutomateRunning);
        Assert.Equal(0, h.Session.Undo.UndoCount);
        Assert.Equal(100, h.State.Treasury.GetGold(RedCap(h)));
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
        Assert.Equal(3, h.Session.Undo.UndoCount);
        Assert.Equal(3, h.Controller.UndoBeatBatchDepth);

        h.Hud.ClickUndoLast();
        Assert.Equal(2, h.Controller.UndoBeatBatchDepth);
        Assert.Equal(1, h.Controller.RedoBeatBatchDepth);

        h.Hud.ClickRedoLast();
        Assert.Equal(3, h.Controller.UndoBeatBatchDepth);
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
}
