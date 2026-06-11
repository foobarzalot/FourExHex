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
    /// Difficulty lever (issue #11): how much upkeep this player's units
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
}
