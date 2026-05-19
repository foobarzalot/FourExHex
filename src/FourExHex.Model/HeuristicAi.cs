using System;
using System.Collections.Generic;

/// <summary>
/// A 1-ply score-maximizing AI. For each legal candidate action
/// across every unvisited owned territory, it clones the current
/// game state, applies the action to the clone, and scores the
/// resulting board via <see cref="AiStateScorer.Score"/>. It then
/// returns the candidate whose simulated-post-score delta over the
/// current-score is highest.
///
/// Shares all legality/solvency rules with <see cref="RandomAi"/>
/// through <see cref="AiCommon.Enumerate"/> — this class owns no
/// rules, only the "which action is best?" decision. The visited-
/// capital set has the same meaning as for RandomAi (multi-action
/// turns): a territory is marked visited only when enumeration
/// finds zero candidates.
///
/// Determinism: the <see cref="Random"/> parameter is used solely
/// for tiebreaking at equal scores, so tests with a seeded RNG
/// still observe stable behavior.
/// </summary>
public static class HeuristicAi
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
        double baseScore = AiStateScorer.Score(state, forPlayer);

        AiAction? best = null;
        // Positive-delta threshold: the AI only picks an action
        // that strictly improves its position. If no candidate
        // does, we return null and the step machine ends the turn
        // — better to pass than to burn gold on a losing combine
        // or a useless tower.
        double bestDelta = 0.0;

        // Diagnostic counters for the stasis-signal log below.
        int totalCandidates = 0;
        int positiveCandidates = 0;
        double observedBestDelta = double.NegativeInfinity;
        AiActionKind? observedBestKind = null;

        foreach (Territory t in state.Territories)
        {
            if (t.Owner != forPlayer) continue;
            if (!t.HasCapital) continue;
            if (visitedCapitals.Contains(t.Capital!.Value)) continue;

            bool anyCandidate = false;
            foreach (AiCandidate candidate in AiCommon.Enumerate(t, state))
            {
                anyCandidate = true;
                totalCandidates++;

                GameState clone = AiSimulator.Clone(state);
                AiSimulator.Apply(candidate.Action, clone);
                double afterScore = AiStateScorer.Score(clone, forPlayer);
                double delta = afterScore - baseScore;

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
                $"{observedBestDelta:+0.0;-0.0;0.0} ({observedBestKind?.ToString() ?? "?"})");
        }

        // rng unused for now — the first-wins tiebreak at equal
        // positive deltas is deterministic, which is what we want
        // for test stability. Kept in signature to match the
        // AiChooser contract.
        _ = rng;

        return best;
    }
}
