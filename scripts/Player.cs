using Godot;

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
    // Scripted opponent used by the tutorial scene only — never
    // selectable from the play-config menu.
    Tutorial,
}

/// <summary>
/// A player in the game. Identified by display name and color. The
/// <see cref="Kind"/> field determines who drives the player's turn:
/// a human (wait for input) or one of the available AIs.
/// </summary>
public class Player
{
    public string Name { get; }
    public Color Color { get; }
    public AiKind Kind { get; }

    /// <summary>
    /// Convenience: true iff this slot is any kind of AI. Equivalent
    /// to <c>Kind != AiKind.Human</c>. Kept for call sites like
    /// <see cref="GameController"/>'s "auto-drive AI players" loop
    /// that don't care which flavor of AI is active.
    /// </summary>
    public bool IsAi => Kind != AiKind.Human;

    public Player(string name, Color color, AiKind kind = AiKind.Human)
    {
        Name = name;
        Color = color;
        Kind = kind;
    }

    /// <summary>
    /// Backwards-compatible constructor used by existing tests that
    /// only care about human/AI, not which AI flavor. Maps
    /// <c>isAi: true</c> to <see cref="AiKind.Random"/>, which is
    /// what those tests have always been exercising under the
    /// default chooser.
    /// </summary>
    public Player(string name, Color color, bool isAi)
        : this(name, color, isAi ? AiKind.Random : AiKind.Human)
    {
    }
}
