// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Game-over pause --------------------------------------------------
    //
    // Every ending (a declared winner) and every mid-game human defeat
    // enters the game-over pause: the HUD chrome hides and the endgame
    // overlays are held at once, the board settles (a move's travel tween
    // stretches this), then StepPacing.EndgamePauseHintDelayMs later the
    // continue hint shows. A clean tap anywhere on the map, or the HUD's
    // Enter / Space / Escape request, continues: hint hidden, chrome
    // back, hold released, views refreshed. Pan/zoom never reach the
    // controller, so they can't continue.

    /// <summary>4x1: Red (human) {0,1,2} with a Soldier at (2,0); Blue's
    /// lone tile (3,0). The click-move 2→3 completes domination.</summary>
    private static ControllerHarness BuildHumanDominationBoard(IAiPacer pacer)
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        return TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (3, 0, blue.Id) },
            seed: 0, aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
    }

    /// <summary>Play the winning move on <see cref="BuildHumanDominationBoard"/>
    /// and return the harness in the freshly entered pause.</summary>
    private static ControllerHarness EnterWinningPause(QueuedAiPacer pacer)
    {
        ControllerHarness h = BuildHumanDominationBoard(pacer);
        pacer.DrainAll(); // discard startup scheduling
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(2, 0))); // select + pick up
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0))); // winning move
        Assert.Equal(h.Players[0].Id, h.Session.Winner);
        Assert.True(h.Controller.EndgamePauseActive);
        return h;
    }

    private static void AssertPaused(ControllerHarness h)
    {
        Assert.True(h.Controller.EndgamePauseActive);
        Assert.True(h.Hud.EndgameOverlaysHeld);
        Assert.True(h.Hud.ChromeHidden);
    }

    private static void AssertContinued(ControllerHarness h)
    {
        Assert.False(h.Controller.EndgamePauseActive);
        Assert.False(h.Hud.EndgameOverlaysHeld);
        Assert.False(h.Hud.ChromeHidden);
        Assert.False(h.Hud.EndgameContinueHintShown);
    }

    [Fact]
    public void HumanMove_DominationWin_EntersPause_HintAfterSettleThenDelay()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);

        AssertPaused(h);
        Assert.False(h.Hud.EndgameContinueHintShown);
        // One chain only: the move's settle delay first…
        Assert.Equal(1, pacer.PendingCount);
        Assert.Equal(StepPacing.MoveSettleDelayMs(1), pacer.ScheduledDelaysMs[^1]);

        pacer.StepOne(); // settle
        Assert.False(h.Hud.EndgameContinueHintShown);
        // …then the unscaled hint delay.
        Assert.Equal(1, pacer.PendingCount);
        Assert.Equal(StepPacing.EndgamePauseHintDelayMs, pacer.ScheduledDelaysMs[^1]);

        pacer.StepOne(); // hint
        Assert.True(h.Hud.EndgameContinueHintShown);
        AssertPaused(h);
        Assert.False(pacer.HasPending);

        int refreshesBefore = h.Hud.RefreshCount;
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(0, 0))); // tap anywhere

        AssertContinued(h);
        Assert.True(h.Hud.RefreshCount > refreshesBefore);
        Assert.Equal(h.Players[0].Id, h.Hud.LastSeenWinner);
    }

    [Fact]
    public void GameOver_RendersEveryUnitInert_BeforeAndAfterContinue()
    {
        // No unit pulses white on the finished board: the occupant pass is
        // handed no current player once the game is over, during the pause
        // and after the modal is revealed alike.
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);
        Assert.Null(h.Map.LastOccupantRefreshPlayer);

        pacer.DrainAll();
        h.Map.SimulateOffGridClick(HexCoord.FromOffset(9, 9));

        AssertContinued(h);
        Assert.Null(h.Map.LastOccupantRefreshPlayer);
    }

    [Fact]
    public void MidGameDefeatPause_RendersEveryUnitInert_UntilDismissed()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        AiAction? scriptedKill = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: PlayerId.None,
            ownerOverrides: new[]
            {
                (0, 0, blue.Id), (1, 0, blue.Id), (2, 0, blue.Id),
                (3, 0, red.Id), (4, 0, red.Id),
            },
            seed: 0,
            aiChooser: (s, c, v, ru, r) => { AiAction? n = scriptedKill; scriptedKill = null; return n; },
            aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));

        h.Hud.ClickEndTurn();
        pacer.StepOne(); // preview
        pacer.StepOne(); // execute: Red eliminated → pause
        Assert.Equal(red.Id, h.Session.PendingDefeatScreen);
        Assert.Null(h.Map.LastOccupantRefreshPlayer);

        pacer.DrainAll();
        h.Map.SimulateOffGridClick(HexCoord.FromOffset(9, 9)); // reveal the defeat overlay

        // The pause is over and the game goes on behind the modal: Blue's
        // units are live again from here (the overlay covers the board).
        Assert.Equal(blue.Id, h.Map.LastOccupantRefreshPlayer);
        h.Hud.ClickDefeatContinue();
        Assert.Equal(blue.Id, h.Map.LastOccupantRefreshPlayer);
    }

    [Fact]
    public void EarlyTap_ContinuesAtOnce_AndCancelsPendingHint()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(1, 0)));

        AssertContinued(h);
        pacer.DrainAll(); // the stale chain must be a no-op
        Assert.False(h.Hud.EndgameContinueHintShown);
        Assert.False(h.Hud.ChromeHidden);
    }

    [Fact]
    public void OffGridTap_Continues()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);
        pacer.DrainAll();
        Assert.True(h.Hud.EndgameContinueHintShown);

        h.Map.SimulateOffGridClick(HexCoord.FromOffset(9, 9));

        AssertContinued(h);
    }

    [Fact]
    public void LongPress_Continues()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);
        pacer.DrainAll();

        h.Map.SimulateLongClick(h.State.Grid.Get(HexCoord.FromOffset(0, 0)));

        AssertContinued(h);
    }

    [Fact]
    public void HudContinueRequest_Continues()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);
        pacer.DrainAll();

        h.Hud.RaiseEndgameContinue();

        AssertContinued(h);
    }

    [Fact]
    public void TapDuringPause_DoesNotSelectOrPushUndo()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);
        int undoBefore = h.Session.Undo.UndoCount;
        Territory? selectedBefore = h.Session.SelectedTerritory;

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(0, 0)));

        Assert.Same(selectedBefore, h.Session.SelectedTerritory);
        Assert.Equal(undoBefore, h.Session.Undo.UndoCount);
    }

    [Fact]
    public void AiMove_DefeatingHuman_EntersPause_AiStaysParkedUntilDefeatContinue()
    {
        // 5x1: Blue (AI) {(0,0),(1,0),(2,0)} with a Soldier at (2,0);
        // Red (human) {(3,0),(4,0)}, capital at (3,0). The scripted kill
        // move captures Red's capital, eliminating Red.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        AiAction? scriptedKill = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, HashSet<HexCoord> ru, DeterministicRng r)
        {
            AiAction? next = scriptedKill;
            scriptedKill = null;
            return next;
        }
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: PlayerId.None,
            ownerOverrides: new[]
            {
                (0, 0, blue.Id), (1, 0, blue.Id), (2, 0, blue.Id),
                (3, 0, red.Id), (4, 0, red.Id),
            },
            seed: 0, aiChooser: Chooser, aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));

        h.Hud.ClickEndTurn(); // Red → Blue (AI); schedules the AI preview
        pacer.StepOne();      // preview: choose the kill move
        pacer.StepOne();      // execute: capture → PendingDefeatScreen set

        Assert.Equal(red.Id, h.Session.PendingDefeatScreen);
        Assert.False(h.Session.IsGameOver);
        AssertPaused(h);
        Assert.Equal(1, pacer.PendingCount);
        Assert.Equal(StepPacing.MoveSettleDelayMs(1), pacer.ScheduledDelaysMs[^1]);
        Assert.Equal(1, h.Map.BankruptcySoundCount);   // loss cue, at once
        Assert.Equal(1, h.Map.PlayerDefeatedSoundCount);

        pacer.DrainAll();     // settle + hint; the AI must not advance
        Assert.True(h.Hud.EndgameContinueHintShown);
        Assert.Equal(red.Id, h.Session.PendingDefeatScreen);

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(4, 0)));
        AssertContinued(h);
        Assert.Equal(red.Id, h.Session.PendingDefeatScreen); // overlay now showing
        Assert.False(pacer.HasPending);                     // AI still parked

        h.Hud.ClickDefeatContinue();
        Assert.Null(h.Session.PendingDefeatScreen);
        Assert.True(pacer.HasPending); // the AI loop re-armed
    }

    [Fact]
    public void DefeatContinue_WhilePaused_ContinuesAndDismisses()
    {
        // A dismiss handler reached mid-pause (a scripted flow, not a
        // visible button) collapses the pause into the dismissal.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        AiAction? scriptedKill = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: PlayerId.None,
            ownerOverrides: new[]
            {
                (0, 0, blue.Id), (1, 0, blue.Id), (2, 0, blue.Id),
                (3, 0, red.Id), (4, 0, red.Id),
            },
            seed: 0,
            aiChooser: (s, c, v, ru, r) => { AiAction? n = scriptedKill; scriptedKill = null; return n; },
            aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));

        h.Hud.ClickEndTurn();
        pacer.StepOne();      // preview
        pacer.StepOne();      // execute: the kill move
        Assert.Equal(red.Id, h.Session.PendingDefeatScreen);
        AssertPaused(h);

        h.Hud.ClickDefeatContinue();

        Assert.Null(h.Session.PendingDefeatScreen);
        AssertContinued(h);
        Assert.True(pacer.HasPending); // the AI loop re-armed
    }

    [Fact]
    public void InstantAi_DefeatingHuman_RebuildsBoardBeforePause()
    {
        // The Instant batch suppresses per-capture map rebuilds; when it
        // stops for the defeat overlay the paused board is on show, so the
        // captured tiles must already wear the captor's color.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        AiAction? scriptedKill = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: PlayerId.None,
            ownerOverrides: new[]
            {
                (0, 0, blue.Id), (1, 0, blue.Id), (2, 0, blue.Id),
                (3, 0, red.Id), (4, 0, red.Id),
            },
            seed: 0,
            aiChooser: (s, c, v, ru, r) => { AiAction? n = scriptedKill; scriptedKill = null; return n; },
            aiSilentMode: () => true,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));
        int rebuildsBefore = h.Map.RebuildCount;

        h.Hud.ClickEndTurn(); // Blue's instant batch: kill move, then stop

        Assert.Equal(red.Id, h.Session.PendingDefeatScreen);
        AssertPaused(h);
        Assert.True(h.Map.RebuildCount > rebuildsBefore, "board not rebuilt before the pause");
        Assert.False(h.Map.SilentMode);
    }

    [Fact]
    public void AiBuy_DominationWin_EntersPause_WithBaselineSettle()
    {
        // 4x1 Blue (AI) board; the game-ending action is a BUY — no
        // travel tween, so the settle is the baseline action delay.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        bool chosen = false;
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            defaultOwner: blue.Id,
            ownerOverrides: new[] { (3, 0, red.Id) },
            currentPlayerIndex: 1,
            seed: 0,
            aiChooser: (s, c, v, ru, r) =>
            {
                if (chosen) return null;
                chosen = true;
                HexCoord cap = s.Territories.First(t => t.Owner == c).Capital!.Value;
                return new AiBuyUnitAction(cap, HexCoord.FromOffset(3, 0), UnitLevel.Recruit);
            },
            aiPacer: pacer,
            suppressClaimVictory: false);

        pacer.StepOne();      // preview
        pacer.StepOne();      // execute: buy-capture → domination

        Assert.Equal(blue.Id, h.Session.Winner);
        AssertPaused(h);
        Assert.Equal(1, pacer.PendingCount);
        Assert.Equal(StepPacing.AiActionDelayMs, pacer.ScheduledDelaysMs[^1]);

        pacer.DrainAll();
        Assert.True(h.Hud.EndgameContinueHintShown);
    }

    [Fact]
    public void EndOfTurnWin_EntersPause()
    {
        // 4x1: Red {0,1,2} capital-bearing, Blue's (3,0) a capital-less
        // singleton. Red ending the turn is the sole capital-bearer → wins.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (3, 0, blue.Id) },
            seed: 0, aiPacer: pacer,
            suppressClaimVictory: false);
        pacer.DrainAll();

        h.Hud.ClickEndTurn();

        Assert.Equal(red.Id, h.Session.Winner);
        AssertPaused(h);
        Assert.Equal(StepPacing.AiActionDelayMs, pacer.ScheduledDelaysMs[^1]);
        pacer.DrainAll();
        Assert.True(h.Hud.EndgameContinueHintShown);
        Assert.Equal(1, h.Map.GameWonSoundCount);
        Assert.Equal(0, h.Map.BankruptcySoundCount);
    }

    [Fact]
    public void ClaimVictoryWinNow_EntersPause()
    {
        // 5x2 with Red on 8 of 10 tiles: End Turn raises the claim prompt.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 2,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (3, 1, blue.Id), (4, 1, blue.Id) },
            seed: 0, aiPacer: pacer,
            suppressClaimVictory: false);
        pacer.DrainAll();

        h.Hud.ClickEndTurn();
        Assert.True(h.Session.PendingClaimVictory.HasValue);
        Assert.False(h.Controller.EndgamePauseActive); // the prompt itself is not an ending

        h.Hud.ClickClaimVictoryWinNow();

        Assert.Equal(red.Id, h.Session.Winner);
        AssertPaused(h);
        pacer.DrainAll();
        Assert.True(h.Hud.EndgameContinueHintShown);
    }

    [Fact]
    public void VikingWipeout_EntersPause_WithLossCue()
    {
        // Mirrors VikingRaiders_TotalWipeout_VikingsWin: the neutral seat
        // takes both capitals; Red's mid-game defeat pauses first, then the
        // Vikings' outright win pauses again.
        HexGrid grid = TestHelpers.BuildSpotGrid(
            PlayerId.FromIndex(0),
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(2, 1));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), PlayerId.FromIndex(1)));
        grid.Add(new HexTile(HexCoord.FromOffset(0, 1), PlayerId.FromIndex(1)));
        HexCoord redSea = HexCoord.FromOffset(3, 0);
        HexCoord blueSea = HexCoord.FromOffset(-1, 0);
        var g = new VikingGame(
            grid,
            water: new HashSet<HexCoord> { redSea, blueSea },
            turnNumber: 3,
            blueKind: PlayerKind.Computer,
            beforeStart: s => s.Vikings.Reset(
                new[]
                {
                    new SeaViking(redSea, UnitLevel.Captain),
                    new SeaViking(blueSea, UnitLevel.Captain),
                },
                nextWaveIndex: VikingRaidersRules.TotalWaves,
                lastCompletedRound: 0,
                lastSpawnRound: 2));

        g.Hud.ClickEndTurn(); // Red ends → Blue AI → the neutral seat raids

        Assert.Equal(g.Red.Id, g.Session.PendingDefeatScreen);
        Assert.True(g.Controller.EndgamePauseActive);
        Assert.True(g.Hud.ChromeHidden);
        Assert.Equal(1, g.Map.BankruptcySoundCount);

        g.Map.SimulateOffGridClick(HexCoord.FromOffset(5, 5)); // continue → defeat overlay
        Assert.False(g.Controller.EndgamePauseActive);
        g.Hud.ClickDefeatContinue();                            // neutral seat finishes

        Assert.True(g.Session.IsGameOver);
        Assert.Equal(PlayerId.None, g.Session.Winner);
        Assert.True(g.Controller.EndgamePauseActive);
        Assert.True(g.Hud.EndgameOverlaysHeld);
        Assert.True(g.Hud.ChromeHidden);
        Assert.True(g.Hud.EndgameContinueHintShown); // synchronous pacer: chain ran inline
        Assert.Equal(2, g.Map.BankruptcySoundCount);  // defeat + the final loss
        Assert.Equal(0, g.Map.GameWonSoundCount);
    }

    [Fact]
    public void TideDrowning_EntersPause_AndLocksOtherHumansUntilContinue()
    {
        // Mirrors RisingTides_SubmergeEliminatesHumanAtTurnEnd_RaisesDefeatScreen:
        // Red drowns its own capital at end of turn 1; Blue (human) is
        // next up but the board is paused for Red's defeat.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var green = new Player("Green", PlayerId.FromIndex(2));
        var players = new List<Player> { red, blue, green };
        var grid = TestHelpers.BuildRectGrid(8, 1, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(5, 0))!.Owner = green.Id;
        grid.Get(HexCoord.FromOffset(6, 0))!.Owner = green.Id;
        grid.Get(HexCoord.FromOffset(7, 0))!.Owner = green.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.RisingTides);
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        // Seeded: the tide forecast is RNG-driven and Blue's own end-of-turn
        // flood must not also drown Blue for the hand-off to Green below.
        var controller = new GameController(state, session, map, hud, seed: 1);
        controller.StartGame();

        hud.ClickEndTurn(); // Red drowns its own capital

        Assert.Equal(red.Id, session.PendingDefeatScreen);
        Assert.Equal(blue.Id, state.Turns.CurrentPlayer.Id);
        Assert.True(controller.EndgamePauseActive);
        Assert.True(hud.ChromeHidden);
        Assert.Equal(1, map.BankruptcySoundCount);

        // Blue's inputs are inert while the board is paused.
        int turnBefore = state.Turns.TurnNumber;
        Territory? selectedBefore = session.SelectedTerritory;
        hud.ClickEndTurn();
        hud.ClickBuyRecruit();
        hud.PressNextTerritory();
        Assert.Equal(blue.Id, state.Turns.CurrentPlayer.Id);
        Assert.Equal(turnBefore, state.Turns.TurnNumber);
        Assert.Same(selectedBefore, session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, session.Mode);

        map.SimulateOffGridClick(HexCoord.FromOffset(9, 9)); // continue
        Assert.False(controller.EndgamePauseActive);
        hud.ClickDefeatContinue();
        hud.ClickEndTurn(); // Blue can act again
        Assert.Equal(green.Id, state.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void EndOfTurnDefeat_WithAiUpNext_AiWaitsUnderThePause()
    {
        // Red drowns its own capital at end of turn 1 (Rising Tides, seeded);
        // Blue (AI) is up next. The paced AI must not play under the pause —
        // it waits until the defeat is continued and dismissed.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var green = new Player("Green", PlayerId.FromIndex(2));
        var players = new List<Player> { red, blue, green };
        var grid = TestHelpers.BuildRectGrid(8, 1, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(5, 0))!.Owner = green.Id;
        grid.Get(HexCoord.FromOffset(6, 0))!.Owner = green.Id;
        grid.Get(HexCoord.FromOffset(7, 0))!.Owner = green.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.RisingTides);
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var pacer = new QueuedAiPacer();
        var controller = new GameController(state, session, map, hud, seed: 1, aiPacer: pacer);
        controller.StartGame();
        pacer.DrainAll();

        hud.ClickEndTurn(); // Red drowns its own capital; Blue (AI) is next
        Assert.Equal(red.Id, session.PendingDefeatScreen);
        Assert.Equal(blue.Id, state.Turns.CurrentPlayer.Id);
        Assert.True(controller.EndgamePauseActive);

        pacer.DrainAll(); // hint chain only — no AI beats may run

        Assert.Equal(blue.Id, state.Turns.CurrentPlayer.Id);
        Assert.Equal(red.Id, session.PendingDefeatScreen);
        Assert.True(hud.EndgameContinueHintShown);

        map.SimulateOffGridClick(HexCoord.FromOffset(9, 9)); // continue → defeat overlay
        Assert.Equal(blue.Id, state.Turns.CurrentPlayer.Id); // still waiting
        hud.ClickDefeatContinue();                            // now Blue plays
        pacer.DrainAll();
        Assert.Equal(green.Id, state.Turns.CurrentPlayer.Id);
    }

    [Fact]
    public void InstantAutomate_Win_EntersPause()
    {
        // Mirrors Automate_Instant_OverlayThenGameOver_StopsAndLiftsSilence:
        // the Instant track pauses on both the mid-game defeat and the win.
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
        HexCoord redCap = h.State.Territories.First(t => t.Owner == h.Players[0].Id).Capital!.Value;
        h.State.Treasury.SetGold(redCap, 100);
        script.Add(new AiBuyUnitAction(redCap, HexCoord.FromOffset(3, 0), UnitLevel.Soldier));
        script.Add(new AiBuyUnitAction(redCap, HexCoord.FromOffset(4, 0), UnitLevel.Soldier));

        h.Hud.ClickAutomate();

        Assert.Equal(h.Players[1].Id, h.Session.PendingDefeatScreen);
        AssertPaused(h);
        Assert.True(h.Hud.EndgameContinueHintShown); // synchronous pacer

        h.Map.SimulateOffGridClick(HexCoord.FromOffset(9, 9));
        AssertContinued(h);
        h.Hud.ClickDefeatContinue();
        h.Hud.ClickAutomate();

        Assert.True(h.Session.IsGameOver);
        Assert.Equal(h.Players[0].Id, h.Session.Winner);
        AssertPaused(h);
        Assert.True(h.Hud.EndgameContinueHintShown);
        Assert.False(h.Map.SilentMode);
    }

    [Fact]
    public void AbandonGame_ExitsPause()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);

        h.Controller.AbandonGame();

        Assert.False(h.Controller.EndgamePauseActive);
        Assert.False(h.Hud.EndgameOverlaysHeld);
        Assert.False(h.Hud.ChromeHidden);
        Assert.False(h.Hud.EndgameContinueHintShown);
    }

    // --- Replay -----------------------------------------------------------

    [Fact]
    public void Replay_HoldsOverlaysThroughPlayback_ThenPausesAtTheWinner()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EnterWinningPause(pacer);
        pacer.DrainAll();
        h.Map.SimulateOffGridClick(HexCoord.FromOffset(9, 9)); // reveal the modal
        AssertContinued(h);

        h.Controller.BeginReplay(); // Main wires the HUD's Replay button to this
        Assert.True(h.Controller.IsReplayMode);
        Assert.False(h.Controller.EndgamePauseActive);
        Assert.True(h.Hud.EndgameOverlaysHeld);   // held for the whole playback
        Assert.False(h.Hud.ChromeHidden);         // chrome stays up while replaying
        Assert.Null(h.Session.Winner);

        pacer.DrainAll(); // plays the recording to its game-ending beat

        Assert.False(h.Controller.IsReplayMode);
        Assert.Equal(h.Players[0].Id, h.Session.Winner);
        AssertPaused(h);
        Assert.True(h.Hud.EndgameContinueHintShown); // EndReplay's Cancel didn't eat the hint

        h.Map.SimulateOffGridClick(HexCoord.FromOffset(9, 9));
        AssertContinued(h);
        Assert.Equal(h.Players[0].Id, h.Hud.LastSeenWinner);
    }

    [Fact]
    public void Replay_MidGameHumanDefeat_NeverPausesAndNeverPaintsTheDefeatOverlay()
    {
        // 7x1, three humans: Red {0,1,2} Soldier at (2,0); Blue {3,4};
        // Green {5,6}. Red's move 2→3 takes Blue's capital tile (Blue's
        // capital sits on one of its two tiles; either way Blue drops to a
        // singleton). Recorded live, then replayed.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var green = new Player("Green", PlayerId.FromIndex(2));
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue, green }, cols: 7, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[]
            {
                (3, 0, blue.Id), (4, 0, blue.Id),
                (5, 0, green.Id), (6, 0, green.Id),
            },
            seed: 0, aiPacer: pacer,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        pacer.DrainAll();

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(2, 0)));
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0)));
        Assert.Equal(blue.Id, h.Session.PendingDefeatScreen);
        Assert.True(h.Controller.EndgamePauseActive);
        pacer.DrainAll();
        h.Map.SimulateOffGridClick(HexCoord.FromOffset(9, 9));
        h.Hud.ClickDefeatContinue();
        h.Hud.ClickEndTurn();
        pacer.DrainAll();

        h.Controller.BeginReplay();
        var heldSamples = new List<bool>();
        var chromeSamples = new List<bool>();
        var pauseSamples = new List<bool>();
        h.Controller.ReplayBeatPreviewing += _ =>
        {
            heldSamples.Add(h.Hud.EndgameOverlaysHeld);
            chromeSamples.Add(h.Hud.ChromeHidden);
            pauseSamples.Add(h.Controller.EndgamePauseActive);
        };
        pacer.DrainAll();

        Assert.False(h.Controller.IsReplayMode);
        Assert.NotEmpty(heldSamples);
        Assert.All(heldSamples, Assert.True);
        Assert.All(chromeSamples, Assert.False);
        Assert.All(pauseSamples, Assert.False);
        // No winner: the replay ran out of beats, so the hold is released
        // and nothing is paused.
        Assert.Null(h.Session.Winner);
        Assert.False(h.Controller.EndgamePauseActive);
        Assert.False(h.Hud.EndgameOverlaysHeld);
        Assert.False(h.Hud.ChromeHidden);
        Assert.False(h.Hud.EndgameContinueHintShown);
    }

    // --- Sounds -----------------------------------------------------------

    [Fact]
    public void AiWin_OverHuman_PlaysLossCue()
    {
        // Mirrors AiWin_DoesNotFireGameWonSound: Red (AI) captures Blue's
        // (human) last tile during StartGame's AI run.
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (4, 0, blue.Id) },
            seed: 1,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant =
                new Unit(red.Id));

        Assert.Equal(red.Id, h.Session.Winner);
        Assert.Equal(0, h.Map.GameWonSoundCount);
        Assert.Equal(1, h.Map.BankruptcySoundCount);
        AssertPaused(h);
    }

    [Fact]
    public void AiWin_InstantBatch_LossCueBypassesBatchSilence()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (4, 0, blue.Id) },
            seed: 1,
            aiSilentMode: () => true,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant =
                new Unit(red.Id));

        Assert.Equal(red.Id, h.Session.Winner);
        Assert.Equal(1, h.Map.BankruptcySoundCount);
    }

    [Fact]
    public void AllAiRoster_NoLossCue()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (4, 0, blue.Id) },
            seed: 1,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(3, 0))!.Occupant =
                new Unit(red.Id));

        Assert.Equal(red.Id, h.Session.Winner);
        Assert.Equal(0, h.Map.BankruptcySoundCount);
        Assert.Equal(0, h.Map.GameWonSoundCount);
    }
}
