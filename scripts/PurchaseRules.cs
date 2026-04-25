using System.Linq;

/// <summary>
/// Pure rules for buying units. Callers are expected to check
/// <see cref="CanAfford"/> and <see cref="IsValidPeasantTarget"/>
/// before invoking the buy.
/// </summary>
public static class PurchaseRules
{
    public const int PeasantCost = 10;
    public const int SpearmanCost = 20;
    public const int KnightCost = 30;
    public const int BaronCost = 40;
    public const int TowerCost = 15;

    /// <summary>Gold cost to directly buy a unit of the given level.</summary>
    public static int CostFor(UnitLevel level) => level switch
    {
        UnitLevel.Peasant => PeasantCost,
        UnitLevel.Spearman => SpearmanCost,
        UnitLevel.Knight => KnightCost,
        UnitLevel.Baron => BaronCost,
        _ => int.MaxValue,
    };

    /// <summary>
    /// True iff <paramref name="territory"/> has a capital and enough
    /// gold there to buy a unit of <paramref name="level"/>.
    /// </summary>
    public static bool CanAfford(Territory territory, Treasury treasury, UnitLevel level)
    {
        if (!territory.HasCapital) return false;
        return treasury.GetGold(territory.Capital!.Value) >= CostFor(level);
    }

    public static bool CanAffordPeasant(Territory territory, Treasury treasury) =>
        CanAfford(territory, treasury, UnitLevel.Peasant);

    /// <summary>
    /// True iff <paramref name="territory"/> has a capital and enough
    /// gold to build a tower (15g, one-time, no upkeep).
    /// </summary>
    public static bool CanAffordTower(Territory territory, Treasury treasury)
    {
        if (!territory.HasCapital) return false;
        return treasury.GetGold(territory.Capital!.Value) >= TowerCost;
    }

    public static bool IsValidPeasantTarget(HexTile tile, Territory territory)
    {
        // Must be placeable: empty or a grave (graves don't block
        // placement — a new peasant buries the grave).
        if (tile.Occupant != null && tile.Occupant is not Grave) return false;
        return territory.Coords.Contains(tile.Coord);
    }

    public static void BuyPeasant(HexTile tile, Territory territory, Treasury treasury)
    {
        HexCoord capital = territory.Capital!.Value;
        treasury.SetGold(capital, treasury.GetGold(capital) - PeasantCost);
        tile.Occupant = new Unit(territory.Owner);
    }
}
