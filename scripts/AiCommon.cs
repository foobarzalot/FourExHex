using System.Collections.Generic;
using Godot;

/// <summary>
/// Strategic category of an AI action, used by both
/// <see cref="RandomAi"/>'s priority-bucket selector and
/// <see cref="HeuristicAi"/>'s scoring loop. Assigned when the action
/// is enumerated so downstream consumers never need to re-classify.
/// </summary>
public enum AiActionKind
{
    Capture,
    Chop,
    Combine,
    Tower,
    Reposition,
}

/// <summary>
/// A legal, solvency-checked action paired with its strategic kind.
/// </summary>
public readonly record struct AiCandidate(AiAction Action, AiActionKind Kind);

/// <summary>
/// Shared AI plumbing: legality + solvency enumeration and helpers.
/// Both the random and heuristic AIs call into <see cref="Enumerate"/>
/// as their single source of truth for "what moves are available" —
/// each class then decides how to pick among them.
///
/// The enumeration here encodes the game-mechanical rules about what
/// a player *could* do (and what wouldn't bankrupt them); it says
/// nothing about what's *best*. That judgement lives in the AI
/// classes that consume this output.
/// </summary>
public static class AiCommon
{
    /// <summary>
    /// Every legal, solvent AI action in <paramref name="territory"/>
    /// for the current game state. Results are tagged with an
    /// <see cref="AiActionKind"/> so callers can group / score
    /// without re-classifying. Self-combine moves (source == dest,
    /// which <see cref="MovementRules.ValidTargets"/> trivially
    /// includes because a unit can "combine with itself") are
    /// filtered out. Tower builds are restricted to border tiles.
    /// </summary>
    public static IEnumerable<AiCandidate> Enumerate(Territory territory, GameState state)
    {
        Color owner = territory.Owner;
        int income = TreeRules.CountIncomeProducingTiles(territory, state.Grid);
        int upkeep = UpkeepRules.TotalUpkeepFor(territory, state.Grid);
        int netBefore = income - upkeep;

        // --- Move actions: capture, chop, or combine ---
        // Capture / chop: +1 income, 0 upkeep change. Post-net is
        // netBefore + 1, requirement netBefore >= -1.
        // Combine: 0 income change, upkeep delta =
        // upkeep(combined) - upkeep(source) - upkeep(destination).
        // Requirement netBefore - upkeepDelta >= 0.
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
                // ValidTargets lists the source's own tile as a
                // combine target (P.CanCombineWith(P) is trivially
                // true for a unit against itself). Skip it.
                if (target.Equals(coord)) continue;

                HexTile? targetTile = state.Grid.Get(target);
                if (targetTile == null) continue;

                TargetKind kind = ClassifyTarget(targetTile, owner);
                switch (kind)
                {
                    case TargetKind.Capture:
                        if (netBefore + 1 >= 0)
                        {
                            yield return new AiCandidate(
                                new AiMoveAction(coord, target),
                                AiActionKind.Capture);
                        }
                        break;
                    case TargetKind.Chop:
                        if (netBefore + 1 >= 0)
                        {
                            yield return new AiCandidate(
                                new AiMoveAction(coord, target),
                                AiActionKind.Chop);
                        }
                        break;
                    case TargetKind.Combine:
                        Unit destUnit = (Unit)targetTile.Occupant!;
                        UnitLevel combinedLevel = sourceUnit.Level.CombinedWith(destUnit.Level);
                        int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel)
                                          - UpkeepRules.UpkeepFor(sourceUnit.Level)
                                          - UpkeepRules.UpkeepFor(destUnit.Level);
                        if (netBefore - upkeepDelta >= 0)
                        {
                            yield return new AiCandidate(
                                new AiMoveAction(coord, target),
                                AiActionKind.Combine);
                        }
                        break;
                    case TargetKind.Reposition:
                        // Repositioning a unit within friendly tiles
                        // doesn't change income or upkeep. Only worth
                        // enumerating for border destinations — moving
                        // to an interior tile gains nothing the scorer
                        // can see. Empty target only (capitals/towers/
                        // graves are filtered to Reposition by
                        // ClassifyTarget but aren't legal placement
                        // tiles for a moving unit, and ValidTargets
                        // already excludes them).
                        if (targetTile.Occupant == null
                            && IsBorderTile(target, state.Grid, owner))
                        {
                            yield return new AiCandidate(
                                new AiMoveAction(coord, target),
                                AiActionKind.Reposition);
                        }
                        break;
                }
            }
        }

        // --- Buy actions: buy-capture, buy-chop, or buy-reposition ---
        // Capture/chop add +1 income and +upkeep(level): post-net =
        // netBefore + 1 - upkeep(level), requires >= 0.
        // Reposition (placing onto an empty own border tile) gains no
        // tile, so post-net = netBefore - upkeep(level), requires >= 0
        // — strictly tighter than capture/chop. Buy-to-combine isn't
        // considered.
        UnitLevel[] buyLevels = { UnitLevel.Peasant, UnitLevel.Spearman, UnitLevel.Knight, UnitLevel.Baron };
        foreach (UnitLevel level in buyLevels)
        {
            if (!PurchaseRules.CanAfford(territory, state.Treasury, level)) continue;
            int upkeep_ = UpkeepRules.UpkeepFor(level);
            bool captureSolvent = netBefore + 1 - upkeep_ >= 0;
            bool repositionSolvent = netBefore - upkeep_ >= 0;
            if (!captureSolvent && !repositionSolvent) continue;

            List<HexCoord> buyTargets = MovementRules.ValidTargets(
                level, territory, state.Grid, state.Territories);
            foreach (HexCoord target in buyTargets)
            {
                HexTile? targetTile = state.Grid.Get(target);
                if (targetTile == null) continue;

                TargetKind kind = ClassifyTarget(targetTile, owner);
                if (kind == TargetKind.Capture && captureSolvent)
                {
                    yield return new AiCandidate(
                        new AiBuyUnitAction(territory.Capital!.Value, target, level),
                        AiActionKind.Capture);
                }
                else if (kind == TargetKind.Chop && captureSolvent)
                {
                    yield return new AiCandidate(
                        new AiBuyUnitAction(territory.Capital!.Value, target, level),
                        AiActionKind.Chop);
                }
                else if (kind == TargetKind.Reposition
                         && repositionSolvent
                         && targetTile.Occupant == null
                         && IsBorderTile(target, state.Grid, owner))
                {
                    yield return new AiCandidate(
                        new AiBuyUnitAction(territory.Capital!.Value, target, level),
                        AiActionKind.Reposition);
                }
            }
        }

        // --- Build-tower actions ---
        // Towers have no upkeep and don't change income, so post-net
        // equals netBefore: requires netBefore >= 0 and 15g. Only
        // considered for border tiles — an interior tower defends
        // nothing.
        if (PurchaseRules.CanAffordTower(territory, state.Treasury)
            && netBefore >= 0)
        {
            foreach (HexCoord coord in territory.Coords)
            {
                HexTile? tile = state.Grid.Get(coord);
                if (tile == null) continue;
                if (tile.Occupant != null) continue;
                if (!IsBorderTile(coord, state.Grid, owner)) continue;
                yield return new AiCandidate(
                    new AiBuildTowerAction(territory.Capital!.Value, coord),
                    AiActionKind.Tower);
            }
        }
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
    /// <paramref name="owner"/>: enemy color = capture, own color
    /// with a Tree = chop, own color with a friendly Unit = combine,
    /// anything else (empty own, own capital, own tower, grave) =
    /// pure reposition.
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
    /// <paramref name="owner"/>. A tower on a non-border tile is
    /// pointless, so this gate prunes them before the AI wastes
    /// gold on interior fortifications.
    /// </summary>
    public static bool IsBorderTile(HexCoord coord, HexGrid grid, Color owner)
    {
        foreach (HexCoord neighbor in coord.Neighbors())
        {
            HexTile? tile = grid.Get(neighbor);
            if (tile == null) continue;
            if (tile.Color != owner) return true;
        }
        return false;
    }
}
