using Godot;

/// <summary>
/// A player in the game. Identified by display name and color. The
/// <see cref="IsAi"/> flag marks whether the controller should auto-
/// drive this player's turn via <see cref="RandomAi"/> instead of
/// waiting for human input.
/// </summary>
public class Player
{
    public string Name { get; }
    public Color Color { get; }
    public bool IsAi { get; }

    public Player(string name, Color color, bool isAi = false)
    {
        Name = name;
        Color = color;
        IsAi = isAi;
    }
}
