using Godot;

public enum UnitLevel
{
    Peasant = 1,
    Spearman = 2,
    Knight = 3,
    Baron = 4,
}

/// <summary>
/// A unit occupying a single hex. Level determines offensive strength,
/// defensive contribution, and (later) upkeep cost. Owner is the color
/// of the player that controls it — matches <see cref="Player.Color"/>.
/// </summary>
public class Unit
{
    public UnitLevel Level { get; }
    public Color Owner { get; }

    public Unit(UnitLevel level, Color owner)
    {
        Level = level;
        Owner = owner;
    }
}
