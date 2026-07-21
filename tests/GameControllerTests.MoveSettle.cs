// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Distance-scaled post-move settle delay ---------------------------
    //
    // The move-travel tween's duration scales with hex distance
    // (StepPacing.MoveTravelBaseMs), so the beat scheduled after a paced
    // move must stretch to cover it (StepPacing.MoveSettleDelayMs) — the
    // next beat's refresh rebuilds the unit layer and would kill an
    // in-flight tween. All three paced step machines (live AI, replay,
    // Automate) thread the executed move's distance into the post-execute
    // dispatch; non-move actions keep the baseline AiActionDelayMs.

    /// <summary>
    /// 8×2 grid with a long Red strip (0..4, 1) and a Red unit parked at
    /// (4,1) (placed pre-reconciliation so the capital lands elsewhere).
    /// Gives moves of hex distance 3-4 within Red's own territory.
    /// </summary>
    private static ControllerHarness BuildLongStripGame(
        IAiPacer pacer,
        bool redIsAi = false,
        Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, DeterministicRng, AiAction?>? aiChooser = null,
        Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, DeterministicRng, AiAction?>? automateChooser = null)
    {
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: redIsAi);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        return TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            cols: 8,
            rows: 2,
            ownerOverrides: new[]
            {
                (0, 1, red.Id), (1, 1, red.Id), (2, 1, red.Id),
                (3, 1, red.Id), (4, 1, red.Id),
            },
            aiChooser: aiChooser,
            automateChooser: automateChooser,
            aiPacer: pacer,
            beforeTerritories: g =>
                g.Get(HexCoord.FromOffset(4, 1))!.Occupant = new Unit(red.Id));
    }

    /// <summary>The strip's long-move destination: the far-end tile
    /// (0,1), or (1,1) if the capital reconciled onto (0,1). Both are
    /// distance ≥ 3 from the unit at (4,1).</summary>
    private static HexCoord LongMoveDestination(GameState state)
    {
        HexCoord farEnd = HexCoord.FromOffset(0, 1);
        return state.Grid.Get(farEnd)!.Occupant == null
            ? farEnd
            : HexCoord.FromOffset(1, 1);
    }

    [Fact]
    public void AiTurn_LongMove_SchedulesStretchedSettleDelay()
    {
        var pacer = new QueuedAiPacer();
        HexCoord from = HexCoord.FromOffset(4, 1);
        HexCoord? to = null;
        bool chosen = false;
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, HashSet<HexCoord> ru, DeterministicRng rng)
        {
            if (chosen) return null;
            chosen = true;
            to = LongMoveDestination(s);
            return new AiMoveAction(from, to.Value);
        }
        ControllerHarness h = BuildLongStripGame(pacer, redIsAi: true, aiChooser: Chooser);

        pacer.StepOne(); // StepAiPreview: choose + highlight, schedule execute
        pacer.StepOne(); // StepAiExecute: apply move, schedule next preview

        int dist = HexCoord.Distance(from, to!.Value);
        Assert.True(dist >= 3, $"fixture should yield a long move, got {dist}");
        Assert.Equal(StepPacing.MoveSettleDelayMs(dist), pacer.ScheduledDelaysMs[^1]);
    }

    [Fact]
    public void Replay_LongMoveBeat_SchedulesStretchedSettleDelay()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = BuildLongStripGame(pacer);
        HexCoord from = HexCoord.FromOffset(4, 1);
        HexCoord to = LongMoveDestination(h.State);

        // One click on an owned unit both selects its territory and picks
        // the unit up (a second click on the same tile would commit a
        // no-op self-move and pollute the recording).
        h.Map.SimulateClick(h.State.Grid.Get(from)!);
        Assert.Equal(from, h.Session.MoveSource);
        h.Map.SimulateClick(h.State.Grid.Get(to)!);   // long move
        pacer.DrainAll();

        Assert.IsType<ReplayMoveBeat>(Assert.Single(h.Controller.ReplayBeats));

        h.Controller.BeginReplay();
        pacer.StepOne(); // preview: Move (schedules execute after AiPreviewDelayMs)
        pacer.StepOne(); // execute: Move (schedules next beat — the settle)

        int dist = HexCoord.Distance(from, to);
        Assert.True(dist >= 3, $"fixture should yield a long move, got {dist}");
        Assert.Equal(StepPacing.MoveSettleDelayMs(dist), pacer.ScheduledDelaysMs[^1]);
    }

    [Fact]
    public void Automate_LongMove_SchedulesStretchedSettleDelay()
    {
        var pacer = new QueuedAiPacer();
        HexCoord from = HexCoord.FromOffset(4, 1);
        HexCoord? to = null;
        bool chosen = false;
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, HashSet<HexCoord> ru, DeterministicRng rng)
        {
            if (chosen) return null;
            chosen = true;
            to = LongMoveDestination(s);
            return new AiMoveAction(from, to.Value);
        }
        ControllerHarness h = BuildLongStripGame(pacer, automateChooser: Chooser);

        h.Hud.ClickAutomate(); // schedules StepAutomatePreview
        pacer.StepOne();       // preview: select + highlight, schedule execute
        pacer.StepOne();       // execute: apply move, schedule next preview

        int dist = HexCoord.Distance(from, to!.Value);
        Assert.True(dist >= 3, $"fixture should yield a long move, got {dist}");
        Assert.Equal(StepPacing.MoveSettleDelayMs(dist), pacer.ScheduledDelaysMs[^1]);
    }
}
