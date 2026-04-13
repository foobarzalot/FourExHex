using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A deliberately unsophisticated AI that makes random legal moves
/// from a prioritized, solvency-filtered candidate pool. Two design
/// rules keep it from doing nothing useful:
///
/// 1. <b>Solvency.</b> Any action that would leave its containing
///    territory with <c>income &lt; upkeep</c> next turn is skipped.
/// 2. <b>Priority.</b> Valid actions are sorted into four buckets —
///    captures &gt; chops &gt; combines &gt; towers. The AI picks
///    uniformly at random from the highest non-empty bucket, so it
///    always commits to aggression when it has any, falls back to
///    leveling up (combining) when it's stuck, and only builds
///    towers when there's nothing else to do.
///
/// The AI walks each owned multi-hex territory per call; a
/// territory stays eligible ("not visited") until a call finds zero
/// valid actions in it, so a single territory can execute many
/// actions across consecutive calls within one turn (move/capture
/// with one unit, then combine the rest, then build a tower on a
/// border tile). Tower builds are further restricted to tiles
/// adjacent to enemy territory — a tower in the interior of an
/// unthreatened blob does nothing but bleed gold.
///
/// Valid actions:
///   - Move an existing unit to a capture or tree-chop target.
///   - Move an existing unit onto a friendly unit to combine them,
///     subject to a per-combine upkeep-delta solvency check.
///   - Buy a peasant and place it for a capture or a tree chop.
///   - Build a tower on an empty border own tile.
/// Never: pure reposition, buy-to-combine, buy-to-empty, or
/// anything in a territory whose action pool is already empty this
/// turn.
/// </summary>
public static class RandomAi
{
    /// <summary>
    /// Pick a single action for <paramref name="forPlayer"/> to take
    /// right now, or null if every owned territory has been visited
    /// or has no valid actions. The caller is responsible for
    /// maintaining <paramref name="visitedCapitals"/> across calls —
    /// the function reads and mutates it as a set of capital coords
    /// that have already been looked at this turn.
    /// </summary>
    public static AiAction? ChooseNextAction(
        GameState state,
        Color forPlayer,
        HashSet<HexCoord> visitedCapitals,
        Random rng)
    {
        // Gather every multi-hex territory this player owns that
        // hasn't been visited yet. Visited now means "exhausted this
        // turn" rather than "already acted this turn" — under
        // multi-action semantics a territory stays eligible until
        // enumeration finds nothing to do there.
        var candidates = new List<Territory>();
        foreach (Territory t in state.Territories)
        {
            if (t.Owner != forPlayer) continue;
            if (!t.HasCapital) continue;
            if (visitedCapitals.Contains(t.Capital!.Value)) continue;
            candidates.Add(t);
        }
        if (candidates.Count == 0) return null;

        // Random order so turn-to-turn behavior isn't biased by
        // territory insertion order.
        Shuffle(candidates, rng);

        foreach (Territory t in candidates)
        {
            BucketedActions actions = EnumerateValidActions(t, state);
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
    /// Action buckets emitted by <see cref="EnumerateValidActions"/>.
    /// The AI picks from the first non-empty bucket in priority
    /// order: captures &gt; chops &gt; combines &gt; towers. Within a
    /// bucket the pick is uniform random.
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

    /// <summary>
    /// Enumerate every legal-for-the-random-AI action in
    /// <paramref name="territory"/>, sorted into priority buckets.
    /// Applies per-action solvency rules so no returned action
    /// leaves the territory with <c>income &lt; upkeep</c> next
    /// turn. Tower builds are further restricted to border tiles —
    /// tiles that already have at least one enemy-colored neighbor
    /// — so the AI never sinks gold into defending a tile nothing
    /// threatens.
    /// </summary>
    private static BucketedActions EnumerateValidActions(Territory territory, GameState state)
    {
        var bucket = new BucketedActions();
        Color owner = territory.Owner;

        int income = TreeRules.CountNonTreeTiles(territory, state.Grid);
        int upkeep = UpkeepRules.TotalUpkeepFor(territory, state.Grid);
        int netBefore = income - upkeep;

        // --- Move actions: capture, chop, or combine -------------------
        // Capture / chop: +1 income, 0 upkeep change. Post-net is
        // netBefore + 1, requirement netBefore >= -1.
        // Combine: 0 income change, upkeep delta = upkeep(combined)
        // - upkeep(source) - upkeep(destination). Requirement
        // netBefore - upkeepDelta >= 0.
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = state.Grid.Get(coord);
            if (tile?.Unit == null) continue;
            if (tile.Unit.HasMovedThisTurn) continue;

            Unit sourceUnit = tile.Unit;
            List<HexCoord> targets = MovementRules.ValidTargets(
                sourceUnit.Level, territory, state.Grid, state.Territories);

            foreach (HexCoord target in targets)
            {
                // ValidTargets lists the source's own tile among its
                // combine targets (P.CanCombineWith(P) is trivially
                // true for the unit against itself). Skip self so we
                // don't emit a bogus self-combine AiMoveAction.
                if (target.Equals(coord)) continue;

                HexTile? targetTile = state.Grid.Get(target);
                if (targetTile == null) continue;

                TargetKind kind = ClassifyTarget(targetTile, owner);
                switch (kind)
                {
                    case TargetKind.Capture:
                        if (netBefore + 1 >= 0)
                        {
                            bucket.Captures.Add(new AiMoveAction(coord, target));
                        }
                        break;
                    case TargetKind.Chop:
                        if (netBefore + 1 >= 0)
                        {
                            bucket.Chops.Add(new AiMoveAction(coord, target));
                        }
                        break;
                    case TargetKind.Combine:
                        // ValidTargets already filtered to
                        // combine-legal pairs (sum <= Baron), so
                        // CombinedWith is safe.
                        Unit destUnit = (Unit)targetTile.Occupant!;
                        UnitLevel combinedLevel = sourceUnit.Level.CombinedWith(destUnit.Level);
                        int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel)
                                          - UpkeepRules.UpkeepFor(sourceUnit.Level)
                                          - UpkeepRules.UpkeepFor(destUnit.Level);
                        if (netBefore - upkeepDelta >= 0)
                        {
                            bucket.Combines.Add(new AiMoveAction(coord, target));
                        }
                        break;
                    // Reposition: skip.
                }
            }
        }

        // --- Buy actions: buy-capture or buy-chop ----------------------
        // A fresh peasant adds +1 income and +2 upkeep, so post-net =
        // netBefore - 1. Requirement: netBefore >= 1. Also requires
        // 10g. Buy-to-combine and buy-to-empty are not considered.
        if (PurchaseRules.CanAffordPeasant(territory, state.Treasury)
            && netBefore - 1 >= 0)
        {
            List<HexCoord> targets = MovementRules.ValidTargets(
                UnitLevel.Peasant, territory, state.Grid, state.Territories);
            foreach (HexCoord target in targets)
            {
                HexTile? targetTile = state.Grid.Get(target);
                if (targetTile == null) continue;

                TargetKind kind = ClassifyTarget(targetTile, owner);
                if (kind == TargetKind.Capture)
                {
                    bucket.Captures.Add(new AiBuyUnitAction(territory.Capital!.Value, target));
                }
                else if (kind == TargetKind.Chop)
                {
                    bucket.Chops.Add(new AiBuyUnitAction(territory.Capital!.Value, target));
                }
            }
        }

        // --- Build-tower actions ---------------------------------------
        // A tower has no upkeep and doesn't change income (only trees
        // do), so post-net equals netBefore: requires netBefore >= 0
        // and 15g. In addition, towers are only considered for border
        // tiles — own tiles that have at least one enemy-colored
        // neighbor. A tower on a fully-interior tile does nothing
        // but raise the tile's defense for no threatened approach,
        // which is why 6-AI games used to drown in useless towers.
        if (PurchaseRules.CanAffordTower(territory, state.Treasury)
            && netBefore >= 0)
        {
            foreach (HexCoord coord in territory.Coords)
            {
                HexTile? tile = state.Grid.Get(coord);
                if (tile == null) continue;
                if (tile.Occupant != null) continue;
                if (!IsBorderTile(coord, state.Grid, owner)) continue;
                bucket.Towers.Add(new AiBuildTowerAction(territory.Capital!.Value, coord));
            }
        }

        return bucket;
    }

    private enum TargetKind
    {
        Capture,
        Chop,
        Combine,
        Reposition,
    }

    /// <summary>
    /// Classify a move / buy destination tile relative to
    /// <paramref name="owner"/>: an enemy-colored tile is a capture,
    /// a same-color tile holding a Tree is a chop, a same-color tile
    /// holding a friendly Unit is a combine, and anything else
    /// (empty own tile, grave, capital, tower) is a pure reposition.
    /// </summary>
    private static TargetKind ClassifyTarget(HexTile targetTile, Color owner)
    {
        if (targetTile.Color != owner) return TargetKind.Capture;
        if (targetTile.Occupant is Tree) return TargetKind.Chop;
        if (targetTile.Occupant is Unit unit && unit.Owner == owner) return TargetKind.Combine;
        return TargetKind.Reposition;
    }

    /// <summary>
    /// True iff <paramref name="coord"/> has at least one neighbor
    /// whose tile exists on the grid and is a different color than
    /// <paramref name="owner"/>. Used to restrict tower builds to
    /// tiles that actually defend a potential enemy approach.
    /// </summary>
    private static bool IsBorderTile(HexCoord coord, HexGrid grid, Color owner)
    {
        foreach (HexCoord neighbor in coord.Neighbors())
        {
            HexTile? tile = grid.Get(neighbor);
            if (tile == null) continue;
            if (tile.Color != owner) return true;
        }
        return false;
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
