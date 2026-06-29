/// <summary>
/// Selectable runtime game mode. Distinct from the freeform-vs-campaign
/// split (which is carried by <see cref="GameSettings.CampaignLevel"/>) —
/// <see cref="Mode"/> changes the rules that run during play, not how the
/// roster/map were built. Lives on <see cref="GameState"/> so the
/// controller's win checks and start-of-turn processing can branch on it,
/// and round-trips through the save format.
/// </summary>
public enum GameMode
{
    /// <summary>Standard rules: domination / sole-capital / claim-victory wins.</summary>
    Freeform = 0,

    /// <summary>
    /// "Rising Tides": at the start of each owner's turn a shore
    /// tile of theirs submerges (mountains demote first), shrinking the map.
    /// All early-win paths are suppressed; the game ends only when one player
    /// is left standing.
    /// </summary>
    RisingTides = 1,
}
