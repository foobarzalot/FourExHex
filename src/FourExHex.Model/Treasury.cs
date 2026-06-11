using System;
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

    /// <summary>Drop every entry. Used by undo/restore.</summary>
    public void Clear() => _gold.Clear();

    /// <summary>
    /// Add income gold to each multi-hex territory owned by
    /// <paramref name="player"/>. Territories without capitals (singletons)
    /// are silently skipped. When <paramref name="grid"/> is supplied,
    /// income excludes tiles occupied by <see cref="Tree"/> occupants
    /// (trees block income on their tile); when it is null, income is
    /// <see cref="Territory.Size"/>.
    /// </summary>
    public void CollectIncomeFor(
        Player player,
        IEnumerable<Territory> territories,
        HexGrid? grid = null)
    {
        foreach (Territory territory in territories)
        {
            if (territory.Owner != player.Id) continue;
            if (!territory.HasCapital) continue;

            HexCoord capital = territory.Capital!.Value;
            int income = grid != null
                ? IncomeRules.IncomeFor(territory, grid)
                : territory.Size;
            int current = GetGold(capital);
            _gold[capital] = current + income;
        }
    }

    /// <summary>
    /// Rebuild the treasury after a capture re-ran <see cref="TerritoryFinder"/>.
    /// Rules:
    ///   - For each NEW territory, look at the OLD capitals still inside it
    ///     (possibly 0, 1, or many).
    ///   - 0 inherited: the new capital starts at 0 gold.
    ///   - 1 or more inherited: sum the inherited gold and credit it to the
    ///     new capital's coord (which may be different from any of the old
    ///     capital coords, because <see cref="CapitalAssigner"/> picks
    ///     deterministically).
    ///   - Old capitals not inherited by any new territory (captured by
    ///     enemy, or the capital hex itself got demoted) have their gold
    ///     forfeited.
    /// After this call the treasury's keys are exactly the current capital
    /// coords of <paramref name="newTerritories"/>.
    /// </summary>
    public void ReconcileAfterCapture(
        IReadOnlyList<Territory> oldTerritories,
        IReadOnlyList<Territory> newTerritories)
    {
        // Snapshot each old capital's owner + gold. Owner is needed so a
        // captured enemy capital tile doesn't bleed gold into the
        // attacker's treasury (Slay's rule: taking an enemy capital
        // destroys their gold, it doesn't transfer).
        var oldCapitalInfo = new Dictionary<HexCoord, (PlayerId Owner, int Gold)>();
        foreach (Territory old in oldTerritories)
        {
            if (old.HasCapital)
            {
                HexCoord oldCap = old.Capital!.Value;
                oldCapitalInfo[oldCap] = (old.Owner, GetGold(oldCap));
            }
        }

        // Build the new gold map by inheriting each new territory's treasury
        // from whatever same-owner old capitals still lie inside it.
        var newGold = new Dictionary<HexCoord, int>();
        foreach (Territory newT in newTerritories)
        {
            if (!newT.HasCapital) continue;
            HexCoord newCap = newT.Capital!.Value;

            int inheritedTotal = 0;
            foreach (HexCoord coord in newT.Coords)
            {
                if (oldCapitalInfo.TryGetValue(coord, out (PlayerId Owner, int Gold) info)
                    && info.Owner == newT.Owner)
                {
                    inheritedTotal += info.Gold;
                }
            }

            newGold[newCap] = inheritedTotal;
        }

        // Replace the treasury atomically. Any old-capital entries that
        // didn't survive into a new territory are dropped (gold forfeit).
        _gold.Clear();
        foreach (KeyValuePair<HexCoord, int> kvp in newGold)
        {
            _gold[kvp.Key] = kvp.Value;
        }
    }
}
