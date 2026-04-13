/// <summary>
/// Process-wide game configuration shared between the main menu and
/// the in-game scene. Pure C# (no Godot references) so it can live in
/// the test project — but tests have no reason to read or write it;
/// this class exists purely to hand player-role choices from the
/// main menu to <see cref="Main"/> across a scene change.
/// </summary>
public static class GameSettings
{
    /// <summary>
    /// Fixed roster of player slots. Order is preserved across the
    /// menu, the game, and the turn order. Colors are stored as hex
    /// strings so this class can stay Godot-free.
    /// </summary>
    public static readonly (string Name, string Hex)[] PlayerConfig =
    {
        ("Red",    "e53935"),
        ("Blue",   "1e88e5"),
        ("Green",  "43a047"),
        ("Yellow", "fdd835"),
        ("Purple", "8e24aa"),
        ("Orange", "fb8c00"),
    };

    /// <summary>
    /// One entry per slot in <see cref="PlayerConfig"/>; <c>true</c>
    /// means the slot is an AI, <c>false</c> means a human. The main
    /// menu writes this; <see cref="Main"/> reads it when building
    /// players. Defaults to the original "Player 1 human, everyone
    /// else AI" config so a fresh launch still works if the menu is
    /// skipped.
    /// </summary>
    public static bool[] PlayerIsAi =
    {
        false, // Red = human
        true,  // Blue
        true,  // Green
        true,  // Yellow
        true,  // Purple
        true,  // Orange
    };
}
