using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// A 1-ply score-maximizing AI. For each legal candidate action
/// across every unvisited owned territory, it clones the current
/// game state, applies the action to the clone, and scores the
/// resulting board via <see cref="AiStateScorer.Score"/>. It then
/// returns the candidate whose simulated-post-score delta over the
/// current-score is highest. This is the only AI in the game; a
/// <see cref="PlayerKind.Computer"/> slot is driven by it.
///
/// Draws all legality/solvency rules from <see cref="AiCommon.Enumerate"/>
/// — this class owns no rules, only the "which action is best?"
/// decision. The visited-capital set supports multi-action turns:
/// a territory is marked visited only when enumeration finds zero
/// candidates.
///
/// Determinism: the <see cref="Random"/> parameter is used solely
/// for tiebreaking at equal scores, so tests with a seeded RNG
/// still observe stable behavior.
/// </summary>
public static class ComputerAi
{
    /// <summary>
    /// Pick the highest-scoring single action for
    /// <paramref name="forPlayer"/>, or null if no unvisited
    /// territory has any legal actions left this turn. The
    /// <paramref name="visitedCapitals"/> set is mutated in place:
    /// any territory enumerated to an empty candidate list is
    /// recorded so subsequent calls can skip it.
    /// </summary>
    public static AiAction? ChooseNextAction(
        GameState state,
        PlayerId forPlayer,
        HashSet<HexCoord> visitedCapitals,
        Random rng)
    {
        // Profiling: per-call accumulators for the four hot-path
        // arms (Clone, Apply, Score, and bookkeeping/enumeration as
        // the implicit remainder). Surfaced as a single [ai-prof]
        // line at the end of the call; aggregate across a game by
        // grep+sum on the harness output. See issue #25.
        long methodStart = Log.Stamp();
        long cloneTicks = 0, applyTicks = 0, scoreTicks = 0;

        long scoreT = Log.Stamp();
        int baseScore = AiStateScorer.Score(state, forPlayer);
        scoreTicks += Log.Stamp() - scoreT;

        AiAction? best = null;
        // Positive-delta threshold: the AI only picks an action
        // that strictly improves its position. If no candidate
        // does, we return null and the step machine ends the turn
        // — better to pass than to burn gold on a losing combine
        // or a useless tower.
        int bestDelta = 0;

        // Diagnostic counters for the stasis-signal log below.
        int totalCandidates = 0;
        int positiveCandidates = 0;
        int observedBestDelta = int.MinValue;
        AiActionKind? observedBestKind = null;

        foreach (Territory t in state.Territories
            .OrderByDescending(terr => terr.Size)
            .ThenBy(terr => terr.HasCapital ? terr.Capital!.Value : default(HexCoord)))
        {
            if (t.Owner != forPlayer) continue;
            if (!t.HasCapital) continue;
            if (visitedCapitals.Contains(t.Capital!.Value)) continue;
            Log.Debug(Log.LogCategory.Ai,
                $"[territory-order] capital={t.Capital} size={t.Size}");

            bool anyCandidate = false;
            foreach (AiCandidate candidate in AiCommon.Enumerate(t, state))
            {
                anyCandidate = true;
                totalCandidates++;

                long cloneT = Log.Stamp();
                GameState clone = AiSimulator.Clone(state);
                cloneTicks += Log.Stamp() - cloneT;

                long applyT = Log.Stamp();
                AiSimulator.Apply(candidate.Action, clone);
                applyTicks += Log.Stamp() - applyT;

                long scoreT2 = Log.Stamp();
                int afterScore = AiStateScorer.Score(clone, forPlayer);
                scoreTicks += Log.Stamp() - scoreT2;
                int delta = afterScore - baseScore;

                // Tower placement is the one action whose value
                // doesn't show up in Score: a fresh tower has no
                // upkeep, no income effect, and (post the move to
                // a per-action bonus) no static term. Add the
                // BuildTowerBonus on top of the score delta so the
                // AI sees a reason to spend gold on towers.
                if (candidate.Action is AiBuildTowerAction bt)
                {
                    delta += AiStateScorer.BuildTowerBonus(bt.Destination, state, forPlayer);
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

            // Mark truly empty territories visited so we don't
            // re-enumerate them next call. Territories that had
            // candidates but none were strictly positive are NOT
            // marked visited: under multi-action semantics another
            // territory's action might change state in a way that
            // makes this one productive again. If nothing is
            // productive this call, we return null anyway and the
            // turn ends.
            if (!anyCandidate)
            {
                visitedCapitals.Add(t.Capital!.Value);
            }
        }

        if (best == null && totalCandidates > 0)
        {
            // The AI had options but every one was non-positive.
            // This is the stasis signal: legal actions exist but
            // the scorer thinks they all hurt. Log once per call.
            Log.Debug(Log.LogCategory.Ai,
                $"[heuristic] {forPlayer} has {totalCandidates} candidates " +
                $"({positiveCandidates} positive); best delta = " +
                $"{observedBestDelta:+#;-#;0} ({observedBestKind?.ToString() ?? "?"})");
        }

        // rng unused for now — the first-wins tiebreak at equal
        // positive deltas is deterministic, which is what we want
        // for test stability. Kept in signature to match the
        // AiChooser contract.
        _ = rng;

        // Profile breakdown: one line per ChooseNextAction call. The
        // `other` bucket is the implicit remainder — enumeration
        // (AiCommon.Enumerate's lazy work, interleaved with the
        // foreach body), bookkeeping, and the per-iteration
        // branching/allocation overhead. Per-call cost: a Stopwatch
        // tick read is ~10ns on M1; with thousands of candidates
        // per call we add tens of microseconds of measurement
        // noise, dwarfed by the millisecond-scale Clone/Score work
        // we're measuring. See #25.
        long totalTicks = Log.Stamp() - methodStart;
        long ToMs(long ticks) => ticks * 1000L / Stopwatch.Frequency;
        long otherTicks = totalTicks - cloneTicks - applyTicks - scoreTicks;
        Log.Info(Log.LogCategory.Ai,
            $"[ai-prof] cand={totalCandidates} " +
            $"clone={ToMs(cloneTicks)}ms " +
            $"apply={ToMs(applyTicks)}ms " +
            $"score={ToMs(scoreTicks)}ms " +
            $"other={ToMs(otherTicks)}ms " +
            $"total={ToMs(totalTicks)}ms");

        return best;
    }
}
