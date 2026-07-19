// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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
    // Muted, heraldic player fills tuned so dark on-tile glyphs — black units,
    // the slate tower, grey mountain rock — read clearly on top; Brown is a
    // saturated chocolate red-brown. Retired hexes are aliased in
    // SaveSerializer.RetiredHexAliases for legacy saves.
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
    /// (difficulty lever). Read by <see cref="Player.BuildRoster"/>
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
    /// Mirror a loaded save's player roster (kinds + difficulties) into
    /// <see cref="PlayerKinds"/> / <see cref="Difficulties"/> so the menu
    /// reflects them next open and a "Play Again" preserves the saved kinds.
    /// Copies up to the shorter of the roster and the slot arrays.
    /// </summary>
    public static void AdoptRosterFrom(LoadedSave loaded)
    {
        for (int i = 0; i < loaded.Players.Count && i < PlayerKinds.Length; i++)
        {
            PlayerKinds[i] = loaded.Players[i].Kind;
            Difficulties[i] = loaded.Players[i].Difficulty;
        }
    }

    /// <summary>
    /// Master seed for grid generation and per-turn RNG. Written by
    /// the main menu's "Map Seed" field before scene change; null
    /// means "auto-pick" (used by the FOUREXHEX_6AI diagnostic path
    /// which skips the menu entirely).
    /// </summary>
    public static int? MasterSeed = null;

    /// <summary>
    /// Forest density (percent of land) for a freshly-generated map.
    /// Written by the New Game "Map Setup" stepper (and the map editor's own
    /// stepper); read by <see cref="Main"/> / the map thumbnail / the editor die
    /// into the <c>MapGenOptions</c> passed to <c>MapGenerator</c>. Defaults to 5
    /// (5% of land).
    /// </summary>
    public static int TreeDensity = 5;

    /// <summary>
    /// Mountain-range density (percent of land) for a freshly-generated map.
    /// Defaults to 0 (off). See <see cref="TreeDensity"/> for the write/read path.
    /// </summary>
    public static int MountainDensity = 0;

    /// <summary>
    /// Gold-cluster density (percent of land) for a freshly-generated map.
    /// Defaults to 0 (off). See <see cref="TreeDensity"/> for
    /// the write/read path.
    /// </summary>
    public static int GoldDensity = 0;

    /// <summary>
    /// Total neutral coverage target (percent of land, 0..75) for a
    /// freshly-generated map: the share of land left unclaimed, features
    /// included. Players expand from seeds into the all-neutral map to equal
    /// quotas (claiming mountains freely, gold only as a last resort) and the
    /// unclaimed remainder stays neutral. Defaults to 0 (off). See
    /// <see cref="TreeDensity"/> for the write/read path; threaded into
    /// <c>MapGenOptions.NeutralDensity</c>.
    /// </summary>
    public static int NeutralDensity = 0;

    /// <summary>
    /// Player-territory clumping factor (0..100) for a freshly-generated map.
    /// 0 = fragmented salt-and-pepper assignment; higher values seed fewer,
    /// larger contiguous regions
    /// (seed-flood Voronoi), 100 = one blob per player. Defaults to 0 so a fresh
    /// launch / skipped menu reproduces the baseline. See <see cref="TreeDensity"/>
    /// for the write/read path; threaded into <c>MapGenOptions.ClumpingFactor</c>.
    /// </summary>
    public static int ClumpingFactor = 0;

    /// <summary>
    /// Campaign level index (0..255) when the next game is a campaign
    /// launch, null for freeform games. Written by the
    /// campaign screen alongside <see cref="MasterSeed"/> (identity
    /// mapping: seed = level); cleared by the freeform Start Game path
    /// so ordinary games never record campaign results. <c>Main</c>
    /// reads it to wire the win/loss bookkeeping and the campaign
    /// variant of the victory overlay.
    /// </summary>
    public static int? CampaignLevel = null;

    /// <summary>
    /// Selectable game mode for the next freeform launch. Written
    /// by the New Game "Map Setup" stepper's mode selector; read by
    /// <see cref="Main"/> into the <c>GameState</c> it builds. Defaults to
    /// <see cref="GameMode.Freeform"/> and is reset to Freeform on the
    /// freeform/Quick Play start paths (mirroring how <see cref="CampaignLevel"/>
    /// is cleared) so an ordinary game is never accidentally a Rising Tides one.
    /// Campaign launches always run Freeform rules.
    /// </summary>
    public static GameMode Mode = GameMode.Freeform;
}
