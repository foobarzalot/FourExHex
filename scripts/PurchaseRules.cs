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

    /// <summary>
    /// Minimum hex distance between two towers in the same territory.
    /// 3 means a gap of two empty (or non-tower) tiles must lie
    /// between any two friendly towers. Prevents the AI (and humans)
    /// from clustering towers redundantly on the same border.
    /// </summary>
    public const int MinTowerSpacing = 3;

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

    /// <summary>
    /// True iff a tower can be placed on <paramref name="tile"/>:
    /// the tile must be empty, in <paramref name="territory"/>, AND
    /// at least <see cref="MinTowerSpacing"/> hex distance from
    /// every existing tower in the same territory. The spacing rule
    /// prevents redundant towers covering the same border tiles.
    /// </summary>
    public static bool IsValidTowerLocation(HexTile tile, Territory territory, HexGrid grid)
    {
        if (tile.Occupant != null) return false;
        if (!territory.Coords.Contains(tile.Coord)) return false;

        foreach (HexCoord coord in territory.Coords)
        {
            if (coord.Equals(tile.Coord)) continue;
            HexTile? other = grid.Get(coord);
            if (other?.Occupant is not Tower) continue;
            if (HexCoord.Distance(tile.Coord, coord) < MinTowerSpacing) return false;
        }
        return true;
    }

    public static void BuyPeasant(HexTile tile, Territory territory, Treasury treasury)
    {
        HexCoord capital = territory.Capital!.Value;
        treasury.SetGold(capital, treasury.GetGold(capital) - PeasantCost);
        tile.Occupant = new Unit(territory.Owner);
    }
}
