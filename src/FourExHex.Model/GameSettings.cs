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
    // original neon primaries. Brown was re-tuned so the dark on-tile
    // glyphs — black units, the slate tower, grey mountain rock — read
    // clearly on top (issue #62). Rather than a plain lift (which drifted
    // toward the warm neutral grey 888378), it was made a saturated, lighter
    // version of its original hue: Brown 8a5a2b -> a3582c (chocolate
    // red-brown). Its PlayerDark border companion was re-derived to match in
    // PlayerPalette, and the old hex is aliased for legacy save migration in
    // SaveSerializer.RetiredHexAliases.
    public static readonly (string Name, string Hex)[] PlayerConfig =
    {
        ("Red",    "cd473f"),
        ("Blue",   "367cbf"),
        ("Green",  "50985c"),
        ("Brown",  "a3582c"),
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
    /// One <see cref="Difficulty"/> per slot in <see cref="PlayerConfig"/>
    /// (issue #11 difficulty lever). Read by <see cref="Player.BuildRoster"/>
    /// onto each <see cref="Player.Difficulty"/>. The New Game panel writes the
    /// chosen level to every human slot (AIs stay Soldier); a loaded save mirrors
    /// each player's saved level here before BuildRoster. Defaults to all-Soldier
    /// so a fresh launch is unchanged; the headless <c>FOUREXHEX_DIFFICULTY</c>
    /// env var (see <see cref="Main"/>) overwrites it for AI-stress tests.
    /// </summary>
    public static Difficulty[] Difficulties =
    {
        Difficulty.Soldier, Difficulty.Soldier, Difficulty.Soldier,
        Difficulty.Soldier, Difficulty.Soldier, Difficulty.Soldier,
    };

    /// <summary>
    /// Master seed for grid generation and per-turn RNG. Written by
    /// the main menu's "Map Seed" field before scene change; null
    /// means "auto-pick" (used by the FOUREXHEX_6AI diagnostic path
    /// which skips the menu entirely).
    /// </summary>
    public static int? MasterSeed = null;

    /// <summary>
    /// Forest density (percent of land) for a freshly-generated map (issue #66).
    /// Written by the New Game "Map Setup" stepper (and the map editor's own
    /// stepper); read by <see cref="Main"/> / the map thumbnail / the editor die
    /// into the <c>MapGenOptions</c> passed to <c>MapGenerator</c>. Defaults to 5
    /// (the historical <c>grid.Count / 20</c> scatter) so a fresh launch / skipped
    /// menu generates the pre-#48 baseline map.
    /// </summary>
    public static int TreeDensity = 5;

    /// <summary>
    /// Mountain-range density (percent of land) for a freshly-generated map
    /// (issue #48 / #66). Defaults to 0 (off) so a fresh launch generates the
    /// pre-#48 baseline map. See <see cref="TreeDensity"/> for the write/read path.
    /// </summary>
    public static int MountainDensity = 0;

    /// <summary>
    /// Gold-cluster density (percent of land) for a freshly-generated map
    /// (issue #48 / #66). Defaults to 0 (off). See <see cref="TreeDensity"/> for
    /// the write/read path.
    /// </summary>
    public static int GoldDensity = 0;

    /// <summary>
    /// Player-territory clumping factor (0..100) for a freshly-generated map
    /// (issue #72). 0 = today's fragmented salt-and-pepper assignment (the
    /// pre-#72 baseline); higher values seed fewer, larger contiguous regions
    /// (seed-flood Voronoi), 100 = one blob per player. Defaults to 0 so a fresh
    /// launch / skipped menu reproduces the baseline. See <see cref="TreeDensity"/>
    /// for the write/read path; threaded into <c>MapGenOptions.ClumpingFactor</c>.
    /// </summary>
    public static int ClumpingFactor = 0;

    /// <summary>
    /// Campaign level index (0..255) when the next game is a campaign
    /// launch (issue #2), null for freeform games. Written by the
    /// campaign screen alongside <see cref="MasterSeed"/> (identity
    /// mapping: seed = level); cleared by the freeform Start Game path
    /// so ordinary games never record campaign results. <c>Main</c>
    /// reads it to wire the win/loss bookkeeping and the campaign
    /// variant of the victory overlay.
    /// </summary>
    public static int? CampaignLevel = null;
}
