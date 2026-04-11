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
        if (tile.Unit != null) return false;
        if (territory.Capital == tile.Coord) return false;
        return territory.Coords.Contains(tile.Coord);
    }

    public static void BuyPeasant(HexTile tile, Territory territory, Treasury treasury)
    {
        HexCoord capital = territory.Capital!.Value;
        treasury.SetGold(capital, treasury.GetGold(capital) - PeasantCost);
        tile.Unit = new Unit(UnitLevel.Peasant, territory.Owner);
    }
}
