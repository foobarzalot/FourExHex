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
    /// Per-slot darker companion used for the 1.2px hex border stroke. Indices
    /// align with <see cref="GameSettings.PlayerConfig"/>; the heraldic palette
    /// spec pairs each `fill` with a matching `dk`.
    /// </summary>
    private static readonly Color[] PlayerDark =
    {
        new Color("7c3329"), // Gules dk
        new Color("33506f"), // Azure dk
        new Color("3d6750"), // Vert dk
        new Color("967426"), // Or dk
        new Color("553355"), // Purpure dk
        new Color("8a5326"), // Tenné dk
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
