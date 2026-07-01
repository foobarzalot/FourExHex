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
    /// Ordinary win conditions apply (domination as in every mode); only the
    /// end-of-turn check swaps to last-player-standing rather than
    /// sole-capital, so a self-drowning tide crowns the surviving opponent.
    /// </summary>
    RisingTides = 1,

    /// <summary>
    /// "Fog Of War": exactly one human player (the rest computer). The board is
    /// hidden from the human except their own territory plus a one-hex ring;
    /// previously-seen tiles render as a dimmed last-seen memory, never-seen
    /// tiles render nothing. A view-only restriction — rules, AI, and
    /// determinism are identical to <see cref="Freeform"/>.
    /// </summary>
    FogOfWar = 2,
}
