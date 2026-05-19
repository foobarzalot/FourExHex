using System;

/// <summary>
/// A thing sitting on a hex. Tiles can hold at most one occupant at a
/// time. Subclasses: <see cref="Unit"/>, <see cref="Capital"/>,
/// <see cref="Tower"/>, <see cref="Tree"/>, <see cref="Grave"/>.
/// </summary>
public abstract class HexOccupant
{
    /// <summary>
    /// Deep copy of <paramref name="occupant"/>, used by snapshot types
    /// to keep restored state independent of any later mutations to the
    /// originals. Each subclass copies whatever per-instance data it has;
    /// stateless types (Capital/Grave/Tree/Tower) just produce a fresh
    /// instance.
    /// </summary>
    public static HexOccupant? Clone(HexOccupant? occupant) => occupant switch
    {
        Unit u => new Unit(u.Owner, u.Level) { HasMovedThisTurn = u.HasMovedThisTurn },
        Capital => new Capital(),
        Grave => new Grave(),
        Tree => new Tree(),
        Tower => new Tower(),
        null => null,
        _ => throw new InvalidOperationException($"Unknown occupant type: {occupant.GetType()}"),
    };
}
