/// <summary>
/// Copy and gate logic for the one-time game-mode intro overlay (issue #96).
/// Rising Tides and Fog Of War change how the game plays in ways that aren't
/// obvious from the board, so the first time the player starts each mode we
/// show a short explainer over the loaded map. The "seen once" flag lives in
/// <see cref="UserSettings"/>; the overlay itself is <c>Main</c> driving
/// <c>HudView.ShowTappableTutorialMessage</c>.
///
/// Pure content + predicate — no Godot nodes — so it stays trivially readable
/// and the same seam serves every launch path (campaign, custom game,
/// next-unbeaten, starting map, resume) that funnels through <c>Main</c>.
/// </summary>
public static class GameModeIntro
{
    /// <summary>
    /// The intro paragraph for <paramref name="mode"/>, or null when the mode
    /// has no explainer (Freeform). The overlay has no separate title label,
    /// so each paragraph leads with the mode name.
    /// </summary>
    public static string? TextFor(GameMode mode) => mode switch
    {
        GameMode.RisingTides =>
            "Rising Tides — the sea rises every turn. Coastal tiles submerge " +
            "and the map keeps shrinking, so ground you hold now can vanish " +
            "beneath the waves.",
        GameMode.FogOfWar =>
            "Fog of War — the map begins hidden. Your territories light up the " +
            "ground around them; everything else stays dark until you scout it. " +
            "Undo and Redo are disabled in this mode.",
        GameMode.VikingRaiders =>
            "Viking Raiders — Vikings are invading your island. Viking units " +
            "pay no upkeep but their forces can't grow after they land on " +
            "shore. Stand by to repel the invaders!",
        _ => null,
    };

    /// <summary>
    /// True when this mode has an intro AND the player hasn't dismissed it yet.
    /// </summary>
    public static bool ShouldShow(GameMode mode) =>
        TextFor(mode) != null && !UserSettings.HasSeenModeIntro(mode);
}
