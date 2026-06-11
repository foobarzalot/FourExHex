/// <summary>
/// Per-player difficulty level (issue #11), named after the unit ranks
/// (Recruit = easiest … Commander = hardest). A self-imposed handicap on the
/// HUMAN player's economy: it scales the upkeep their units cost via
/// <see cref="DifficultyRules.UnitUpkeep"/> (earn rate is flat for now). The
/// New Game panel sets one level for the human; AI opponents always play at
/// <see cref="Soldier"/> (the baseline). Storage is per-player so a future
/// UI can vary it per slot.
/// </summary>
public enum Difficulty
{
    Recruit,
    Soldier,
    Captain,
    Commander,
}
