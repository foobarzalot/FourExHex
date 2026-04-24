using Godot;

/// <summary>
/// Pure state → scalar scoring function used by <see cref="HeuristicAi"/>.
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
    private const double TileWeight = 10.0;

    // Recurring net income is treated as a SINGLE-turn effect in the
    // score — higher weights crushed combines and chops into
    // permanent negative territory (a P+P→S combine costs +2 upkeep,
    // which at weight 3 was -6 on score, while the unit-value gain
    // was only +4). Weight 1 keeps captures dominant (+20+) but
    // leaves combines marginally positive (~+2-4), so the AI can
    // level up when it hits defensive stasis.
    private const double NetIncomeWeight = 1.0;

    // Fragmentation penalty: every territory pays this flat cost.
    // Driving force behind "merges are good" and "enemy splits are
    // good" — both effects fall out of the sign flip when summing
    // over all territories on the board.
    private const double FragmentationPenalty = 15.0;

    // Treasury gold has modest direct value (you can spend it next
    // turn). Not too high or the AI hoards instead of acting.
    private const double GoldWeight = 0.3;

    // Flat penalty per tree in an OWN territory. Trees both block
    // income on their tile (already accounted for) and spread into
    // empty neighbors via TreeRules.SpreadTrees. A cluster of trees
    // grows quadratically, so once bankruptcy cascades drop a pile
    // of graves the board floods with forest unless the AI
    // actively chops. Applied in Score() rather than
    // TerritoryValue() so it's one-sided — penalizing only our
    // own trees, not rewarding us for enemy trees (which would
    // mean capturing an enemy tree-tile is worth less than capturing
    // a bare tile, the opposite of what we want).
    private const double OwnTreePenalty = 6.0;

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
    private const double EnemyEdgePenalty = 1.0;

    // Flat penalty per own tile that (a) has at least one
    // enemy-colored neighbor and (b) has defense 0 (no unit,
    // tower, or capital on or radiating to it). Immediate capture
    // risk. This rewards covering borders with units / towers,
    // building towers on contested frontiers, and capturing an
    // enemy tile that brings a defender into range.
    private const double UndefendedBorderPenalty = 4.0;

    /// <summary>
    /// Score the board from <paramref name="forPlayer"/>'s
    /// perspective: sum of own territory values minus sum of enemy
    /// territory values. Higher = better for this player.
    /// </summary>
    public static double Score(GameState state, Color forPlayer)
    {
        double total = 0.0;
        foreach (Territory t in state.Territories)
        {
            double value = TerritoryValue(t, state);
            if (t.Owner == forPlayer)
            {
                total += value;
                total -= OwnTreePenalty * CountTreesIn(t, state.Grid);
                total -= EnemyEdgePenalty * CountEnemyEdges(t, state.Grid, forPlayer);
                total -= UndefendedBorderPenalty * CountUndefendedBorderTiles(t, state.Grid, forPlayer);
            }
            else
            {
                total -= value;
            }
        }
        return total;
    }

    private static int CountTreesIn(Territory territory, HexGrid grid)
    {
        int count = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile?.Occupant is Tree) count++;
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
    private static int CountEnemyEdges(Territory territory, HexGrid grid, Color forPlayer)
    {
        int edges = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            foreach (HexCoord neighbor in coord.Neighbors())
            {
                HexTile? nt = grid.Get(neighbor);
                if (nt == null) continue;
                if (nt.Color != forPlayer) edges++;
            }
        }
        return edges;
    }

    /// <summary>
    /// Count own tiles in this territory that are adjacent to an
    /// enemy-colored neighbor AND have zero defense coverage.
    /// Each such tile is an immediate, zero-cost capture target
    /// for any enemy peasant; fixing them is one of the AI's
    /// primary defensive jobs.
    /// </summary>
    private static int CountUndefendedBorderTiles(Territory territory, HexGrid grid, Color forPlayer)
    {
        int count = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            bool bordersEnemy = false;
            foreach (HexCoord neighbor in coord.Neighbors())
            {
                HexTile? nt = grid.Get(neighbor);
                if (nt != null && nt.Color != forPlayer)
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
    /// Standalone value of a single territory, independent of whose
    /// perspective we're scoring from. Components:
    ///   + tiles × TileWeight
    ///   + max(0, net_income) × NetIncomeWeight  (clamped: a
    ///     bankrupt territory doesn't get positive recurring value)
    ///   + unit value (zeroed if bankrupt — units will die)
    ///   + gold × GoldWeight
    ///   − FragmentationPenalty (flat, per territory)
    /// </summary>
    private static double TerritoryValue(Territory territory, GameState state)
    {
        int tiles = territory.Coords.Count;
        int income = TreeRules.CountNonTreeTiles(territory, state.Grid);
        int upkeep = UpkeepRules.TotalUpkeepFor(territory, state.Grid);
        int netIncome = income - upkeep;

        // Bankruptcy lookahead: a territory with netIncome < 0 or
        // no capital will lose all its units on its next upkeep
        // step, so its units (and any stored gold it can't even
        // collect) are worth zero going forward.
        bool willBankrupt = netIncome < 0 || !territory.HasCapital;

        double unitValue = 0.0;
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

        int gold = territory.HasCapital
            ? state.Treasury.GetGold(territory.Capital!.Value)
            : 0;

        double effectiveNetIncome = System.Math.Max(0, netIncome);
        double value = tiles * TileWeight
                       + effectiveNetIncome * NetIncomeWeight
                       + unitValue
                       + gold * GoldWeight
                       - FragmentationPenalty;

        return value;
    }

    /// <summary>
    /// Strategic value of a unit by level. Roughly tracks upkeep
    /// cost but discounts higher levels so the AI doesn't
    /// over-combine when lower-level units would suffice. Peasant
    /// = 4 (attack level 1, cheap), Spearman = 12, Knight = 30,
    /// Baron = 70. Not strictly proportional to upkeep (2/6/18/54)
    /// because the upkeep/value ratio should reward leveling up.
    /// </summary>
    private static double UnitValue(UnitLevel level) => level switch
    {
        UnitLevel.Peasant => 4.0,
        UnitLevel.Spearman => 12.0,
        UnitLevel.Knight => 30.0,
        UnitLevel.Baron => 70.0,
        _ => 0.0,
    };
}
