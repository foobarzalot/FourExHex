using System.Collections.Generic;

/// <summary>
/// Strategic category of an AI action, used by
/// <see cref="ComputerAi"/>'s scoring loop. Assigned when the action
/// is enumerated so downstream consumers never need to re-classify.
/// </summary>
public enum AiActionKind
{
    Capture,
    Chop,
    Combine,
    BuyCombine,
    Tower,
    Reposition,
}

/// <summary>
/// A legal, solvency-checked action paired with its strategic kind.
/// </summary>
public readonly record struct AiCandidate(AiAction Action, AiActionKind Kind);

/// <summary>
/// Shared AI plumbing: legality + solvency enumeration and helpers.
/// <see cref="ComputerAi"/> calls into <see cref="Enumerate"/> as its
/// single source of truth for "what moves are available" — it then
/// decides how to pick among them.
///
/// The enumeration here encodes the game-mechanical rules about what
/// a player *could* do (and what wouldn't bankrupt them); it says
/// nothing about what's *best*. That judgement lives in the AI
/// classes that consume this output.
/// </summary>
public static class AiCommon
{
    /// <summary>
    /// Minimum hex distance between two AI-built towers in the same
    /// territory. 3 means a gap of two tiles must lie between any two
    /// friendly towers — purely an AI heuristic to prevent redundant
    /// clustering on the same border. Humans aren't bound by it.
    /// </summary>
    public const int MinTowerSpacing = 3;

    /// <summary>
    /// True iff <paramref name="coord"/> is at least
    /// <see cref="MinTowerSpacing"/> hex distance from every existing
    /// same-territory tower. Used to filter AI tower-placement
    /// candidates so the AI doesn't cluster towers redundantly.
    /// </summary>
    public static bool MeetsAiTowerSpacing(HexCoord coord, Territory territory, HexGrid grid)
    {
        foreach (HexCoord c in territory.Coords)
        {
            if (c.Equals(coord)) continue;
            HexTile? other = grid.Get(c);
            if (other?.Occupant is not Tower) continue;
            if (HexCoord.Distance(coord, c) < MinTowerSpacing) return false;
        }
        return true;
    }

    /// <summary>
    /// Every legal, solvent AI action in <paramref name="territory"/>
    /// for the current game state. Results are tagged with an
    /// <see cref="AiActionKind"/> so callers can group / score
    /// without re-classifying. Self-combine moves (source == dest,
    /// which <see cref="MovementRules.ValidTargets"/> trivially
    /// includes because a unit can "combine with itself") are
    /// filtered out. Tower builds are restricted to border tiles
    /// AND spacing-checked via <see cref="MeetsAiTowerSpacing"/>.
    /// </summary>
    /// <summary>
    /// The owner's difficulty plus the territory's net
    /// income (income − upkeep), exactly as real play will charge it.
    /// Single helper shared by every enumerator so the solvency gates
    /// can't drift from each other or from <see cref="Treasury"/> /
    /// <see cref="UpkeepRules.ApplyUpkeepFor"/>.
    /// </summary>
    private static (Difficulty Difficulty, int NetBefore) EconomyBefore(
        Territory territory, GameState state)
    {
        Difficulty difficulty = state.Players[territory.Owner.Index].Difficulty;
        int income = IncomeRules.IncomeFor(territory, state.Grid);
        int upkeep = UpkeepRules.TotalUpkeepFor(territory, state.Grid, difficulty);
        return (difficulty, income - upkeep);
    }

    public static IEnumerable<AiCandidate> Enumerate(Territory territory, GameState state)
    {
        PlayerId owner = territory.Owner;
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        // Treasury is consulted by every solvency gate below. A
        // capital-less territory can't hold gold and can't collect
        // income — it dies on the next upkeep step regardless, so
        // SurvivesNextUpkeep(0, …) below will reject everything for
        // it, which is the right behavior.
        int gold = territory.HasCapital
            ? state.Treasury.GetGold(territory.Capital!.Value)
            : 0;

        // --- Move actions: capture, chop, or combine ---
        // Each gate asks the shared SurvivesNextUpkeep predicate
        // whether the post-action (gold, netIncome) pair clears
        // next upkeep. Move actions don't spend gold, so the gold
        // argument is unchanged. Units are iterated in power-then-
        // coord order (Commander → Recruit, lex-min within tier) so
        // ties resolve in favor of the strongest unit — same order
        // the human N-cycle uses. See issue #21.
        foreach (HexCoord coord in MovementRules.MovableUnitsInPowerOrder(territory, owner, state.Grid))
        {
            HexTile? tile = state.Grid.Get(coord);
            // Defensive: helper already filtered by owner +
            // !HasMovedThisTurn, but the unit could theoretically be
            // gone in a state we don't trust. Belt-and-braces.
            if (tile?.Unit == null) continue;

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
                        if (UpkeepRules.SurvivesNextUpkeep(gold, netBefore + 1))
                        {
                            yield return new AiCandidate(
                                new AiMoveAction(coord, target),
                                AiActionKind.Capture);
                        }
                        break;
                    case TargetKind.Chop:
                        if (UpkeepRules.SurvivesNextUpkeep(gold, netBefore + 1))
                        {
                            yield return new AiCandidate(
                                new AiMoveAction(coord, target),
                                AiActionKind.Chop);
                        }
                        break;
                    case TargetKind.Combine:
                        Unit destUnit = (Unit)targetTile.Occupant!;
                        UnitLevel combinedLevel = sourceUnit.Level.CombinedWith(destUnit.Level);
                        int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel, difficulty)
                                          - UpkeepRules.UpkeepFor(sourceUnit.Level, difficulty)
                                          - UpkeepRules.UpkeepFor(destUnit.Level, difficulty);
                        if (UpkeepRules.SurvivesNextUpkeep(gold, netBefore - upkeepDelta))
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
        // A buy spends `cost` gold and adds `upkeep_` upkeep. Capture
        // also gains +1 tile (+1 income). Each gate passes the
        // post-action (gold - cost, netBefore + Δ) pair through
        // SurvivesNextUpkeep. Buy-to-combine isn't considered.
        UnitLevel[] buyLevels = { UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander };
        foreach (UnitLevel level in buyLevels)
        {
            if (!PurchaseRules.CanAfford(territory, state.Treasury, level, difficulty)) continue;
            int upkeep_ = UpkeepRules.UpkeepFor(level, difficulty);
            int cost = PurchaseRules.CostFor(level, difficulty);
            bool captureSolvent = UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore + 1 - upkeep_);
            bool repositionSolvent = UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore - upkeep_);
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
        // equals netBefore; the action just drains TowerCost gold.
        // Only considered for border tiles — an interior tower defends
        // nothing — and AI-only spacing (MeetsAiTowerSpacing) prevents
        // redundant towers clustered on the same border.
        if (PurchaseRules.CanAffordTower(territory, state.Treasury, difficulty)
            && UpkeepRules.SurvivesNextUpkeep(gold - PurchaseRules.TowerCostFor(difficulty), netBefore))
        {
            foreach (HexCoord coord in territory.Coords)
            {
                HexTile? tile = state.Grid.Get(coord);
                if (tile == null) continue;
                if (!IsBorderTile(coord, state.Grid, owner)) continue;
                if (!PurchaseRules.IsValidTowerLocation(tile, territory, state.Grid)) continue;
                if (!MeetsAiTowerSpacing(coord, territory, state.Grid)) continue;
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
        Grave,
        Combine,
        Reposition,
    }

    /// <summary>
    /// Classify a move / buy destination tile relative to
    /// <paramref name="owner"/>: enemy color = capture, own tree = chop,
    /// own grave = grave (movement-consuming, like chop), own friendly
    /// unit = combine, anything else (empty own, capital, tower) =
    /// pure reposition.
    /// </summary>
    private static TargetKind ClassifyTarget(HexTile targetTile, PlayerId owner)
    {
        if (targetTile.Owner != owner) return TargetKind.Capture;
        if (targetTile.Occupant is Tree) return TargetKind.Chop;
        if (targetTile.Occupant is Grave) return TargetKind.Grave;
        if (targetTile.Occupant is Unit unit && unit.Owner == owner) return TargetKind.Combine;
        return TargetKind.Reposition;
    }

    /// <summary>
    /// True iff <paramref name="coord"/> has at least one neighbor
    /// whose tile exists on the grid and is owned by a different
    /// player than <paramref name="owner"/>. A tower on a non-border
    /// tile is pointless, so this gate prunes them before the AI
    /// wastes gold on interior fortifications.
    /// </summary>
    public static bool IsBorderTile(HexCoord coord, HexGrid grid, PlayerId owner)
    {
        foreach (HexCoord neighbor in coord.Neighbors())
        {
            HexTile? tile = grid.Get(neighbor);
            if (tile == null) continue;
            if (tile.Owner != owner) return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // Phase-specific enumeration (stepwise-greedy AI, issue #26)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Phase 1: captures, tree chops, and grave clears available to
    /// <paramref name="unitCoord"/>/<paramref name="unit"/> in
    /// <paramref name="territory"/>. All are movement-consuming and cost
    /// no gold. Solvency-gated (all three actions yield +1 income on the
    /// tile they resolve on).
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase1ForUnit(
        HexCoord unitCoord,
        Unit unit,
        Territory territory,
        GameState state)
    {
        // No solvency gate: captures, chops, and grave clears don't change
        // upkeep at all (the unit was already paying upkeep). They can only
        // improve the economic situation (+1 income tile for captures/chops).
        // Gating them was wrong — a bankrupt territory should still attack.
        List<HexCoord> targets = MovementRules.ValidTargets(
            unit.Level, territory, state.Grid, state.Territories);
        foreach (HexCoord target in targets)
        {
            if (target.Equals(unitCoord)) continue;
            HexTile? targetTile = state.Grid.Get(target);
            if (targetTile == null) continue;
            TargetKind kind = ClassifyTarget(targetTile, territory.Owner);
            if (kind == TargetKind.Capture)
                yield return new AiCandidate(new AiMoveAction(unitCoord, target), AiActionKind.Capture);
            else if (kind == TargetKind.Chop || kind == TargetKind.Grave)
                yield return new AiCandidate(new AiMoveAction(unitCoord, target), AiActionKind.Chop);
        }
    }

    /// <summary>
    /// Phase 2a: combine moves for <paramref name="unitCoord"/>/<paramref
    /// name="unit"/> that pass the unlock filter — the combined unit must
    /// reach a movement-consuming target that neither source individually
    /// could. No gold spent; solvency-gated on the upkeep delta of the
    /// combine.
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase2aForUnit(
        HexCoord unitCoord,
        Unit unit,
        Territory territory,
        GameState state)
    {
        int gold = territory.HasCapital ? state.Treasury.GetGold(territory.Capital!.Value) : 0;
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        List<HexCoord> targets = MovementRules.ValidTargets(
            unit.Level, territory, state.Grid, state.Territories);
        foreach (HexCoord target in targets)
        {
            if (target.Equals(unitCoord)) continue;
            HexTile? targetTile = state.Grid.Get(target);
            if (targetTile == null) continue;
            if (ClassifyTarget(targetTile, territory.Owner) != TargetKind.Combine) continue;

            Unit destUnit = (Unit)targetTile.Occupant!;
            UnitLevel combinedLevel = unit.Level.CombinedWith(destUnit.Level);
            int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel, difficulty)
                              - UpkeepRules.UpkeepFor(unit.Level, difficulty)
                              - UpkeepRules.UpkeepFor(destUnit.Level, difficulty);
            if (!UpkeepRules.SurvivesNextUpkeep(gold, netBefore - upkeepDelta)) continue;
            if (!UnlocksMovementConsumingTarget(unit.Level, destUnit.Level, territory, state)) continue;

            yield return new AiCandidate(new AiMoveAction(unitCoord, target), AiActionKind.Combine);
        }
    }

    /// <summary>
    /// Phase 2b: buy-and-combine candidates that pass the unlock filter.
    /// Emits <see cref="AiBuyCombineAction"/> for each affordable level ×
    /// unmoved friendly unit pair that unlocks a new movement-consuming target.
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase2b(
        Territory territory,
        GameState state)
    {
        if (!territory.HasCapital) yield break;
        int gold = state.Treasury.GetGold(territory.Capital!.Value);
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        UnitLevel[] levels = { UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander };
        foreach (UnitLevel level in levels)
        {
            if (!PurchaseRules.CanAfford(territory, state.Treasury, level, difficulty)) continue;
            int cost = PurchaseRules.CostFor(level, difficulty);
            int buyUpkeep = UpkeepRules.UpkeepFor(level, difficulty);

            foreach (HexCoord coord in territory.Coords)
            {
                Unit? existingUnit = state.Grid.Get(coord)?.Unit;
                if (existingUnit == null || existingUnit.HasMovedThisTurn) continue;

                UnitLevel combinedLevel = level.CombinedWith(existingUnit.Level);
                int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel, difficulty)
                                  - UpkeepRules.UpkeepFor(existingUnit.Level, difficulty)
                                  - buyUpkeep;
                if (!UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore - upkeepDelta)) continue;
                if (!UnlocksMovementConsumingTarget(level, existingUnit.Level, territory, state)) continue;

                yield return new AiCandidate(
                    new AiBuyCombineAction(territory.Capital!.Value, coord, level),
                    AiActionKind.BuyCombine);
            }
        }
    }

    /// <summary>
    /// Phase 3: buy-to-capture and buy-to-chop candidates — buys that land
    /// on movement-consuming targets (enemy tiles, trees, graves).
    /// Buy-reposition is excluded by design. Solvency-gated.
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase3(
        Territory territory,
        GameState state)
    {
        if (!territory.HasCapital) yield break;
        int gold = state.Treasury.GetGold(territory.Capital!.Value);
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        UnitLevel[] levels = { UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander };
        foreach (UnitLevel level in levels)
        {
            if (!PurchaseRules.CanAfford(territory, state.Treasury, level, difficulty)) continue;
            int cost = PurchaseRules.CostFor(level, difficulty);
            int levelUpkeep = UpkeepRules.UpkeepFor(level, difficulty);
            if (!UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore + 1 - levelUpkeep)) continue;

            List<HexCoord> targets = MovementRules.ValidTargets(
                level, territory, state.Grid, state.Territories);
            foreach (HexCoord target in targets)
            {
                HexTile? targetTile = state.Grid.Get(target);
                if (targetTile == null) continue;
                TargetKind kind = ClassifyTarget(targetTile, territory.Owner);
                if (kind == TargetKind.Capture)
                    yield return new AiCandidate(
                        new AiBuyUnitAction(territory.Capital!.Value, target, level),
                        AiActionKind.Capture);
                else if (kind == TargetKind.Chop || kind == TargetKind.Grave)
                    yield return new AiCandidate(
                        new AiBuyUnitAction(territory.Capital!.Value, target, level),
                        AiActionKind.Chop);
            }
        }
    }

    /// <summary>
    /// Phase 4a: tower placements — border tiles that pass
    /// <see cref="MeetsAiTowerSpacing"/> and the gold solvency gate.
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase4Towers(
        Territory territory,
        GameState state)
    {
        if (!territory.HasCapital) yield break;

        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);
        if (!PurchaseRules.CanAffordTower(territory, state.Treasury, difficulty)) yield break;

        int gold = state.Treasury.GetGold(territory.Capital!.Value);
        if (!UpkeepRules.SurvivesNextUpkeep(gold - PurchaseRules.TowerCostFor(difficulty), netBefore)) yield break;

        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = state.Grid.Get(coord);
            if (tile == null) continue;
            if (!IsBorderTile(coord, state.Grid, territory.Owner)) continue;
            if (!PurchaseRules.IsValidTowerLocation(tile, territory, state.Grid)) continue;
            if (!MeetsAiTowerSpacing(coord, territory, state.Grid)) continue;
            yield return new AiCandidate(
                new AiBuildTowerAction(territory.Capital!.Value, coord),
                AiActionKind.Tower);
        }
    }

    /// <summary>
    /// Phase 4b: defensive repositions for a single unit — moves to empty
    /// border tiles within the territory. No gold spent.
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase4bForUnit(
        HexCoord unitCoord,
        Unit unit,
        Territory territory,
        GameState state)
    {
        List<HexCoord> targets = MovementRules.ValidTargets(
            unit.Level, territory, state.Grid, state.Territories);
        foreach (HexCoord target in targets)
        {
            if (target.Equals(unitCoord)) continue;
            HexTile? targetTile = state.Grid.Get(target);
            if (targetTile == null) continue;
            if (ClassifyTarget(targetTile, territory.Owner) == TargetKind.Reposition
                && targetTile.Occupant == null
                && IsBorderTile(target, state.Grid, territory.Owner))
            {
                yield return new AiCandidate(
                    new AiMoveAction(unitCoord, target), AiActionKind.Reposition);
            }
        }
    }

    /// <summary>
    /// True iff combining a unit of <paramref name="sourceLevel"/> with
    /// a unit of <paramref name="destLevel"/> unlocks at least one
    /// movement-consuming target (capture, chop, grave) from
    /// <paramref name="territory"/> that was not reachable by either unit
    /// at its original level. Used by phase 2a and 2b.
    /// </summary>
    public static bool UnlocksMovementConsumingTarget(
        UnitLevel sourceLevel,
        UnitLevel destLevel,
        Territory territory,
        GameState state)
    {
        UnitLevel combinedLevel = sourceLevel.CombinedWith(destLevel);
        if (combinedLevel == sourceLevel && combinedLevel == destLevel) return false;

        var preCombine = new System.Collections.Generic.HashSet<HexCoord>(
            MovementConsumingTargets(sourceLevel, territory, state));
        foreach (HexCoord c in MovementConsumingTargets(destLevel, territory, state))
            preCombine.Add(c);

        foreach (HexCoord c in MovementConsumingTargets(combinedLevel, territory, state))
        {
            if (!preCombine.Contains(c)) return true;
        }
        return false;
    }

    private static IEnumerable<HexCoord> MovementConsumingTargets(
        UnitLevel level, Territory territory, GameState state)
    {
        foreach (HexCoord target in MovementRules.ValidTargets(
            level, territory, state.Grid, state.Territories))
        {
            HexTile? tile = state.Grid.Get(target);
            if (tile == null) continue;
            TargetKind kind = ClassifyTarget(tile, territory.Owner);
            if (kind == TargetKind.Capture || kind == TargetKind.Chop || kind == TargetKind.Grave)
                yield return target;
        }
    }
}
