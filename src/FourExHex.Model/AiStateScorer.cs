using System.Linq;

/// <summary>
/// Pure state → scalar scoring function used by <see cref="ComputerAi"/>.
/// Computes <c>self_value − sum(enemy_values)</c> so higher = better
/// for the given player. The key strategic behaviors fall out of
/// three design choices:
///
/// 1. <b>Per-territory fragmentation penalty.</b> Every territory
///    (own or enemy) subtracts a fixed constant from its value.
///    Fewer own territories = less penalty = higher self score, so
///    a capture that merges two of our territories into one gives a
///    bonus automatically. More enemy territories = more penalty on
///    them, so a capture that splits an enemy also scores higher.
///
/// 2. <b>Bankruptcy lookahead.</b> A territory whose
///    <c>income &lt; upkeep</c> or that has no capital will bankrupt
///    on its owner's next upkeep step — its units are effectively
///    already dead. We zero out its unit value contribution. This
///    gives "orphaning" captures their deserved score: a split that
///    leaves 5 enemy tiles in a capital-less sub-territory reads as
///    a near-total loss for them.
///
/// 3. <b>Tile and income weight.</b> Raw territorial gain and
///    recurring income both count. Unit strength counts too (so the
///    AI cares about preserving high-level units it has built).
///
/// All weights are magic numbers tuned by playtest; see comments at
/// each constant for the rationale.
/// </summary>
public static class AiStateScorer
{
    // Per-tile value — the base unit of territorial worth.
    private const int TileWeight = 10;

    // Recurring net income is treated as a SINGLE-turn effect in the
    // score. Weight 1 keeps captures dominant while leaving combines
    // marginally positive, so the AI can level up out of defensive stasis.
    private const int NetIncomeWeight = 1;

    // Fragmentation penalty: every territory pays this flat cost.
    // Driving force behind "merges are good" and "enemy splits are
    // good" — both effects fall out of the sign flip when summing
    // over all territories on the board.
    private const int FragmentationPenalty = 15;

    // Flat penalty per tree (or grave) in an OWN territory. Trees block income
    // on their tile and seed further growth via TreeRules.RunStartOfTurnGrowth;
    // graves on own tiles convert unconditionally to trees on the same
    // start-of-turn step, so they count as trees here. Applied in Score() so
    // it's one-sided — penalizing only our own trees/graves, not rewarding us
    // for enemy ones. The value must exceed up to 3 uncovered borders (a chop
    // is worth this minus UndefendedBorderPenalty per border the chopping unit
    // stops covering) so chops are taken on their own merit.
    private const int OwnTreePenalty = 35;

    // Flat penalty per edge on our territories that faces an
    // enemy-colored tile. "Edges" are counted from our side only
    // (one edge per own-tile-to-enemy-tile adjacency, counted
    // once). Rewards captures that fill enemy concavities (many
    // edges removed in one shot) and penalizes captures that
    // extend a salient into enemy territory (many new edges
    // exposed). Also rewards territory consolidation — merging
    // two neighboring own territories turns their shared edges
    // from internal into... still internal, but any enemy-facing
    // edges that were previously mid-front become back-of-blob.
    //
    // Weight 3 makes a fully-enclosed singleton capture (-6 edges) worth +18
    // from this term alone, comparable to two tiles' worth of TileWeight,
    // enough to outrun the tower-defended-border-bonus loss when interior tiles
    // stop being borders. Past 5 the AI starts contorting territory shape at the
    // expense of captures that actually grow it.
    private const int EnemyEdgePenalty = 3;

    // Flat penalty per own tile that (a) has at least one
    // enemy-colored neighbor and (b) has defense 0 (no unit,
    // tower, or capital on or radiating to it). Immediate capture
    // risk. This rewards covering borders with units / towers,
    // building towers on contested frontiers, and capturing an
    // enemy tile that brings a defender into range.
    private const int UndefendedBorderPenalty = 10;

    // Bonus awarded per border tile that a newly-placed tower
    // covers, counted only at the moment of the BuildTower action.
    // Pre-existing towers contribute zero — the bonus is a one-shot
    // incentive for the act of placing the tower, not a standing
    // reward for owning towers. Without this term the scorer can't
    // see what towers are actually for and the AI never builds them.
    // Per-action rather than static so captures that turn own border tiles
    // into interior tiles don't lose a standing bonus and read as worse.
    private const int BuildTowerCoverageBonus = 10;

    // Standing reward per point of defense on an OWN tile that borders an
    // enemy (one-sided, applied in Score() like UndefendedBorderPenalty).
    // Values *holding* a strong frontier, so a defender stays put rather than
    // wandering, and holding/capturing a mountain reads better because
    // DefenseRules.Defense bakes in the +1 high-ground. Kept small so it
    // stays a tie-breaker between defensive positions, not a driver that
    // crowds out captures (the stasis guard).
    private const int ContestedDefenseWeight = 2;

    // Ceiling on the defense counted by ContestedDefenseWeight. A tile at
    // defense 4 is already uncapturable (no level-5 attacker exists), so
    // paying linearly past that rewards safety that can't be threatened and
    // nudges over-garrisoning. Set to 3 — the lowest cap that still rewards
    // the bread-and-butter soldier-onto-mountain play (defense 2→3); it
    // clamps captain/commander to the same ceiling so stacking strength past
    // it stops paying. Must stay ≥ 3 or the mountain +1 goes invisible.
    private const int ContestedDefenseCap = 3;

    /// <summary>
    /// One-shot scoring delta awarded for the act of placing a
    /// tower at <paramref name="placement"/>. Counts border tiles
    /// in the new tower's coverage area (the placement tile + its
    /// same-territory neighbors that border an enemy) and skips
    /// any that are already tower-defended by some pre-existing
    /// tower. Returns count × <see cref="BuildTowerCoverageBonus"/>.
    /// Returns 0 if <paramref name="placement"/> is not in an own
    /// territory (defensive — callers should only invoke for valid
    /// BuildTower candidates).
    /// </summary>
    public static int BuildTowerBonus(HexCoord placement, GameState state, PlayerId owner)
    {
        Territory? territory = TerritoryLookup.FindOwnedContaining(
            state.Territories, owner, placement);
        if (territory == null) return 0;

        int count = 0;
        if (CoverageTileQualifies(placement, placement, territory, state.Grid, owner))
        {
            count++;
        }
        foreach (HexCoord neighbor in placement.Neighbors())
        {
            if (!territory.Contains(neighbor)) continue;
            if (CoverageTileQualifies(neighbor, placement, territory, state.Grid, owner))
            {
                count++;
            }
        }
        return count * BuildTowerCoverageBonus;
    }

    /// <summary>
    /// True iff <paramref name="tile"/> is a border tile (has at
    /// least one enemy-colored neighbor) that the tower at
    /// <paramref name="placement"/> would be the first/only tower
    /// to cover. We exclude any tower that lives at
    /// <paramref name="placement"/> itself (the new tower we're
    /// scoring) so the new tower never disqualifies its own tiles.
    /// </summary>
    private static bool CoverageTileQualifies(
        HexCoord tile,
        HexCoord placement,
        Territory territory,
        HexGrid grid,
        PlayerId owner)
    {
        if (!AiCommon.IsBorderTile(tile, grid, owner)) return false;

        HexTile? selfTile = grid.Get(tile);
        if (!tile.Equals(placement) && selfTile?.Occupant is Tower) return false;

        foreach (HexCoord neighbor in tile.Neighbors())
        {
            if (neighbor.Equals(placement)) continue;
            if (!territory.Contains(neighbor)) continue;
            HexTile? nt = grid.Get(neighbor);
            if (nt?.Occupant is Tower) return false;
        }
        return true;
    }

    /// <summary>
    /// Score the board from <paramref name="forPlayer"/>'s
    /// perspective: sum of own territory values minus sum of enemy
    /// territory values. Higher = better for this player.
    /// </summary>
    public static int Score(GameState state, PlayerId forPlayer)
    {
        int total = 0;
        foreach (Territory t in state.Territories)
        {
            int value = TerritoryValue(t, state);
            if (t.Owner == forPlayer)
            {
                total += value;
                // Vikings pay no upkeep, so trees/graves on their own neutral
                // land are harmless — no standing penalty (which would
                // otherwise leak a chop incentive into their capture deltas).
                if (!forPlayer.IsNone)
                {
                    total -= OwnTreePenalty * CountTreesAndGravesIn(t, state.Grid);
                }
                total -= EnemyEdgePenalty * CountEnemyEdges(t, state.Grid, forPlayer);
                total -= UndefendedBorderPenalty * CountUndefendedBorderTiles(t, state.Grid, forPlayer);
                total += ContestedDefenseWeight * SumCappedContestedBorderDefense(t, state.Grid, forPlayer);
            }
            else
            {
                total -= value;
            }
        }
        return total;
    }

    private static int CountTreesAndGravesIn(Territory territory, HexGrid grid)
    {
        int count = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile?.Occupant is Tree || tile?.Occupant is Grave) count++;
        }
        return count;
    }

    /// <summary>
    /// Count adjacent-neighbor edges from this territory's tiles
    /// that face a tile of a different color. Off-map neighbors
    /// aren't counted — we only care about contested interfaces
    /// with live enemies. Each edge is counted once, from the
    /// own-tile side.
    /// </summary>
    private static int CountEnemyEdges(Territory territory, HexGrid grid, PlayerId forPlayer)
    {
        int edges = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            foreach (HexCoord neighbor in coord.Neighbors())
            {
                HexTile? nt = grid.Get(neighbor);
                if (nt == null) continue;
                if (nt.Owner != forPlayer) edges++;
            }
        }
        return edges;
    }

    /// <summary>
    /// Count own tiles in this territory that are adjacent to an
    /// enemy-owned neighbor AND have zero defense coverage.
    /// Each such tile is an immediate, zero-cost capture target
    /// for any enemy recruit; fixing them is one of the AI's
    /// primary defensive jobs.
    /// </summary>
    private static int CountUndefendedBorderTiles(Territory territory, HexGrid grid, PlayerId forPlayer)
    {
        int count = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            bool bordersEnemy = false;
            foreach (HexCoord neighbor in coord.Neighbors())
            {
                HexTile? nt = grid.Get(neighbor);
                if (nt != null && nt.Owner != forPlayer)
                {
                    bordersEnemy = true;
                    break;
                }
            }
            if (!bordersEnemy) continue;
            if (DefenseRules.Defense(coord, grid, territory) == 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Sum of (capped) defense over this territory's tiles that border an
    /// enemy — the standing reward backing <see cref="ContestedDefenseWeight"/>.
    /// Each contested-border tile contributes
    /// <c>min(Defense, ContestedDefenseCap)</c>, so holding a stronger
    /// frontier (e.g. a defender on a mountain, +1 via
    /// <see cref="DefenseRules.Defense"/>) reads as more valuable, up to the
    /// cap. Mirrors the contested-border predicate
    /// <see cref="CountUndefendedBorderTiles"/> uses (off-map neighbours don't
    /// count); shares <see cref="AiCommon.IsBorderTile"/> for that test.
    /// </summary>
    private static int SumCappedContestedBorderDefense(Territory territory, HexGrid grid, PlayerId forPlayer)
    {
        int sum = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            if (!AiCommon.IsBorderTile(coord, grid, forPlayer)) continue;
            int defense = DefenseRules.Defense(coord, grid, territory);
            sum += System.Math.Min(defense, ContestedDefenseCap);
        }
        return sum;
    }

    /// <summary>
    /// Standalone value of a single territory, independent of whose
    /// perspective we're scoring from. Components:
    ///   + tiles × TileWeight
    ///   + max(0, net_income) × NetIncomeWeight  (clamped: a
    ///     bankrupt territory doesn't get positive recurring value)
    ///   + unit value (zeroed if bankrupt — units will die)
    ///   − FragmentationPenalty (flat, per territory)
    ///
    /// Treasury gold contributes ZERO to standing value. With a
    /// non-zero gold term the AI was reading a 1500g hoard as
    /// +450 standing score, dwarfing the +1 swing of any productive
    /// 1-ply buy and collapsing into "do nothing" stasis.
    /// Removing it makes any score-positive buy strictly
    /// better than holding gold. The bankruptcy lookahead below
    /// still prevents buys that would push the territory into
    /// negative net income — that's the remaining cost signal.
    /// </summary>
    private static int TerritoryValue(Territory territory, GameState state)
    {
        int tiles = territory.Coords.Count;
        int income = IncomeRules.IncomeFor(territory, state.Grid);
        int upkeep = UpkeepRules.TotalUpkeepFor(territory, state.Grid);
        int netIncome = income - upkeep;

        int gold = territory.HasCapital
            ? state.Treasury.GetGold(territory.Capital!.Value)
            : 0;

        // Bankruptcy lookahead: a capital-less territory can't collect
        // income at all, so its units die on the next upkeep step.
        // Otherwise defer to the shared solvency primitive — the same
        // one AiCommon.Enumerate uses for its candidate-gating gates.
        // Neutral (viking) territories are upkeep-exempt and can never
        // bankrupt, so their units always carry full value — for the
        // vikings' own scoring AND as threats in every player's.
        bool willBankrupt = !territory.Owner.IsNone
            && (!territory.HasCapital
                || !UpkeepRules.SurvivesNextUpkeep(gold, netIncome));

        int unitValue = 0;
        if (!willBankrupt)
        {
            foreach (HexCoord coord in territory.Coords)
            {
                HexTile? tile = state.Grid.Get(coord);
                if (tile?.Unit != null)
                {
                    unitValue += UnitValue(tile.Unit.Level);
                }
            }
        }

        // Gold earning premium. A gold tile earns 5× an ordinary
        // tile (1 + IncomeRules.GoldTileBonus), so its territorial worth is
        // 5× TileWeight: the base TileWeight above is unconditional (every
        // tile, like ordinary land), and this adds the extra
        // TileWeight × GoldTileBonus on top — derived from the same lever
        // so it auto-rescales, no new magic constant. Two-sided (this whole
        // value is subtracted for enemies) so capturing an enemy gold tile
        // reads as doubly good. Counted via CountGoldIncomeTiles, so it is
        // gated only on the tile actually earning (a tree/grave-blocked gold
        // tile contributes nothing and reads as ordinary land — clearing the
        // tree unlocks the premium, making gold-trees the most desirable
        // chops). NOT gated by bankruptcy: the premium is durable terrain
        // worth, the whole point of the issue — it survives a temporary
        // bankruptcy that zeroes the income blip below.
        int goldIncomeTiles = TreeRules.CountGoldIncomeTiles(territory, state.Grid);
        int goldPremium = goldIncomeTiles * TileWeight * IncomeRules.GoldTileBonus;
        if (goldPremium > 0)
        {
            Log.Debug(Log.LogCategory.Ai,
                $"[gold-premium] cap={territory.Capital} goldTiles={goldIncomeTiles} " +
                $"premium={goldPremium}");
        }

        int effectiveNetIncome = System.Math.Max(0, netIncome);
        int value = tiles * TileWeight
                    + goldPremium
                    + effectiveNetIncome * NetIncomeWeight
                    + unitValue
                    - FragmentationPenalty;

        return value;
    }

    /// <summary>
    /// Rising Tides: per-move evacuation delta. When <paramref name="mv"/>
    /// moves <paramref name="owner"/>'s unit OFF a tile that is forecast to submerge
    /// this turn (in <see cref="GameState.PendingTide"/>) and onto a tile that is
    /// NOT itself doomed, returns the unit's <see cref="UnitValue"/> — the value
    /// saved from drowning. Returns 0 for any non-escaping move (source not doomed,
    /// destination doomed, or no own unit at the source). Added to a candidate's
    /// delta in <see cref="ComputerAi"/>, exactly like <see cref="BuildTowerBonus"/>,
    /// so the absolute <see cref="Score"/> stays clean. This drives the defensive
    /// phase to evacuate a unit that would otherwise sit still and drown.
    /// </summary>
    public static int EvacuationBonus(AiMoveAction mv, GameState state, PlayerId owner)
    {
        if (state.PendingTide.Count == 0) return 0;

        bool sourceDoomed = false;
        bool destDoomed = false;
        foreach (TideStep step in state.PendingTide)
        {
            if (step.Coord.Equals(mv.Source)) sourceDoomed = true;
            if (step.Coord.Equals(mv.Destination)) destDoomed = true;
        }
        if (!sourceDoomed || destDoomed) return 0;

        if (state.Grid.Get(mv.Source)?.Occupant is not Unit unit || unit.Owner != owner)
        {
            return 0;
        }

        int bonus = UnitValue(unit.Level);
        Log.Debug(Log.LogCategory.Ai,
            $"[tide-evac] {owner} {mv.Source}->{mv.Destination} +{bonus}");
        return bonus;
    }

    /// <summary>
    /// Strategic value of a unit by level. Roughly tracks upkeep
    /// cost but discounts higher levels so the AI doesn't
    /// over-combine when lower-level units would suffice. Recruit
    /// = 4 (attack level 1, cheap), Soldier = 12, Captain = 30,
    /// Commander = 70. Not strictly proportional to upkeep (2/6/18/54)
    /// because the upkeep/value ratio should reward leveling up.
    /// </summary>
    private static int UnitValue(UnitLevel level) => level switch
    {
        UnitLevel.Recruit => 4,
        UnitLevel.Soldier => 12,
        UnitLevel.Captain => 30,
        UnitLevel.Commander => 70,
        _ => 0,
    };
}
