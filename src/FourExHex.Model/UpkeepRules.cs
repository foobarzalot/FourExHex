using System.Collections.Generic;

/// <summary>
/// Per-turn upkeep rules. Each unit level has a fixed upkeep cost paid
/// from its containing territory's treasury at the start of the owner's
/// turn. When a territory can't cover its total upkeep, every unit in
/// the territory dies (the territory is "bankrupt"). The remaining gold
/// stays in the treasury.
///
/// Upkeep is a flat per-level table (per Slay): Recruit = 2, Soldier = 6,
/// Captain = 18, Commander = 54 — the same for every player.
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
    /// <summary>
    /// The upkeep cost a single unit of the given level demands per turn.
    /// Explicit hand-picked integers (no percent formula, no truncation
    /// arithmetic); this is per-unit (not per-territory-total) so AiCommon's
    /// combine/buy upkeep-delta arithmetic stays exactly consistent with
    /// totals and real charging.
    /// </summary>
    public static int UpkeepFor(UnitLevel level) => level switch
    {
        UnitLevel.Recruit => 2,
        UnitLevel.Soldier => 6,
        UnitLevel.Captain => 18,
        UnitLevel.Commander => 54,
        _ => 0,
    };

    /// <summary>
    /// Number of upkeep steps the AI's solvency check looks ahead.
    /// One-step lookahead let the AI take buys whose post-state
    /// barely survived next upkeep but drained the treasury within
    /// a few more turns — the doom spiral. A 5-step horizon
    /// forces the AI to keep enough runway to absorb the new
    /// upkeep over several turns, not just one.
    /// </summary>
    public const int UpkeepHorizon = 5;

    /// <summary>
    /// True iff a territory holding <paramref name="gold"/> gold at
    /// <paramref name="netIncome"/> per turn can sustain itself
    /// across the next <see cref="UpkeepHorizon"/> upkeep steps —
    /// i.e., gold + <see cref="UpkeepHorizon"/> × netIncome ≥ 0.
    /// Positive or zero net income trivially survives regardless of
    /// gold (the multiplication can't push it negative).
    ///
    /// The single shared solvency predicate used by
    /// <see cref="AiStateScorer"/>'s bankruptcy lookahead and by every
    /// gate in <see cref="AiCommon.Enumerate"/>. Both layers must
    /// agree on what counts as solvent — otherwise the scorer
    /// approves actions the enumerator never proposes, or vice versa.
    /// Tuning the horizon (or moving to a graduated discount) is a
    /// one-line edit here that the entire system inherits.
    /// </summary>
    public static bool SurvivesNextUpkeep(int gold, int netIncome) =>
        gold + UpkeepHorizon * netIncome >= 0;

    /// <summary>Sum of upkeep costs for every unit in <paramref name="territory"/>.
    /// Neutral (<see cref="PlayerId.None"/>-owned) territories always owe zero:
    /// viking units are upkeep-exempt (they never bankrupt, so they never leave
    /// graves), and this is the single chokepoint every upkeep/solvency path
    /// reads.</summary>
    public static int TotalUpkeepFor(Territory territory, HexGrid grid)
    {
        if (territory.Owner.IsNone) return 0;
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
    /// Classify <paramref name="territory"/>'s near-term economy:
    ///   <see cref="EconomyOutlook.BankruptNextTurn"/> when reserves plus
    ///     next turn's income cannot cover upkeep;
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

        int income = IncomeRules.IncomeFor(territory, grid);
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

        // Bankrupt — every unit in the territory dies, leaving a grave.
        // Graves and mountains coexist, so a unit dying on a
        // mountain leaves a grave the same as ordinary ground. Capital
        // occupants and other non-unit occupants survive untouched.
        int killed = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile?.Occupant is Unit)
            {
                tile.Occupant = new Grave();
                killed++;
            }
        }
        Log.Info(Log.LogCategory.Turn,
            $"[upkeep] BANKRUPT owner={territory.Owner.Index} " +
            $"cap={(territory.HasCapital ? territory.Capital!.Value.ToString() : "none")} " +
            $"size={territory.Coords.Count} units_killed={killed} " +
            $"owed={owed} available={available}");
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
        => ApplyUpkeepFor(player.Id, territories, grid, treasury);

    /// <summary>
    /// Owner-id overload of <see cref="ApplyUpkeepFor(Player, IEnumerable{Territory}, HexGrid, Treasury)"/>,
    /// for callers that hold an owner without a <see cref="Player"/> object —
    /// notably the neutral owner (<see cref="PlayerId.None"/>), which takes a
    /// phantom turn each round. Neutral territories never hold units, so this
    /// is a no-op for them (nothing owed), but it lets neutral go through the
    /// exact same phantom-turn path as an eliminated player.
    /// </summary>
    public static bool ApplyUpkeepFor(
        PlayerId ownerId, IEnumerable<Territory> territories, HexGrid grid, Treasury treasury)
    {
        bool anyBankrupt = false;
        foreach (Territory territory in territories)
        {
            if (territory.Owner != ownerId) continue;
            if (!ApplyUpkeep(territory, grid, treasury))
            {
                anyBankrupt = true;
            }
        }
        return anyBankrupt;
    }
}
