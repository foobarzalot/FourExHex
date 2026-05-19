using System.Collections.Generic;

/// <summary>
/// What kind of agent controls a player slot. Human means the
/// controller waits for input; the other values are automatic
/// turn-drivers dispatched through <see cref="AiDispatcher"/>.
/// </summary>
public enum AiKind
{
    Human,
    Random,
    Heuristic,
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
    public AiKind Kind { get; }

    /// <summary>
    /// Convenience: true iff this slot is any kind of AI. Equivalent
    /// to <c>Kind != AiKind.Human</c>. Kept for call sites like
    /// <see cref="GameController"/>'s "auto-drive AI players" loop
    /// that don't care which flavor of AI is active.
    /// </summary>
    public bool IsAi => Kind != AiKind.Human;

    public Player(string name, PlayerId id, AiKind kind = AiKind.Human)
    {
        Name = name;
        Id = id;
        Kind = kind;
    }

    /// <summary>
    /// Backwards-compatible constructor used by existing tests that
    /// only care about human/AI, not which AI flavor. Maps
    /// <c>isAi: true</c> to <see cref="AiKind.Random"/>, which is
    /// what those tests have always been exercising under the
    /// default chooser.
    /// </summary>
    public Player(string name, PlayerId id, bool isAi)
        : this(name, id, isAi ? AiKind.Random : AiKind.Human)
    {
    }

    /// <summary>
    /// Build the canonical 6-player roster the game scene uses,
    /// mapping each slot's <see cref="AiKind"/> from
    /// <see cref="GameSettings.PlayerKinds"/>. Falls back to
    /// <see cref="AiKind.Heuristic"/> for any slot the kinds array
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
            AiKind kind = i < GameSettings.PlayerKinds.Length
                ? GameSettings.PlayerKinds[i]
                : AiKind.Heuristic;
            players.Add(new Player(name, PlayerId.FromIndex(i), kind));
        }
        return players;
    }

    /// <summary>
    /// Build the same 6-slot roster but force every slot to
    /// <see cref="AiKind.Human"/>. The map editor and tutorial
    /// builder scenes use this to suppress AI turn-driving while
    /// they share the play harness for previews/recordings.
    /// </summary>
    public static List<Player> BuildAllHumanRoster()
    {
        var players = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            string name = GameSettings.PlayerConfig[i].Name;
            players.Add(new Player(name, PlayerId.FromIndex(i), AiKind.Human));
        }
        return players;
    }
}
