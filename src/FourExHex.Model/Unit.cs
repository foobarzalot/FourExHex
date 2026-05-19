/// <summary>
/// The four Slay unit levels, ordered so that <c>(int)level</c> equals
/// both the level number and the defense contribution.
/// </summary>
public enum UnitLevel
{
    Peasant = 1,
    Spearman = 2,
    Knight = 3,
    Baron = 4,
}

/// <summary>
/// A unit occupying a single hex. Higher-level units are produced by
/// combining lower-level ones (see
/// <see cref="UnitLevelExtensions.CombinedWith"/>). Direct purchase of
/// levels above Peasant is not yet supported.
/// </summary>
public class Unit : HexOccupant
{
    public UnitLevel Level { get; }
    public PlayerId Owner { get; }

    /// <summary>
    /// True if this unit has already spent its one movement action this turn.
    /// Reset at the start of its owner's next turn. Newly bought units start
    /// at true because buy-and-place consumes the purchase's single action.
    /// </summary>
    public bool HasMovedThisTurn { get; set; }

    public Unit(PlayerId owner, UnitLevel level = UnitLevel.Peasant)
    {
        Owner = owner;
        Level = level;
    }
}

/// <summary>
/// Combining rules on <see cref="UnitLevel"/>. Two units can be combined
/// iff the sum of their levels is at most Baron (4). The result is the
/// level at that sum.
/// </summary>
public static class UnitLevelExtensions
{
    public static bool CanCombineWith(this UnitLevel a, UnitLevel b) =>
        (int)a + (int)b <= (int)UnitLevel.Baron;

    /// <summary>
    /// Precondition: <see cref="CanCombineWith"/> returned true for this
    /// pair. Returns the combined level.
    /// </summary>
    public static UnitLevel CombinedWith(this UnitLevel a, UnitLevel b) =>
        (UnitLevel)((int)a + (int)b);
}
