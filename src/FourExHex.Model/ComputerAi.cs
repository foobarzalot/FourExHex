using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Stepwise-greedy 1-ply AI (issue #26). For each call, picks the
/// largest non-exhausted owned territory then tries phases 1→4 in
/// order:
///   1. Free-unit captures, chops, grave clears (existing units).
///   2a. Combine-to-unlock (existing units; unlock filter required).
///   2b. Buy-and-combine-to-unlock (unlock filter required).
///   3. Buy-to-capture / buy-to-chop.
///   4a. Tower placement.
///   4b. Defensive reposition (existing units to border tiles).
///
/// Within each phase, units are iterated in power-then-coord order;
/// all candidates for the current unit are scored (clone+apply+score)
/// and the best one wins. The first phase × unit combination that
/// yields an action returns immediately — lower phases never run for
/// that territory in that call.
///
/// Phases 1 and 2a (free captures/chops/grave-clears and
/// combine-to-unlock) commit to their best legal candidate
/// REGARDLESS of delta sign — an offensive or unlock action is never
/// declined in favor of the status quo, even when exposing a border
/// makes the immediate delta ≤ 0 (chopping a tree off a defended tile,
/// for example). Phases 2b/3/4 keep the strictly-positive (> 0) gate:
/// 2b (buy-combine) and 3 (buy-capture/chop) are always-positive under
/// AiStateScorer anyway, and 4a/4b (towers, defensive repositions) are
/// genuinely optional, so doing nothing is a valid choice for them.
///
/// A territory is marked visited (exhausted) only when all phases
/// produce no candidate, ensuring that a later territory's action
/// can't retroactively unlock a skipped one.
/// </summary>
public static class ComputerAi
{
    /// <summary>
    /// Pick the best single action for <paramref name="forPlayer"/>
    /// under the stepwise-greedy phase ordering, or null if no
    /// unvisited territory has any legal positive-delta action.
    /// <paramref name="visitedCapitals"/> is mutated: territories
    /// with no positive-delta action in any phase are recorded so
    /// subsequent calls skip them.
    /// </summary>
    public static AiAction? ChooseNextAction(
        GameState state,
        PlayerId forPlayer,
        HashSet<HexCoord> visitedCapitals,
        Random rng)
    {
        long methodStart = Log.Stamp();
        long cloneTicks = 0, applyTicks = 0, scoreTicks = 0;
        int totalCandidates = 0, positiveCandidates = 0;
        int observedBestDelta = int.MinValue;
        AiActionKind? observedBestKind = null;

        long scoreT = Log.Stamp();
        int baseScore = AiStateScorer.Score(state, forPlayer);
        scoreTicks += Log.Stamp() - scoreT;

        foreach (Territory t in state.Territories
            .OrderByDescending(terr => terr.Size)
            .ThenBy(terr => terr.HasCapital ? terr.Capital!.Value : default(HexCoord)))
        {
            if (t.Owner != forPlayer) continue;
            if (!t.HasCapital) continue;
            if (visitedCapitals.Contains(t.Capital!.Value)) continue;
            Log.Debug(Log.LogCategory.Ai,
                $"[territory-order] capital={t.Capital} size={t.Size}");

            // Phase 1: free unit captures / chops / grave clears
            foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
            {
                Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                AiAction? p1 = BestPositiveDelta(
                    AiCommon.EnumeratePhase1ForUnit(unitCoord, unit, t, state),
                    int.MinValue, // never decline a free capture/chop/grave for the status quo
                    baseScore, forPlayer, state,
                    ref cloneTicks, ref applyTicks, ref scoreTicks,
                    ref totalCandidates, ref positiveCandidates,
                    ref observedBestDelta, ref observedBestKind);
                if (p1 != null) { EmitProfile(methodStart, cloneTicks, applyTicks, scoreTicks, totalCandidates); _ = rng; return p1; }
            }

            // Phase 2a: combine-to-unlock (existing units)
            foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
            {
                Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                AiAction? p2a = BestPositiveDelta(
                    AiCommon.EnumeratePhase2aForUnit(unitCoord, unit, t, state),
                    int.MinValue, // never decline an unlock-combine for the status quo
                    baseScore, forPlayer, state,
                    ref cloneTicks, ref applyTicks, ref scoreTicks,
                    ref totalCandidates, ref positiveCandidates,
                    ref observedBestDelta, ref observedBestKind);
                if (p2a != null) { EmitProfile(methodStart, cloneTicks, applyTicks, scoreTicks, totalCandidates); _ = rng; return p2a; }
            }

            // Phase 2b: buy-and-combine-to-unlock
            {
                AiAction? p2b = BestPositiveDelta(
                    AiCommon.EnumeratePhase2b(t, state),
                    0, // strictly-positive gate (buy-combine is always-positive anyway)
                    baseScore, forPlayer, state,
                    ref cloneTicks, ref applyTicks, ref scoreTicks,
                    ref totalCandidates, ref positiveCandidates,
                    ref observedBestDelta, ref observedBestKind);
                if (p2b != null) { EmitProfile(methodStart, cloneTicks, applyTicks, scoreTicks, totalCandidates); _ = rng; return p2b; }
            }

            // Phase 3: buy-to-capture / buy-to-chop
            {
                AiAction? p3 = BestPositiveDelta(
                    AiCommon.EnumeratePhase3(t, state),
                    0, // strictly-positive gate (buy-capture/chop is always-positive anyway)
                    baseScore, forPlayer, state,
                    ref cloneTicks, ref applyTicks, ref scoreTicks,
                    ref totalCandidates, ref positiveCandidates,
                    ref observedBestDelta, ref observedBestKind);
                if (p3 != null) { EmitProfile(methodStart, cloneTicks, applyTicks, scoreTicks, totalCandidates); _ = rng; return p3; }
            }

            // Phase 4a: tower placement
            {
                AiAction? p4a = BestPositiveDelta(
                    AiCommon.EnumeratePhase4Towers(t, state),
                    0, // optional defensive action — doing nothing is valid
                    baseScore, forPlayer, state,
                    ref cloneTicks, ref applyTicks, ref scoreTicks,
                    ref totalCandidates, ref positiveCandidates,
                    ref observedBestDelta, ref observedBestKind);
                if (p4a != null) { EmitProfile(methodStart, cloneTicks, applyTicks, scoreTicks, totalCandidates); _ = rng; return p4a; }
            }

            // Phase 4b: defensive repositions
            foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
            {
                Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                AiAction? p4b = BestPositiveDelta(
                    AiCommon.EnumeratePhase4bForUnit(unitCoord, unit, t, state),
                    0, // optional defensive action — doing nothing is valid
                    baseScore, forPlayer, state,
                    ref cloneTicks, ref applyTicks, ref scoreTicks,
                    ref totalCandidates, ref positiveCandidates,
                    ref observedBestDelta, ref observedBestKind);
                if (p4b != null) { EmitProfile(methodStart, cloneTicks, applyTicks, scoreTicks, totalCandidates); _ = rng; return p4b; }
            }

            // All phases exhausted for this territory.
            if (totalCandidates > 0)
            {
                Log.Debug(Log.LogCategory.Ai,
                    $"[heuristic] {forPlayer} territory={t.Capital} has {totalCandidates} candidates " +
                    $"({positiveCandidates} positive); best delta = " +
                    $"{observedBestDelta:+#;-#;0} ({observedBestKind?.ToString() ?? "?"})");
            }
            visitedCapitals.Add(t.Capital!.Value);
        }

        _ = rng;
        EmitProfile(methodStart, cloneTicks, applyTicks, scoreTicks, totalCandidates);
        return null;
    }

    /// <summary>
    /// Score every candidate and return the action with the best delta
    /// that strictly exceeds <paramref name="threshold"/>, or null if
    /// none does. Pass <c>0</c> for the strictly-positive gate (a
    /// candidate must improve the score to win); pass
    /// <see cref="int.MinValue"/> to force the best legal candidate
    /// regardless of sign — used by phases 1 and 2a so an offensive /
    /// unlock action is never declined in favor of the status quo.
    /// Ties resolve to the first-yielded candidate (preserving
    /// power-then-coord order). Accumulates profiling counters in the
    /// caller's locals via ref.
    /// </summary>
    private static AiAction? BestPositiveDelta(
        IEnumerable<AiCandidate> candidates,
        int threshold,
        int baseScore,
        PlayerId forPlayer,
        GameState state,
        ref long cloneTicks,
        ref long applyTicks,
        ref long scoreTicks,
        ref int totalCandidates,
        ref int positiveCandidates,
        ref int observedBestDelta,
        ref AiActionKind? observedBestKind)
    {
        AiAction? best = null;
        int bestDelta = threshold;

        foreach (AiCandidate candidate in candidates)
        {
            totalCandidates++;

            long cloneT = Log.Stamp();
            GameState clone = AiSimulator.Clone(state);
            cloneTicks += Log.Stamp() - cloneT;

            long applyT = Log.Stamp();
            AiSimulator.Apply(candidate.Action, clone);
            applyTicks += Log.Stamp() - applyT;

            long scoreT = Log.Stamp();
            int afterScore = AiStateScorer.Score(clone, forPlayer);
            scoreTicks += Log.Stamp() - scoreT;
            int delta = afterScore - baseScore;

            if (candidate.Action is AiBuildTowerAction bt)
                delta += AiStateScorer.BuildTowerBonus(bt.Destination, state, forPlayer);

            // Per-action defense incentive (#61): reward landing a defender
            // on a contested-border tile, scaled by its (capped) defense —
            // evaluated on the AFTER state since a capture flips ownership.
            // This is how mountains get sought: the +1 high-ground shows up
            // as higher Defense at the destination. Mirrors the tower bonus.
            switch (candidate.Action)
            {
                case AiMoveAction mv:
                    delta += AiStateScorer.BorderDefenseBonus(mv.Destination, clone, forPlayer);
                    break;
                case AiBuyUnitAction bu:
                    delta += AiStateScorer.BorderDefenseBonus(bu.Destination, clone, forPlayer);
                    break;
                case AiBuyCombineAction bc:
                    delta += AiStateScorer.BorderDefenseBonus(bc.CombineTarget, clone, forPlayer);
                    break;
            }

            if (delta > 0) positiveCandidates++;
            if (delta > observedBestDelta)
            {
                observedBestDelta = delta;
                observedBestKind = candidate.Kind;
            }
            if (delta > bestDelta)
            {
                bestDelta = delta;
                best = candidate.Action;
            }
        }

        return best;
    }

    private static void EmitProfile(
        long methodStart, long cloneTicks, long applyTicks, long scoreTicks, int totalCandidates)
    {
        long totalTicks = Log.Stamp() - methodStart;
        long ToMs(long t) => t * 1000L / Stopwatch.Frequency;
        long otherTicks = totalTicks - cloneTicks - applyTicks - scoreTicks;
        Log.Info(Log.LogCategory.Ai,
            $"[ai-prof] cand={totalCandidates} " +
            $"clone={ToMs(cloneTicks)}ms " +
            $"apply={ToMs(applyTicks)}ms " +
            $"score={ToMs(scoreTicks)}ms " +
            $"other={ToMs(otherTicks)}ms " +
            $"total={ToMs(totalTicks)}ms");
    }
}
