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
    /// Extra gold a gold tile yields per turn on top of the
    /// ordinary 1 gp every income-producing tile pays. A bonus of 4 makes a
    /// gold tile worth 5 gp/turn (5x). Single integer constant — the one
    /// place to retune the gold earn rate.
    /// </summary>
    public const int GoldTileBonus = 4;

    /// <summary>
    /// Gold a territory yields in one turn: the count of income-producing
    /// tiles (<see cref="TreeRules.CountIncomeProducingTiles"/> — trees and
    /// graves don't pay) plus <see cref="GoldTileBonus"/> for each gold
    /// income-tile (<see cref="TreeRules.CountGoldIncomeTiles"/>). Income is
    /// NOT difficulty-scaled: the difficulty handicap acts purely through
    /// purchase costs (<see cref="DifficultyRules.UnitBaseCost"/> /
    /// <see cref="DifficultyRules.TowerCost"/>). The gold earn-rate lever
    /// lives here so every consumer (real play + AI lookahead) inherits it.
    /// </summary>
    public static int IncomeFor(Territory territory, HexGrid grid)
    {
        int baseIncome = TreeRules.CountIncomeProducingTiles(territory, grid);
        int goldTiles = TreeRules.CountGoldIncomeTiles(territory, grid);
        int total = baseIncome + goldTiles * GoldTileBonus;

        if (goldTiles > 0)
        {
            Log.Debug(Log.LogCategory.Turn,
                $"Income: territory cap={territory.Capital} base={baseIncome} " +
                $"gold={goldTiles}(+{goldTiles * GoldTileBonus}) total={total}");
        }

        return total;
    }
}
