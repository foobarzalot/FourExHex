using Godot;

/// <summary>
/// A player in the game. For now just a display name and a color — gold,
/// territories, units, etc. will be added in later steps.
/// </summary>
public class Player
{
    public string Name { get; }
    public Color Color { get; }

    public Player(string name, Color color)
    {
        Name = name;
        Color = color;
    }
}
