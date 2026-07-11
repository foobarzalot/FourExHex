/// <summary>
/// Per-player difficulty level, named after the unit ranks
/// (Recruit = easiest … Commander = hardest). A self-imposed handicap on the
/// HUMAN player's economy: it scales what their units and towers cost to buy
/// via <see cref="DifficultyRules.UnitBaseCost"/> /
/// <see cref="DifficultyRules.TowerCost"/> (upkeep and earn rate are flat).
/// The New Game panel sets one level for the human; AI opponents always play
/// at <see cref="Soldier"/> (the baseline). Storage is per-player so a future
/// UI can vary it per slot.
/// </summary>
public enum Difficulty
{
    Recruit,
    Soldier,
    Captain,
    Commander,
}
