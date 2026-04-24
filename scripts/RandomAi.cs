using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A deliberately unsophisticated AI that makes random legal moves
/// from a prioritized, solvency-filtered candidate pool. Delegates
/// enumeration to <see cref="AiCommon.Enumerate"/> so the heuristic AI
/// sees the same legality/solvency rules — this class only decides
/// *how* to pick, not *what* is legal.
///
/// Two design rules keep it from doing nothing useful:
///
/// 1. <b>Priority.</b> Valid actions are sorted into four buckets —
///    captures &gt; chops &gt; combines &gt; towers. The AI picks
///    uniformly at random from the highest non-empty bucket, so it
///    always commits to aggression when it has any, falls back to
///    leveling up (combining) when it's stuck, and only builds
///    towers when there's nothing else to do.
/// 2. <b>Multi-action turns.</b> A territory stays eligible ("not
///    visited") until a call finds zero valid actions in it, so a
///    single territory can execute many actions across consecutive
///    calls within one turn.
/// </summary>
public static class RandomAi
{
    /// <summary>
    /// Pick a single action for <paramref name="forPlayer"/> to take
    /// right now, or null if every owned territory has been
    /// exhausted this turn. The caller is responsible for
    /// maintaining <paramref name="visitedCapitals"/> across calls —
    /// the function reads and mutates it as a set of capital coords
    /// whose action pool is known to be empty.
    /// </summary>
    public static AiAction? ChooseNextAction(
        GameState state,
        Color forPlayer,
        HashSet<HexCoord> visitedCapitals,
        Random rng)
    {
        var candidates = new List<Territory>();
        foreach (Territory t in state.Territories)
        {
            if (t.Owner != forPlayer) continue;
            if (!t.HasCapital) continue;
            if (visitedCapitals.Contains(t.Capital!.Value)) continue;
            candidates.Add(t);
        }
        if (candidates.Count == 0) return null;

        Shuffle(candidates, rng);

        foreach (Territory t in candidates)
        {
            BucketedActions actions = BucketFor(t, state);
            if (actions.IsEmpty)
            {
                // Nothing more this territory can do; mark it so the
                // next call doesn't re-enumerate it this turn.
                visitedCapitals.Add(t.Capital!.Value);
                continue;
            }

            // Territory still has options — pick from its highest
            // non-empty priority bucket and leave it unvisited so
            // further calls this turn can keep acting on it.
            return actions.Pick(rng);
        }
        return null;
    }

    /// <summary>
    /// Bin the candidates from <see cref="AiCommon.Enumerate"/> by
    /// strategic kind. No legality decisions happen here — AiCommon
    /// has already filtered to legal, solvent actions — this is
    /// purely a sort into priority buckets.
    /// </summary>
    private static BucketedActions BucketFor(Territory territory, GameState state)
    {
        var bucket = new BucketedActions();
        foreach (AiCandidate c in AiCommon.Enumerate(territory, state))
        {
            switch (c.Kind)
            {
                case AiActionKind.Capture: bucket.Captures.Add(c.Action); break;
                case AiActionKind.Chop: bucket.Chops.Add(c.Action); break;
                case AiActionKind.Combine: bucket.Combines.Add(c.Action); break;
                case AiActionKind.Tower: bucket.Towers.Add(c.Action); break;
            }
        }
        return bucket;
    }

    /// <summary>
    /// Priority buckets used by <see cref="RandomAi"/>: the AI picks
    /// from the first non-empty bucket in order, uniformly at random
    /// within that bucket.
    /// </summary>
    private sealed class BucketedActions
    {
        public readonly List<AiAction> Captures = new();
        public readonly List<AiAction> Chops = new();
        public readonly List<AiAction> Combines = new();
        public readonly List<AiAction> Towers = new();

        public bool IsEmpty =>
            Captures.Count == 0 && Chops.Count == 0
            && Combines.Count == 0 && Towers.Count == 0;

        public AiAction Pick(Random rng)
        {
            if (Captures.Count > 0) return Captures[rng.Next(Captures.Count)];
            if (Chops.Count > 0) return Chops[rng.Next(Chops.Count)];
            if (Combines.Count > 0) return Combines[rng.Next(Combines.Count)];
            return Towers[rng.Next(Towers.Count)];
        }
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
