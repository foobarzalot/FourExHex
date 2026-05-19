using System.Collections.Generic;

/// <summary>
/// Per-turn upkeep rules. Each unit level has a fixed upkeep cost paid
/// from its containing territory's treasury at the start of the owner's
/// turn. When a territory can't cover its total upkeep, every unit in
/// the territory dies (the territory is "bankrupt"). The remaining gold
/// stays in the treasury.
///
/// Upkeep values per Slay:
///   Peasant = 2, Spearman = 6, Knight = 18, Baron = 54
/// </summary>
public static class UpkeepRules
{
    /// <summary>The upkeep cost a single unit of the given level demands per turn.</summary>
    public static int UpkeepFor(UnitLevel level) => level switch
    {
        UnitLevel.Peasant => 2,
        UnitLevel.Spearman => 6,
        UnitLevel.Knight => 18,
        UnitLevel.Baron => 54,
        _ => 0,
    };

    /// <summary>Sum of upkeep costs for every unit in <paramref name="territory"/>.</summary>
    public static int TotalUpkeepFor(Territory territory, HexGrid grid)
    {
        int total = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile?.Occupant is Unit u)
            {
                total += UpkeepFor(u.Level);
            }
        }
        return total;
    }

    /// <summary>
    /// Try to pay <paramref name="territory"/>'s total upkeep from its
    /// treasury. If affordable, deducts the owed amount and returns true.
    /// If not affordable (or the territory has no capital and therefore
    /// no treasury), every unit in the territory is destroyed (set to
    /// null) and returns false. The remaining gold is left untouched.
    /// </summary>
    public static bool ApplyUpkeep(Territory territory, HexGrid grid, Treasury treasury)
    {
        int owed = TotalUpkeepFor(territory, grid);
        if (owed == 0) return true; // nothing to pay

        int available = territory.HasCapital
            ? treasury.GetGold(territory.Capital!.Value)
            : 0;

        if (available >= owed)
        {
            treasury.SetGold(territory.Capital!.Value, available - owed);
            return true;
        }

        // Bankrupt — every unit in the territory dies and leaves a
        // grave behind. Capital occupants and other non-unit occupants
        // survive untouched.
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile?.Occupant is Unit)
            {
                tile.Occupant = new Grave();
            }
        }
        return false;
    }

    /// <summary>
    /// Apply upkeep to every territory owned by <paramref name="player"/>.
    /// Parallels <see cref="Treasury.CollectIncomeFor"/>. Returns true
    /// if at least one territory failed to pay (units became graves);
    /// false if every territory paid in full or had nothing to pay.
    /// The controller uses the return value to fire a single-shot
    /// audio cue at turn-start.
    /// </summary>
    public static bool ApplyUpkeepFor(Player player, IEnumerable<Territory> territories, HexGrid grid, Treasury treasury)
    {
        bool anyBankrupt = false;
        foreach (Territory territory in territories)
        {
            if (territory.Owner != player.Id) continue;
            if (!ApplyUpkeep(territory, grid, treasury))
            {
                anyBankrupt = true;
            }
        }
        return anyBankrupt;
    }
}
