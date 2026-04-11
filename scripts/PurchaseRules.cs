using System.Linq;

/// <summary>
/// Pure rules for buying units. Callers are expected to check
/// <see cref="CanAffordPeasant"/> and <see cref="IsValidPeasantTarget"/>
/// before invoking <see cref="BuyPeasant"/>.
/// </summary>
public static class PurchaseRules
{
    public const int PeasantCost = 10;

    public static bool CanAffordPeasant(Territory territory, Treasury treasury)
    {
        if (!territory.HasCapital) return false;
        return treasury.GetGold(territory.Capital!.Value) >= PeasantCost;
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
