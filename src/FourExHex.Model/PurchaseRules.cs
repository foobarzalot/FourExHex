using System.Linq;

/// <summary>
/// Pure rules for buying units. Callers are expected to check
/// <see cref="CanAfford"/> and <see cref="IsValidRecruitTarget"/>
/// before invoking the buy.
/// </summary>
public static class PurchaseRules
{
    public const int RecruitCost = 10;
    public const int SoldierCost = 20;
    public const int CaptainCost = 30;
    public const int CommanderCost = 40;
    public const int TowerCost = 15;

    /// <summary>Gold cost to directly buy a unit of the given level.</summary>
    public static int CostFor(UnitLevel level) => level switch
    {
        UnitLevel.Recruit => RecruitCost,
        UnitLevel.Soldier => SoldierCost,
        UnitLevel.Captain => CaptainCost,
        UnitLevel.Commander => CommanderCost,
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

    public static bool CanAffordRecruit(Territory territory, Treasury treasury) =>
        CanAfford(territory, treasury, UnitLevel.Recruit);

    /// <summary>
    /// True iff <paramref name="territory"/> has a capital and enough
    /// gold to build a tower (15g, one-time, no upkeep).
    /// </summary>
    public static bool CanAffordTower(Territory territory, Treasury treasury)
    {
        if (!territory.HasCapital) return false;
        return treasury.GetGold(territory.Capital!.Value) >= TowerCost;
    }

    public static bool IsValidRecruitTarget(HexTile tile, Territory territory)
    {
        // Must be placeable: empty or a grave (graves don't block
        // placement — a new recruit buries the grave).
        if (tile.Occupant != null && tile.Occupant is not Grave) return false;
        return territory.Coords.Contains(tile.Coord);
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
        return territory.Coords.Contains(tile.Coord);
    }

    public static void BuyRecruit(HexTile tile, Territory territory, Treasury treasury)
    {
        HexCoord capital = territory.Capital!.Value;
        treasury.SetGold(capital, treasury.GetGold(capital) - RecruitCost);
        tile.Occupant = new Unit(territory.Owner);
    }
}
