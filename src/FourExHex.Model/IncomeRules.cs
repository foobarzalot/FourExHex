/// <summary>
/// Single source of truth for a territory's per-turn gold income. Both real
/// play (<see cref="Treasury.CollectIncomeFor"/>) and the AI 1-ply lookahead
/// (<see cref="AiStateScorer"/>) route through this so simulated scoring can
/// never drift from the income the player actually collects. Godot-free and
/// integer-only (no-floats rule).
/// </summary>
public static class IncomeRules
{
    /// <summary>
    /// Gold a territory yields in one turn: the count of income-producing
    /// tiles (<see cref="TreeRules.CountIncomeProducingTiles"/> — trees and
    /// graves don't pay). Income is NOT difficulty-scaled: the difficulty
    /// handicap acts purely through unit upkeep
    /// (<see cref="DifficultyRules.UnitUpkeep"/>). If an earn-rate lever is
    /// ever reintroduced, it goes here so every consumer inherits it.
    /// </summary>
    public static int IncomeFor(Territory territory, HexGrid grid)
        => TreeRules.CountIncomeProducingTiles(territory, grid);
}
