using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Stepwise-greedy 1-ply AI. For each call, picks the
/// largest non-exhausted owned territory then tries phases 1→4 in
/// order:
///   1. Free-unit captures, chops, grave clears (existing units).
///   2a. Combine-to-unlock (existing units; unlock filter required).
///   2b. Buy-and-combine-to-unlock (unlock filter required).
///   3. Buy-to-capture / buy-to-chop.
///   4a. Tower placement.
///   4b. Defensive reposition (existing units to border tiles).
///
/// Vikings (forPlayer == <see cref="PlayerId.None"/>) run phase 1 only,
/// restricted to captures: they have no economy (2a–4a) and never make a
/// defensive-only move (4b) — a landed raider with no capture holds.
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
        var prof = new AiSearchProfile();

        long scoreT = Log.Stamp();
        int baseScore = AiStateScorer.Score(state, forPlayer);
        prof.ScoreTicks += Log.Stamp() - scoreT;

        foreach (Territory t in state.Territories
            .OrderByDescending(terr => terr.Size)
            .ThenBy(terr => TerritoryLookup.AnchorCoord(terr)))
        {
            if (t.Owner != forPlayer) continue;
            // Vikings (forPlayer == None) drive capital-less neutral
            // territories; every other player needs a live capital.
            if (!t.HasCapital && !forPlayer.IsNone) continue;
            HexCoord anchor = TerritoryLookup.AnchorCoord(t);
            if (visitedCapitals.Contains(anchor)) continue;
            Log.Debug(Log.LogCategory.Ai,
                $"[territory-order] capital={anchor} size={t.Size}");

            // Phase 1: free unit captures / chops / grave clears.
            // Vikings capture only — an own-territory tree/grave is harmless
            // to an upkeep-free force, so they never chop "for its own sake"
            // (an enemy tile carrying a tree still classifies as Capture).
            foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
            {
                Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                IEnumerable<AiCandidate> p1Candidates =
                    AiCommon.EnumeratePhase1ForUnit(unitCoord, unit, t, state);
                if (forPlayer.IsNone)
                {
                    p1Candidates = p1Candidates.Where(c => c.Kind == AiActionKind.Capture);
                }
                // never decline a free capture/chop/grave for the status quo
                AiAction? p1 = TryPhase(p1Candidates,
                    int.MinValue, methodStart, baseScore, forPlayer, state, prof);
                if (p1 != null) { _ = rng; return p1; }
            }

            // Phases 2a–4b run only for real players. 2a–4a are the economy:
            // combines and gold spends — vikings have neither (no combining,
            // no capital → no treasury). 4b is defense — vikings are a pure
            // raiding force and never make a defensive-only move: a landed
            // raider with no capture holds.
            if (!forPlayer.IsNone)
            {
                // Phase 2a: combine-to-unlock (existing units)
                foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
                {
                    Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                    // never decline an unlock-combine for the status quo
                    AiAction? p2a = TryPhase(AiCommon.EnumeratePhase2aForUnit(unitCoord, unit, t, state),
                        int.MinValue, methodStart, baseScore, forPlayer, state, prof);
                    if (p2a != null) { _ = rng; return p2a; }
                }

                // Phase 2b: buy-and-combine-to-unlock (strictly-positive gate)
                {
                    AiAction? p2b = TryPhase(AiCommon.EnumeratePhase2b(t, state),
                        0, methodStart, baseScore, forPlayer, state, prof);
                    if (p2b != null) { _ = rng; return p2b; }
                }

                // Phase 3: buy-to-capture / buy-to-chop (strictly-positive gate)
                {
                    AiAction? p3 = TryPhase(AiCommon.EnumeratePhase3(t, state),
                        0, methodStart, baseScore, forPlayer, state, prof);
                    if (p3 != null) { _ = rng; return p3; }
                }

                // Phase 4a: tower placement (optional — doing nothing is valid)
                {
                    AiAction? p4a = TryPhase(AiCommon.EnumeratePhase4Towers(t, state),
                        0, methodStart, baseScore, forPlayer, state, prof);
                    if (p4a != null) { _ = rng; return p4a; }
                }

                // Phase 4b: defensive repositions (optional — doing nothing is valid)
                foreach (HexCoord unitCoord in MovementRules.MovableUnitsInPowerOrder(t, forPlayer, state.Grid))
                {
                    Unit unit = state.Grid.Get(unitCoord)!.Unit!;
                    AiAction? p4b = TryPhase(AiCommon.EnumeratePhase4bForUnit(unitCoord, unit, t, state),
                        0, methodStart, baseScore, forPlayer, state, prof);
                    if (p4b != null) { _ = rng; return p4b; }
                }
            }
            else
            {
                Log.Debug(Log.LogCategory.Ai,
                    $"[viking-hold] territory={anchor} — no capture, holding (phase 4b skipped)");
            }

            // All phases exhausted for this territory.
            if (prof.TotalCandidates > 0)
            {
                Log.Debug(Log.LogCategory.Ai,
                    $"[heuristic] {forPlayer} territory={anchor} has {prof.TotalCandidates} candidates " +
                    $"({prof.PositiveCandidates} positive); best delta = " +
                    $"{prof.ObservedBestDelta:+#;-#;0} ({prof.ObservedBestKind?.ToString() ?? "?"})");
            }
            visitedCapitals.Add(anchor);
        }

        _ = rng;
        EmitProfile(methodStart, prof.CloneTicks, prof.ApplyTicks, prof.ScoreTicks, prof.TotalCandidates);
        return null;
    }

    /// <summary>Run one phase's candidate search and, when it yields an action,
    /// emit the per-action profiling line before returning it. Returns null when
    /// the phase produced no winning candidate. Accumulates counters into
    /// <paramref name="prof"/> (shared across all phases of this call).</summary>
    private static AiAction? TryPhase(
        IEnumerable<AiCandidate> candidates, int threshold,
        long methodStart, int baseScore, PlayerId forPlayer, GameState state, AiSearchProfile prof)
    {
        AiAction? action = BestPositiveDelta(candidates, threshold, baseScore, forPlayer, state, prof);
        if (action != null)
            EmitProfile(methodStart, prof.CloneTicks, prof.ApplyTicks, prof.ScoreTicks, prof.TotalCandidates);
        return action;
    }

    /// <summary>Mutable per-<see cref="ChooseNextAction"/> search accumulators —
    /// timing buckets, candidate counts, and the best observed delta — passed
    /// through every phase so they accumulate across the whole call.</summary>
    private sealed class AiSearchProfile
    {
        public long CloneTicks;
        public long ApplyTicks;
        public long ScoreTicks;
        public int TotalCandidates;
        public int PositiveCandidates;
        public int ObservedBestDelta = int.MinValue;
        public AiActionKind? ObservedBestKind;
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
        AiSearchProfile prof)
    {
        AiAction? best = null;
        int bestDelta = threshold;

        foreach (AiCandidate candidate in candidates)
        {
            prof.TotalCandidates++;

            long cloneT = Log.Stamp();
            GameState clone = AiSimulator.Clone(state);
            prof.CloneTicks += Log.Stamp() - cloneT;

            long applyT = Log.Stamp();
            AiSimulator.Apply(candidate.Action, clone);
            prof.ApplyTicks += Log.Stamp() - applyT;

            long scoreT = Log.Stamp();
            int afterScore = AiStateScorer.Score(clone, forPlayer);
            prof.ScoreTicks += Log.Stamp() - scoreT;
            int delta = afterScore - baseScore;

            if (candidate.Action is AiBuildTowerAction bt)
                delta += AiStateScorer.BuildTowerBonus(bt.Destination, state, forPlayer);
            // Rising Tides: reward moving a unit off a tile that will
            // submerge this turn (and never onto one), so the defensive phase
            // evacuates a unit that would otherwise sit still and drown.
            if (candidate.Action is AiMoveAction mv)
                delta += AiStateScorer.EvacuationBonus(mv, state, forPlayer);

            if (delta > 0) prof.PositiveCandidates++;
            if (delta > prof.ObservedBestDelta)
            {
                prof.ObservedBestDelta = delta;
                prof.ObservedBestKind = candidate.Kind;
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
