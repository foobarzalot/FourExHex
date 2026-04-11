using Godot;

/// <summary>
/// A unit occupying a single hex. For now all units are "peasants" —
/// level distinctions (spearman/knight/baron) will be reintroduced when
/// combining lands in Step 11.
/// </summary>
public class Unit : HexOccupant
{
    public Color Owner { get; }

    /// <summary>
    /// True if this unit has already spent its one movement action this turn.
    /// Reset at the start of its owner's next turn. Newly bought units start
    /// at true because buy-and-place consumes the purchase's single action.
    /// </summary>
    public bool HasMovedThisTurn { get; set; }

    public Unit(Color owner)
    {
        Owner = owner;
    }
}
