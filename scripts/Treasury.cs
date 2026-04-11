using System.Collections.Generic;

/// <summary>
/// Holds the gold balance for every capital hex on the map, keyed by capital
/// coord. Gold survives re-running TerritoryFinder as long as the capital's
/// coordinate is still a capital in the new partition.
///
/// TODO (Step 10): add ReconcileAfterRecompute(oldTerritories, newTerritories)
/// to handle captures. When two old capitals end up in the same new territory
/// (merge), their gold should sum. When an old capital's hex gets captured by
/// a different color, that gold is lost (consistent with Slay's rule that
/// taking an enemy capital destroys the treasury).
/// </summary>
public class Treasury
{
    private readonly Dictionary<HexCoord, int> _gold = new();

    public int GetGold(HexCoord capital) =>
        _gold.TryGetValue(capital, out int amount) ? amount : 0;

    public void SetGold(HexCoord capital, int amount) =>
        _gold[capital] = amount;

    /// <summary>
    /// Add <c>territory.Size</c> gold to each multi-hex territory owned by
    /// <paramref name="player"/>. Territories without capitals (singletons)
    /// are silently skipped.
    /// </summary>
    public void CollectIncomeFor(Player player, IEnumerable<Territory> territories)
    {
        foreach (Territory territory in territories)
        {
            if (territory.Owner != player.Color) continue;
            if (!territory.HasCapital) continue;

            HexCoord capital = territory.Capital!.Value;
            int current = GetGold(capital);
            _gold[capital] = current + territory.Size;
        }
    }
}
