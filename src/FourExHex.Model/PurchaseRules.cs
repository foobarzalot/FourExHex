using System.Linq;

/// <summary>
/// Pure rules for buying units. Callers are expected to check
/// <see cref="CanAfford"/> before purchasing. Costs depend on the buyer's
/// <see cref="Difficulty"/> (the human's self-imposed handicap — AIs
/// always pay the Soldier baseline): unit cost = base × tier with the
/// base from <see cref="DifficultyRules.UnitBaseCost"/>, towers from
/// <see cref="DifficultyRules.TowerCost"/>.
/// </summary>
public static class PurchaseRules
{
    /// <summary>
    /// Gold cost to directly buy a unit of the given level for a buyer at
    /// the given difficulty: base × tier (Recruit 1 … Commander 4).
    /// Soldier base 10 → the classic 10/20/30/40 ladder.
    /// </summary>
    public static int CostFor(UnitLevel level, Difficulty difficulty)
    {
        int tier = level switch
        {
            UnitLevel.Recruit => 1,
            UnitLevel.Soldier => 2,
            UnitLevel.Captain => 3,
            UnitLevel.Commander => 4,
            _ => int.MaxValue,
        };
        return tier == int.MaxValue
            ? int.MaxValue
            : DifficultyRules.UnitBaseCost(difficulty) * tier;
    }

    /// <summary>Tower cost for a buyer at the given difficulty.</summary>
    public static int TowerCostFor(Difficulty difficulty) =>
        DifficultyRules.TowerCost(difficulty);

    /// <summary>
    /// True iff <paramref name="territory"/> has a capital and enough
    /// gold there to buy a unit of <paramref name="level"/> at the
    /// buyer's <paramref name="difficulty"/>.
    /// </summary>
    public static bool CanAfford(Territory territory, Treasury treasury, UnitLevel level, Difficulty difficulty)
    {
        if (!territory.HasCapital) return false;
        return treasury.GetGold(territory.Capital!.Value) >= CostFor(level, difficulty);
    }

    public static bool CanAffordRecruit(Territory territory, Treasury treasury, Difficulty difficulty) =>
        CanAfford(territory, treasury, UnitLevel.Recruit, difficulty);

    /// <summary>
    /// True iff <paramref name="territory"/> has a capital and enough
    /// gold to build a tower (one-time, no upkeep) at the buyer's
    /// <paramref name="difficulty"/>.
    /// </summary>
    public static bool CanAffordTower(Territory territory, Treasury treasury, Difficulty difficulty)
    {
        if (!territory.HasCapital) return false;
        return treasury.GetGold(territory.Capital!.Value) >= TowerCostFor(difficulty);
    }

    /// <summary>
    /// The universal tower placement rule, enforced for every actor at
    /// execution time: the tile must be empty and inside
    /// <paramref name="territory"/>. No player — AI included — may drop
    /// a tower on an occupied tile.
    /// </summary>
    public static bool IsValidTowerLocation(HexTile tile, Territory territory, HexGrid grid)
    {
        if (tile.Occupant != null) return false;
        // Towers may be built on mountains: the +1 high-ground bonus
        // is the whole point. The tile just has to be empty and in-territory.
        return territory.Contains(tile.Coord);
    }

    /// <summary>
    /// AI tower-site *intent* eligibility — NOT an execution rule.
    /// Everything <see cref="IsValidTowerLocation"/> accepts, plus a
    /// tile holding an own unmoved unit with a
    /// <see cref="TowerPushDestination"/> escape. The AI enumerates such
    /// tiles as build intents; the controller's chooser wrapper
    /// (<c>AiActionLowering</c>) lowers an intent into two discrete
    /// legal actions — the make-way reposition, then the build on the
    /// vacated tile — so execution only ever sees the strict rule.
    /// </summary>
    public static bool IsValidTowerLocationWithPush(HexTile tile, Territory territory, HexGrid grid)
    {
        if (IsValidTowerLocation(tile, territory, grid)) return true;
        if (!territory.Contains(tile.Coord)) return false;
        if (tile.Occupant is not Unit unit) return false;
        if (unit.Owner != territory.Owner) return false;
        if (unit.HasMovedThisTurn) return false;
        return TowerPushDestination(tile.Coord, territory, grid) != null;
    }

    /// <summary>
    /// Where a make-way move ahead of a tower build at
    /// <paramref name="coord"/> sends the resident unit: the
    /// lex-smallest (by <see cref="HexCoord.CompareTo"/>) adjacent empty
    /// in-territory tile, or null when the unit is boxed in.
    /// Deterministic on purpose — the simulator's scoring mirror and the
    /// controller's lowering must derive the same destination.
    /// </summary>
    public static HexCoord? TowerPushDestination(HexCoord coord, Territory territory, HexGrid grid)
    {
        HexCoord? best = null;
        foreach (HexCoord neighbor in coord.Neighbors())
        {
            if (!territory.Contains(neighbor)) continue;
            HexTile? tile = grid.Get(neighbor);
            if (tile == null || tile.Occupant != null) continue;
            if (best == null || neighbor.CompareTo(best.Value) < 0) best = neighbor;
        }
        return best;
    }
}
