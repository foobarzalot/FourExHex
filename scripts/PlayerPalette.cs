using Godot;

/// <summary>
/// View-layer boundary adapter: maps the Godot-free <see cref="PlayerId"/>
/// identity to a display <see cref="Color"/> and back. The model no longer
/// carries colors — the roster's display hex lives in
/// <see cref="GameSettings.PlayerConfig"/> and is realized into
/// <see cref="Godot.Color"/> only here, on the Godot side.
/// </summary>
public static class PlayerPalette
{
    /// <summary>Fill shown for unowned/neutral tiles (<see cref="PlayerId.None"/>).</summary>
    public static readonly Color Neutral = new Color("888378");

    /// <summary>Darker companion to <see cref="Neutral"/>, used for the neutral tile border stroke.</summary>
    public static readonly Color NeutralDark = new Color("615c52");

    /// <summary>
    /// Per-slot darker companion used for the hex border stroke (~fill * 0.45).
    /// Indices align with <see cref="GameSettings.PlayerConfig"/>. The dark
    /// is intentionally well below the fill in lightness so that per-tile
    /// borders within a single-owner territory remain visible against the
    /// fill instead of fading toward isoluminance.
    /// </summary>
    private static readonly Color[] PlayerDark =
    {
        new Color("5c201c"), // Red dk
        new Color("183856"), // Blue dk
        new Color("244429"), // Green dk
        new Color("66551b"), // Yellow dk
        new Color("3b1941"), // Purple dk
        new Color("633a0f"), // Orange dk
    };

    /// <summary>Display color for a player slot.</summary>
    public static Color ColorFor(PlayerId id) =>
        id.IsNone ? Neutral : new Color(GameSettings.PlayerConfig[id.Index].Hex);

    /// <summary>
    /// Darker companion to <see cref="ColorFor"/> for hex border strokes and
    /// other 2-tone player chrome.
    /// </summary>
    public static Color DarkColorFor(PlayerId id) =>
        id.IsNone ? NeutralDark : PlayerDark[id.Index];

    /// <summary>
    /// Inverse of <see cref="ColorFor"/>: the player slot whose palette
    /// color equals <paramref name="c"/>, or <see cref="PlayerId.None"/> if
    /// none matches (used by old-save loading and map-editor painting).
    /// </summary>
    public static PlayerId IdForColor(Color c)
    {
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            if (new Color(GameSettings.PlayerConfig[i].Hex) == c)
            {
                return PlayerId.FromIndex(i);
            }
        }
        return PlayerId.None;
    }
}
