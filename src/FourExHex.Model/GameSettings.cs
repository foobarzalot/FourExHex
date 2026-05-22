/// <summary>
/// Process-wide game configuration shared between the main menu and
/// the in-game scene. Pure C# (no Godot references) so it can live
/// in the test project — but tests have no reason to read or write
/// it; this class exists purely to hand player-role choices from
/// the main menu to <see cref="Main"/> across a scene change.
/// </summary>
public static class GameSettings
{
    /// <summary>
    /// Fixed roster of player slots. Order is preserved across the
    /// menu, the game, and the turn order. Colors are stored as hex
    /// strings so this class can stay Godot-free.
    /// </summary>
    // Each fill is a 50% lerp between the original saturated palette
    // (Red e53935 etc.) and the fully-heraldic muted palette (Gules
    // b65649 etc.) — restoring enough chroma that the colors don't read
    // as washed-out while keeping them noticeably calmer than the
    // original neon primaries.
    public static readonly (string Name, string Hex)[] PlayerConfig =
    {
        ("Red",    "cd473f"),
        ("Blue",   "367cbf"),
        ("Green",  "50985c"),
        ("Yellow", "e3bc3b"),
        ("Purple", "843890"),
        ("Orange", "db8221"),
    };

    /// <summary>
    /// One entry per slot in <see cref="PlayerConfig"/> specifying
    /// who controls that slot. The main menu writes this before
    /// switching to the game scene; <see cref="Main"/> reads it
    /// when building players. Defaults to "Player 1 human, everyone
    /// else Computer" so a fresh launch still works even if the
    /// menu is skipped.
    /// </summary>
    public static PlayerKind[] PlayerKinds =
    {
        PlayerKind.Human,     // Red
        PlayerKind.Computer, // Blue
        PlayerKind.Computer, // Green
        PlayerKind.Computer, // Yellow
        PlayerKind.Computer, // Purple
        PlayerKind.Computer, // Orange
    };

    /// <summary>
    /// Master seed for grid generation and per-turn RNG. Written by
    /// the main menu's "Map Seed" field before scene change; null
    /// means "auto-pick" (used by the FOUREXHEX_6AI diagnostic path
    /// which skips the menu entirely).
    /// </summary>
    public static int? MasterSeed = null;
}
