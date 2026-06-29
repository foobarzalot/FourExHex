using System.Collections.Generic;

/// <summary>
/// What kind of agent controls a player slot. Human means the
/// controller waits for input; Computer is automatically driven by
/// <see cref="ComputerAi"/> via <see cref="AiDispatcher"/>.
/// </summary>
public enum PlayerKind
{
    Human,
    Computer,

    /// <summary>
    /// An empty slot: this color does not take part in the game.
    /// <see cref="Player.BuildRoster"/> filters <c>None</c> slots out entirely,
    /// so a <c>None</c> player never enters a live <see cref="TurnState"/> —
    /// it owns no capital, never takes a turn, and is excluded from the win
    /// check. The value exists only as roster-build input and as map-save
    /// metadata (a starting map bakes each color's kind, including which are
    /// <c>None</c>). Lets a match run with 2–6 players.
    /// </summary>
    None,
}

/// <summary>
/// A player in the game. Identified by a Godot-free <see cref="PlayerId"/>
/// (its slot in the roster). The display color is a pure view concern the
/// Godot layer maps from <see cref="Id"/>; the model never carries it. The
/// <see cref="Kind"/> field determines who drives the player's turn:
/// a human (wait for input) or one of the available AIs.
/// </summary>
public class Player
{
    public string Name { get; }
    public PlayerId Id { get; }
    public PlayerKind Kind { get; }

    /// <summary>
    /// Difficulty lever: how much upkeep this player's units
    /// cost per turn, via <see cref="DifficultyRules.UnitUpkeep"/>. Default
    /// <see cref="Difficulty.Soldier"/> = the baseline AIs always play at.
    /// Consumed by <see cref="UpkeepRules"/> (real charging), the AI
    /// solvency gates, and the lookahead scorer.
    /// </summary>
    public Difficulty Difficulty { get; }

    /// <summary>
    /// Convenience: true iff this slot is computer-controlled.
    /// Equivalent to <c>Kind == PlayerKind.Computer</c>. Used by
    /// <see cref="GameController"/>'s "auto-drive AI players" loop.
    /// </summary>
    public bool IsAi => Kind != PlayerKind.Human;

    public Player(string name, PlayerId id, PlayerKind kind = PlayerKind.Human, Difficulty difficulty = Difficulty.Soldier)
    {
        Name = name;
        Id = id;
        Kind = kind;
        Difficulty = difficulty;
    }

    /// <summary>
    /// Convenience constructor used by tests that only care whether a
    /// slot is human or computer-controlled. Maps <c>isAi: true</c> to
    /// <see cref="PlayerKind.Computer"/>.
    /// </summary>
    public Player(string name, PlayerId id, bool isAi)
        : this(name, id, isAi ? PlayerKind.Computer : PlayerKind.Human)
    {
    }

    /// <summary>
    /// Build the canonical 6-player roster the game scene uses,
    /// mapping each slot's <see cref="PlayerKind"/> from
    /// <see cref="GameSettings.PlayerKinds"/>. Falls back to
    /// <see cref="PlayerKind.Computer"/> for any slot the kinds array
    /// doesn't cover (defense in depth — the array is the same length
    /// as <see cref="GameSettings.PlayerConfig"/>, so this branch is
    /// unreachable unless someone shortens one without the other).
    /// </summary>
    public static List<Player> BuildRoster()
    {
        var players = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            string name = GameSettings.PlayerConfig[i].Name;
            PlayerKind kind = i < GameSettings.PlayerKinds.Length
                ? GameSettings.PlayerKinds[i]
                : PlayerKind.Computer;
            // A None slot is absent from the match: skip it so the
            // roster compacts to the active players only. Survivors keep their
            // original slot index via PlayerId.FromIndex(i), so display colors
            // (PlayerPalette indexes PlayerConfig by Id.Index) stay correct.
            if (kind == PlayerKind.None) continue;
            Difficulty difficulty = i < GameSettings.Difficulties.Length
                ? GameSettings.Difficulties[i]
                : Difficulty.Soldier;
            players.Add(new Player(name, PlayerId.FromIndex(i), kind, difficulty));
        }
        return players;
    }

    /// <summary>
    /// Build the same 6-slot roster but force every slot to
    /// <see cref="PlayerKind.Human"/>. The map editor and tutorial
    /// builder scenes use this to suppress AI turn-driving while
    /// they share the play harness for previews/recordings.
    /// </summary>
    public static List<Player> BuildAllHumanRoster()
    {
        var players = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            string name = GameSettings.PlayerConfig[i].Name;
            players.Add(new Player(name, PlayerId.FromIndex(i), PlayerKind.Human));
        }
        return players;
    }

    /// <summary>
    /// Build the roster for a campaign level — a deterministic, per-level subset
    /// of 2–6 colors with the single human at
    /// <see cref="CampaignProgress.HumanColorSlotForLevel"/> carrying the level's
    /// tier difficulty (<see cref="CampaignProgress.DifficultyForLevel"/>), every
    /// other active slot a Soldier Computer. The compact roster keeps each
    /// player's original color slot (`PlayerId.FromIndex`). Derived purely from
    /// the level (never the freeform <see cref="GameSettings.PlayerKinds"/>), so
    /// the same level always plays the same players and a campaign launch can't
    /// change your New Game default.
    /// </summary>
    public static List<Player> BuildCampaignRoster(int level)
    {
        int humanSlot = CampaignProgress.HumanColorSlotForLevel(level);
        Difficulty humanDifficulty = CampaignProgress.DifficultyForLevel(level);
        var players = new List<Player>();
        foreach (int slot in CampaignProgress.ActiveColorSlotsForLevel(level))
        {
            bool isHuman = slot == humanSlot;
            players.Add(new Player(
                GameSettings.PlayerConfig[slot].Name,
                PlayerId.FromIndex(slot),
                isHuman ? PlayerKind.Human : PlayerKind.Computer,
                isHuman ? humanDifficulty : Difficulty.Soldier));
        }
        return players;
    }
}
