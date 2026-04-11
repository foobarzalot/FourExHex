/// <summary>
/// Pure calculation of the defense value covering a hex. For Step 9 there's
/// no defense radiation — a unit only defends its own tile. Higher-level
/// defense (adjacent same-territory units contributing to a tile's defense)
/// is Step 11 work.
/// </summary>
public static class DefenseRules
{
    /// <summary>
    /// Defense value of <paramref name="tile"/>. To capture a tile, the
    /// attacker's level must be strictly greater than this value.
    /// </summary>
    /// <param name="isCapital">Whether this tile is the capital of its
    /// containing territory. Capitals can never hold a unit, so their
    /// defense is simply the capital's static contribution of 1.</param>
    public static int Defense(HexTile tile, bool isCapital)
    {
        if (isCapital) return 1;
        if (tile.Unit != null) return (int)tile.Unit.Level;
        return 0;
    }
}
