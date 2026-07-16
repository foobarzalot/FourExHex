// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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
        GameMode.RisingTides => Strings.Get(StringKeys.IntroRisingTides),
        GameMode.FogOfWar => Strings.Get(StringKeys.IntroFogOfWar),
        GameMode.VikingRaiders => Strings.Get(StringKeys.IntroVikingRaiders),
        _ => null,
    };

    /// <summary>
    /// True when this mode has an intro AND the player hasn't dismissed it yet.
    /// </summary>
    public static bool ShouldShow(GameMode mode) =>
        TextFor(mode) != null && !UserSettings.HasSeenModeIntro(mode);
}
