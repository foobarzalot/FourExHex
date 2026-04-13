using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A deliberately unsophisticated AI that makes uniformly random legal
/// moves from a filtered candidate pool. Its only strategic rule is
/// refusing to bankrupt itself: any action that would leave its
/// containing territory with <c>income &lt; upkeep</c> after applying
/// the change is skipped. The random AI visits each owned multi-hex
/// territory at most once per turn, picks a single random valid
/// action in each (or skips if none), and then ends its turn.
///
/// Valid actions:
///   - Move an existing unit to a capture or to a tree-chop target.
///   - Move an existing unit onto a friendly unit to combine them,
///     subject to a per-combine upkeep-delta solvency check.
///   - Buy a peasant and place it for a capture or a tree chop.
///   - Build a tower on an empty own-territory tile.
/// Never: pure reposition, or anything in a territory it has already
/// visited this turn.
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
        // hasn't been visited yet.
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

        // Walk the shuffled list. Mark each territory visited as we go.
        // Return the first action we find; if a territory has no
        // valid actions, skip to the next.
        foreach (Territory t in candidates)
        {
            visitedCapitals.Add(t.Capital!.Value);

            List<AiAction> actions = EnumerateValidActions(t, state);
            if (actions.Count == 0) continue;

            return actions[rng.Next(actions.Count)];
        }
        return null;
    }

    /// <summary>
    /// Enumerate every legal-for-the-random-AI action in
    /// <paramref name="territory"/>. Applies the net-balance rule
    /// per action type so no returned action leaves the territory
    /// with <c>income &lt; upkeep</c> next turn.
    /// </summary>
    private static List<AiAction> EnumerateValidActions(Territory territory, GameState state)
    {
        var actions = new List<AiAction>();
        Color owner = territory.Owner;

        int income = TreeRules.CountNonTreeTiles(territory, state.Grid);
        int upkeep = UpkeepRules.TotalUpkeepFor(territory, state.Grid);
        int netBefore = income - upkeep;

        // --- Move actions: capture, tree chop, or combine --------------
        // Capture / chop: +1 income, 0 upkeep change. Post-net is
        // netBefore + 1, requirement netBefore >= -1.
        // Combine: 0 income change, upkeep delta = upkeep(combined)
        // - upkeep(source) - upkeep(destination). Requirement
        // netBefore - upkeepDelta >= 0 (i.e. netBefore >= upkeepDelta).
        // Each candidate target is evaluated against the appropriate
        // solvency gate — the AI won't combine itself into
        // bankruptcy and won't attempt a capture from an already
        // too-deep hole.
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

                if (IsCaptureOrChopTarget(target, owner, state.Grid))
                {
                    if (netBefore + 1 >= 0)
                    {
                        actions.Add(new AiMoveAction(coord, target));
                    }
                }
                else if (targetTile.Occupant is Unit destUnit && destUnit.Owner == owner)
                {
                    // Combine. MovementRules.ValidTargets already
                    // filtered to combine-legal pairs (sum <= Baron),
                    // so CombinedWith is safe to call.
                    UnitLevel combinedLevel = sourceUnit.Level.CombinedWith(destUnit.Level);
                    int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel)
                                      - UpkeepRules.UpkeepFor(sourceUnit.Level)
                                      - UpkeepRules.UpkeepFor(destUnit.Level);
                    if (netBefore - upkeepDelta >= 0)
                    {
                        actions.Add(new AiMoveAction(coord, target));
                    }
                }
                // Pure reposition (empty own tile, no tree) is skipped.
            }
        }

        // --- Buy actions: buy-capture or buy-chop ----------------------
        // A fresh peasant adds +1 income (captured tile or cleared tree)
        // and +2 upkeep, so post-net = netBefore - 1. Requirement:
        // netBefore >= 1. Also requires 10g in the treasury.
        if (PurchaseRules.CanAffordPeasant(territory, state.Treasury)
            && netBefore - 1 >= 0)
        {
            List<HexCoord> targets = MovementRules.ValidTargets(
                UnitLevel.Peasant, territory, state.Grid, state.Territories);
            foreach (HexCoord target in targets)
            {
                if (IsCaptureOrChopTarget(target, owner, state.Grid))
                {
                    actions.Add(new AiBuyUnitAction(territory.Capital!.Value, target));
                }
            }
        }

        // --- Build-tower actions ---------------------------------------
        // A tower has no upkeep and doesn't change the tile's income
        // contribution (only trees block income), so post-net equals
        // netBefore. Requirement: netBefore >= 0 and 15g available.
        if (PurchaseRules.CanAffordTower(territory, state.Treasury)
            && netBefore >= 0)
        {
            foreach (HexCoord coord in territory.Coords)
            {
                HexTile? tile = state.Grid.Get(coord);
                if (tile == null) continue;
                if (tile.Occupant != null) continue;
                actions.Add(new AiBuildTowerAction(territory.Capital!.Value, coord));
            }
        }

        return actions;
    }

    /// <summary>
    /// True iff <paramref name="target"/> is either an enemy tile
    /// (would be captured) or a same-color tile currently holding a
    /// <see cref="Tree"/> (would be chopped). Both cases convert the
    /// tile into an income-producing own tile.
    /// </summary>
    private static bool IsCaptureOrChopTarget(HexCoord target, Color owner, HexGrid grid)
    {
        HexTile? tile = grid.Get(target);
        if (tile == null) return false;

        if (tile.Color != owner) return true; // capture
        return tile.Occupant is Tree;         // chop
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
