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
    /// The owner's difficulty (for purchase-cost checks) plus the
    /// territory's net income (income − upkeep), exactly as real play will
    /// charge it. Single helper shared by every enumerator so the solvency
    /// gates can't drift from each other or from <see cref="Treasury"/> /
    /// <see cref="UpkeepRules.ApplyUpkeepFor"/>.
    /// </summary>
    private static (Difficulty Difficulty, int NetBefore) EconomyBefore(
        Territory territory, GameState state)
    {
        Difficulty difficulty = state.DifficultyOf(territory.Owner);
        int income = IncomeRules.IncomeFor(territory, state.Grid);
        int upkeep = UpkeepRules.TotalUpkeepFor(territory, state.Grid);
        return (difficulty, income - upkeep);
    }

    public static IEnumerable<AiCandidate> Enumerate(Territory territory, GameState state)
    {
        PlayerId owner = territory.Owner;
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        // Shared whole-board lookup for every ValidTargets call below —
        // the state is not mutated during enumeration.
        Dictionary<HexCoord, Territory> tileIndex = state.Territories.BuildTileIndex();

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
        // the human N-cycle uses.
        foreach (HexCoord coord in MovementRules.MovableUnitsInPowerOrder(territory, owner, state.Grid))
        {
            HexTile? tile = state.Grid.Get(coord);
            // Defensive: helper already filtered by owner +
            // !HasMovedThisTurn, but the unit could theoretically be
            // gone in a state we don't trust. Belt-and-braces.
            if (tile?.Unit == null) continue;

            Unit sourceUnit = tile.Unit;
            List<HexCoord> targets = MovementRules.ValidTargets(
                sourceUnit.Level, territory, state.Grid, state.Territories, tileIndex);

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
                        int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel)
                                          - UpkeepRules.UpkeepFor(sourceUnit.Level)
                                          - UpkeepRules.UpkeepFor(destUnit.Level);
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
            int upkeep_ = UpkeepRules.UpkeepFor(level);
            int cost = PurchaseRules.CostFor(level, difficulty);
            bool captureSolvent = UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore + 1 - upkeep_);
            bool repositionSolvent = UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore - upkeep_);
            if (!captureSolvent && !repositionSolvent) continue;

            List<HexCoord> buyTargets = MovementRules.ValidTargets(
                level, territory, state.Grid, state.Territories, tileIndex);
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
    // Phase-specific enumeration (stepwise-greedy AI)
    // -----------------------------------------------------------------------

    /// <summary>
    /// One (level, coord) entry per distinct unmoved-unit tier in
    /// <paramref name="territory"/>, levels ascending; each tier is
    /// represented by its first unit in
    /// <see cref="MovementRules.MovableUnitsWeakestFirst"/> order (lex-min
    /// coord). One representative per tier suffices for the capture
    /// phases: <see cref="MovementRules.ValidTargets"/> depends only on
    /// level + territory — never unit position — so same-level units have
    /// identical candidate sets, and scanning one unit per tier IS the
    /// "skip an exhausted tier" optimization.
    /// </summary>
    public static List<(UnitLevel Level, HexCoord Coord)> MovableUnitTiersWeakestFirst(
        Territory territory, PlayerId owner, HexGrid grid)
    {
        var tiers = new List<(UnitLevel Level, HexCoord Coord)>();
        UnitLevel? seen = null;
        foreach (HexCoord coord in MovementRules.MovableUnitsWeakestFirst(territory, owner, grid))
        {
            UnitLevel level = grid.Get(coord)!.Unit!.Level;
            if (seen == level) continue;
            seen = level;
            tiers.Add((level, coord));
        }
        return tiers;
    }

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
        GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        // No solvency gate: captures, chops, and grave clears don't change
        // upkeep at all (the unit was already paying upkeep). They can only
        // improve the economic situation (+1 income tile for captures/chops).
        // A bankrupt territory should still attack, so these are ungated.
        List<HexCoord> targets = MovementRules.ValidTargets(
            unit.Level, territory, state.Grid, state.Territories, tileIndex);
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
    /// combine. Targets must be unmoved: the combined unit inherits the
    /// destination's move state (<c>MovementRules.ResolveArrival</c>), so a
    /// combine into an exhausted unit would be unusable this turn while its
    /// upkeep increase bills immediately — strictly worse than deferring
    /// the same combine to next turn (mirrors phase 2b's exclusion).
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase2aForUnit(
        HexCoord unitCoord,
        Unit unit,
        Territory territory,
        GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        int gold = territory.HasCapital ? state.Treasury.GetGold(territory.Capital!.Value) : 0;
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        List<HexCoord> targets = MovementRules.ValidTargets(
            unit.Level, territory, state.Grid, state.Territories, tileIndex);
        foreach (HexCoord target in targets)
        {
            if (target.Equals(unitCoord)) continue;
            HexTile? targetTile = state.Grid.Get(target);
            if (targetTile == null) continue;
            if (ClassifyTarget(targetTile, territory.Owner) != TargetKind.Combine) continue;

            Unit destUnit = (Unit)targetTile.Occupant!;
            // Never combine into an exhausted unit: the combined unit
            // inherits HasMovedThisTurn, so the merge would give nothing
            // this turn while its upkeep increase bills immediately.
            if (destUnit.HasMovedThisTurn)
            {
                Log.Trace(Log.LogCategory.Ai,
                    $"[p2a] skip combine {unitCoord}→{target}: destination unit already moved");
                continue;
            }
            UnitLevel combinedLevel = unit.Level.CombinedWith(destUnit.Level);
            int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel)
                              - UpkeepRules.UpkeepFor(unit.Level)
                              - UpkeepRules.UpkeepFor(destUnit.Level);
            if (!UpkeepRules.SurvivesNextUpkeep(gold, netBefore - upkeepDelta)) continue;
            if (!UnlocksMovementConsumingTarget(unit.Level, destUnit.Level, territory, state, tileIndex)) continue;

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
        GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        if (!territory.HasCapital) yield break;
        int gold = state.Treasury.GetGold(territory.Capital!.Value);
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        UnitLevel[] levels = { UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander };
        foreach (UnitLevel level in levels)
        {
            if (!PurchaseRules.CanAfford(territory, state.Treasury, level, difficulty)) continue;
            int cost = PurchaseRules.CostFor(level, difficulty);
            int buyUpkeep = UpkeepRules.UpkeepFor(level);

            foreach (HexCoord coord in territory.Coords)
            {
                Unit? existingUnit = state.Grid.Get(coord)?.Unit;
                if (existingUnit == null || existingUnit.HasMovedThisTurn) continue;
                // Combining is only legal up to a level sum of Commander;
                // an unchecked pair would fabricate an out-of-range level
                // that live play executes but replay validation rejects.
                if (!level.CanCombineWith(existingUnit.Level)) continue;

                UnitLevel combinedLevel = level.CombinedWith(existingUnit.Level);
                int upkeepDelta = UpkeepRules.UpkeepFor(combinedLevel)
                                  - UpkeepRules.UpkeepFor(existingUnit.Level)
                                  - buyUpkeep;
                if (!UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore - upkeepDelta)) continue;
                if (!UnlocksMovementConsumingTarget(level, existingUnit.Level, territory, state, tileIndex)) continue;

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
    ///
    /// Each target is offered a buy at only the <b>minimum sufficient
    /// level</b> — the cheapest unit that can reach it (a Recruit chops any
    /// tree; a capture needs the lowest level whose attack exceeds the
    /// tile's defense). Levels are walked cheapest-first and a target,
    /// once offered a buy, is skipped for pricier levels, so the AI never
    /// buys a Captain to take a tile a Recruit could (an overkill-spend
    /// contributor to the #108 doom spiral).
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase3(
        Territory territory,
        GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        // Targets already offered a buy at a cheaper level — never buy a
        // pricier unit to reach the same tile.
        var covered = new HashSet<HexCoord>();

        UnitLevel[] levels = { UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander };
        foreach (UnitLevel level in levels)
        {
            foreach (AiCandidate candidate in EnumeratePhase3ForLevel(territory, state, level, tileIndex))
            {
                HexCoord target = ((AiBuyUnitAction)candidate.Action).Destination;
                if (!covered.Add(target)) continue;
                yield return candidate;
            }
        }
    }

    /// <summary>
    /// Phase 3, one tier: the buy-to-capture / buy-to-chop candidates for a
    /// single <paramref name="level"/> — empty when the tier is
    /// unaffordable or insolvent. The per-tier unit of the AI's
    /// weakest-first capture loop: <c>ComputerAi</c> walks tiers ascending
    /// and commits to the first tier that yields anything, so no cross-tier
    /// "covered" dedup is needed there (a pricier tier is only reached when
    /// every cheaper affordable tier yielded zero candidates).
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase3ForLevel(
        Territory territory,
        GameState state,
        UnitLevel level,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        if (!territory.HasCapital) yield break;
        int gold = state.Treasury.GetGold(territory.Capital!.Value);
        (Difficulty difficulty, int netBefore) = EconomyBefore(territory, state);

        if (!PurchaseRules.CanAfford(territory, state.Treasury, level, difficulty)) yield break;
        int cost = PurchaseRules.CostFor(level, difficulty);
        int levelUpkeep = UpkeepRules.UpkeepFor(level);
        if (!UpkeepRules.SurvivesNextUpkeep(gold - cost, netBefore + 1 - levelUpkeep)) yield break;

        List<HexCoord> targets = MovementRules.ValidTargets(
            level, territory, state.Grid, state.Territories, tileIndex);
        foreach (HexCoord target in targets)
        {
            HexTile? targetTile = state.Grid.Get(target);
            if (targetTile == null) continue;
            TargetKind kind = ClassifyTarget(targetTile, territory.Owner);
            if (kind == TargetKind.Capture)
            {
                yield return new AiCandidate(
                    new AiBuyUnitAction(territory.Capital!.Value, target, level),
                    AiActionKind.Capture);
            }
            else if (kind == TargetKind.Chop || kind == TargetKind.Grave)
            {
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
    /// Reposition-class tiles within the territory. No gold spent. Normally
    /// scoped to empty BORDER tiles (interior fortification is pointless), but
    /// Rising Tides relaxes that for a unit standing on a tile that
    /// is forecast to submerge this turn: such a doomed unit may flee to ANY
    /// empty in-territory tile (including the safe interior), and never onto
    /// another doomed tile. The escaping move is rewarded via
    /// <see cref="AiStateScorer.EvacuationBonus"/> in <see cref="ComputerAi"/>.
    /// </summary>
    public static IEnumerable<AiCandidate> EnumeratePhase4bForUnit(
        HexCoord unitCoord,
        Unit unit,
        Territory territory,
        GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        bool unitIsDoomed = IsTideDoomed(state, unitCoord);
        List<HexCoord> targets = MovementRules.ValidTargets(
            unit.Level, territory, state.Grid, state.Territories, tileIndex);
        foreach (HexCoord target in targets)
        {
            if (target.Equals(unitCoord)) continue;
            HexTile? targetTile = state.Grid.Get(target);
            if (targetTile == null) continue;
            // Never flee a doomed tile straight into another doomed tile.
            if (IsTideDoomed(state, target)) continue;
            if (ClassifyTarget(targetTile, territory.Owner) == TargetKind.Reposition
                && targetTile.Occupant == null
                && (unitIsDoomed || IsBorderTile(target, state.Grid, territory.Owner)))
            {
                yield return new AiCandidate(
                    new AiMoveAction(unitCoord, target), AiActionKind.Reposition);
            }
        }
    }

    /// <summary>
    /// True iff <paramref name="coord"/> is in this turn's Rising Tides
    /// submerge forecast.
    /// </summary>
    private static bool IsTideDoomed(GameState state, HexCoord coord)
    {
        foreach (TideStep step in state.PendingTide)
        {
            if (step.Coord.Equals(coord)) return true;
        }
        return false;
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
        GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        UnitLevel combinedLevel = sourceLevel.CombinedWith(destLevel);
        if (combinedLevel == sourceLevel && combinedLevel == destLevel) return false;

        var preCombine = new System.Collections.Generic.HashSet<HexCoord>(
            MovementConsumingTargets(sourceLevel, territory, state, tileIndex));
        foreach (HexCoord c in MovementConsumingTargets(destLevel, territory, state, tileIndex))
            preCombine.Add(c);

        foreach (HexCoord c in MovementConsumingTargets(combinedLevel, territory, state, tileIndex))
        {
            if (!preCombine.Contains(c)) return true;
        }
        return false;
    }

    private static IEnumerable<HexCoord> MovementConsumingTargets(
        UnitLevel level, Territory territory, GameState state,
        IReadOnlyDictionary<HexCoord, Territory>? tileIndex = null)
    {
        foreach (HexCoord target in MovementRules.ValidTargets(
            level, territory, state.Grid, state.Territories, tileIndex))
        {
            HexTile? tile = state.Grid.Get(target);
            if (tile == null) continue;
            TargetKind kind = ClassifyTarget(tile, territory.Owner);
            if (kind == TargetKind.Capture || kind == TargetKind.Chop || kind == TargetKind.Grave)
                yield return target;
        }
    }
}
