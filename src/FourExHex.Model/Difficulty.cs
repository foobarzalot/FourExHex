/// <summary>
/// Per-player difficulty level (issue #11). Drives how much gold a player
/// earns per turn via <see cref="DifficultyRules"/>. The New Game panel sets
/// one level globally for all AI opponents; storage is per-player so a future
/// UI can vary it per slot. Humans are always <see cref="Normal"/>.
/// </summary>
public enum Difficulty
{
    Easy,
    Normal,
    Hard,
    Brutal,
}
