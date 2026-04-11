/// <summary>
/// Marker on a tile where a unit recently died. Doesn't contribute to
/// defense, doesn't block placement (a unit moving or being bought
/// onto a grave replaces it), and is cleared automatically at the end
/// of each turn. In Step 13 graves will convert into pine trees
/// instead of disappearing.
/// </summary>
public sealed class Grave : HexOccupant
{
}
