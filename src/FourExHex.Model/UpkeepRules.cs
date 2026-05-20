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
/// <summary>
/// Outlook of a territory's near-term economy. Pure economic facts —
/// the human-vs-AI gate that decides whether to surface warnings in
/// the view is the caller's concern.
/// </summary>
public enum EconomyOutlook
{
    /// <summary>Income covers upkeep this turn (or there is no upkeep).</summary>
    Healthy,

    /// <summary>Income is below upkeep but stored gold will cover the
    /// shortfall on the owner's next turn. Reserves are bleeding.</summary>
    NegativeDelta,

    /// <summary>Even with the income about to be collected, the treasury
    /// can't cover next turn's upkeep — every unit will die.</summary>
    BankruptNextTurn,
}

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
    /// Forecast whether <paramref name="territory"/> will go bankrupt on
    /// its owner's next turn. Mirrors the turn-start sequence in
    /// <see cref="GameController"/>: income is collected first
    /// (<see cref="Treasury.CollectIncomeFor"/> adds
    /// <see cref="TreeRules.CountIncomeProducingTiles"/>), then
    /// <see cref="ApplyUpkeep"/> runs — bankrupt iff available &lt; owed,
    /// and owed == 0 is never bankrupt.
    ///
    /// Scoped to capital territories (returns false when there is no
    /// capital): the only consumer is the economic-report HUD label,
    /// which is itself only rendered for territories that have a capital.
    /// This is a forecast over the current static territory — it does
    /// not model start-of-turn tree growth or intervening captures.
    /// </summary>
    public static bool ForecastBankruptNextTurn(Territory territory, HexGrid grid, Treasury treasury)
    {
        if (!territory.HasCapital) return false;

        int owed = TotalUpkeepFor(territory, grid);
        if (owed == 0) return false;

        int income = TreeRules.CountIncomeProducingTiles(territory, grid);
        int available = treasury.GetGold(territory.Capital!.Value) + income;
        return available < owed;
    }

    /// <summary>
    /// Classify <paramref name="territory"/>'s near-term economy:
    ///   <see cref="EconomyOutlook.BankruptNextTurn"/> when
    ///     <see cref="ForecastBankruptNextTurn"/> holds;
    ///   <see cref="EconomyOutlook.NegativeDelta"/> when reserves cover
    ///     next turn but per-turn income &lt; upkeep (bleeding);
    ///   <see cref="EconomyOutlook.Healthy"/> otherwise (including
    ///     no-capital and no-units, neither of which can lose money).
    /// Pure economic facts; the view decides which outlooks deserve a
    /// warning tint based on owner kind.
    /// </summary>
    public static EconomyOutlook Classify(Territory territory, HexGrid grid, Treasury treasury)
    {
        if (!territory.HasCapital) return EconomyOutlook.Healthy;

        int owed = TotalUpkeepFor(territory, grid);
        if (owed == 0) return EconomyOutlook.Healthy;

        int income = TreeRules.CountIncomeProducingTiles(territory, grid);
        int available = treasury.GetGold(territory.Capital!.Value) + income;
        if (available < owed) return EconomyOutlook.BankruptNextTurn;
        if (income < owed) return EconomyOutlook.NegativeDelta;
        return EconomyOutlook.Healthy;
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
