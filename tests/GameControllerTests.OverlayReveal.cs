// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Arrival-synced endgame overlays ----------------------------------
    //
    // When a MOVE ends the game (domination) or defeats the human (last
    // capital captured), the victory/defeat overlay must not pop while the
    // move's travel tween is still in flight. The controller latches
    // IHudView.SetEndgameOverlaysHeld(true) before the beat's refresh
    // paints, and schedules a reveal (release + refresh) after the same
    // settle delay that paces the travel (StepPacing.MoveSettleDelayMs).
    // Non-move game-enders (e.g. a buy completing domination) keep the
    // inline paint — there is no travel to wait for.

    [Fact]
    public void AiMove_DefeatingHuman_HoldsDefeatOverlayUntilSettle()
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
        Assert.True(h.Hud.EndgameOverlaysHeld);
        int settle = StepPacing.MoveSettleDelayMs(1);
        Assert.Equal(settle, pacer.ScheduledDelaysMs[^1]);
        Assert.True(pacer.HasPending); // the scheduled reveal

        int refreshesBefore = h.Map.RefreshOccupantCount;
        pacer.StepOne();      // reveal: release the hold + refresh

        Assert.False(h.Hud.EndgameOverlaysHeld);
        Assert.True(h.Map.RefreshOccupantCount > refreshesBefore);
        Assert.False(pacer.HasPending); // AI stays paused on the overlay
    }

    [Fact]
    public void AiMove_DominationWin_HoldsVictoryOverlayUntilSettle()
    {
        // 4x1: Blue (AI) {(0,0),(1,0),(2,0)} with a Soldier at (2,0);
        // Red's lone tile (3,0) (capital-less singleton — Red is already
        // out of rotation). Capturing it completes domination.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        AiAction? scripted = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, HashSet<HexCoord> ru, DeterministicRng r)
        {
            AiAction? next = scripted;
            scripted = null;
            return next;
        }
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            defaultOwner: blue.Id,
            ownerOverrides: new[] { (3, 0, red.Id) },
            currentPlayerIndex: 1,
            seed: 0, aiChooser: Chooser, aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));

        pacer.StepOne();      // preview
        pacer.StepOne();      // execute: capture → domination → Winner set

        Assert.Equal(blue.Id, h.Session.Winner);
        Assert.True(h.Hud.EndgameOverlaysHeld);
        Assert.Equal(StepPacing.MoveSettleDelayMs(1), pacer.ScheduledDelaysMs[^1]);

        pacer.StepOne();      // reveal

        Assert.False(h.Hud.EndgameOverlaysHeld);
        Assert.False(pacer.HasPending);
    }

    [Fact]
    public void HumanMove_DominationWin_HoldsVictoryOverlayUntilSettle()
    {
        // 4x1: Red (human) {(0,0),(1,0),(2,0)} with a Soldier at (2,0);
        // Blue's lone tile (3,0). The click-move captures it → domination.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 4, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (3, 0, blue.Id) },
            seed: 0, aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(red.Id, UnitLevel.Soldier));
        pacer.DrainAll(); // discard startup scheduling

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(2, 0))); // select + pick up
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0))); // winning move

        Assert.Equal(red.Id, h.Session.Winner);
        Assert.True(h.Hud.EndgameOverlaysHeld);
        Assert.Equal(StepPacing.MoveSettleDelayMs(1), pacer.ScheduledDelaysMs[^1]);

        pacer.StepOne();      // reveal

        Assert.False(h.Hud.EndgameOverlaysHeld);
    }

    [Fact]
    public void AiBuy_DominationWin_PaintsOverlayInline()
    {
        // Same board as the AI domination test, but the game-ending
        // action is a BUY — no travel tween, so no hold: the overlay
        // paints inline on the execute beat.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        AiAction? scripted = null;
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
                scripted = new AiBuyUnitAction(cap, HexCoord.FromOffset(3, 0), UnitLevel.Recruit);
                return scripted;
            },
            aiPacer: pacer,
            suppressClaimVictory: false);

        pacer.StepOne();      // preview
        pacer.StepOne();      // execute: buy-capture → domination

        Assert.Equal(blue.Id, h.Session.Winner);
        Assert.False(h.Hud.EndgameOverlaysHeld); // painted inline, never held
        Assert.False(pacer.HasPending);          // no scheduled reveal
    }
}
