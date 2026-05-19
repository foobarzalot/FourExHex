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
    public static readonly Color Neutral = new Color("888888");

    /// <summary>Display color for a player slot.</summary>
    public static Color ColorFor(PlayerId id) =>
        id.IsNone ? Neutral : new Color(GameSettings.PlayerConfig[id.Index].Hex);

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
