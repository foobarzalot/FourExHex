/// <summary>
/// Marker on a tile where a unit recently died. Doesn't contribute to
/// defense, doesn't block placement (a unit moving or being bought
/// onto a grave replaces it without consuming the unit's action), and
/// converts into a <see cref="Tree"/> at the end of each turn via
/// <see cref="TreeRules.ConvertGravesToTrees"/>.
/// </summary>
public sealed class Grave : HexOccupant
{
}
