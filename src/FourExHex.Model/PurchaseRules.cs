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
    /// True iff a tower can be placed on <paramref name="tile"/>:
    /// the tile must be empty and inside <paramref name="territory"/>.
    /// The AI imposes an additional spacing rule of its own (see
    /// <c>AiCommon.MinTowerSpacing</c>) to avoid redundant clustering;
    /// human players are not bound by it.
    /// </summary>
    public static bool IsValidTowerLocation(HexTile tile, Territory territory, HexGrid grid)
    {
        if (tile.Occupant != null) return false;
        // Towers may be built on mountains: the +1 high-ground bonus
        // is the whole point. The tile just has to be empty and in-territory.
        return territory.Contains(tile.Coord);
    }
}
