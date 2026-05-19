/// <summary>
/// Marker on a tile where a unit recently died. Doesn't contribute to
/// defense. Doesn't block placement the way a capital or tower does,
/// but burying a grave by moving or buy-placing a unit onto it
/// consumes the unit's action for the turn (the same as chopping a
/// tree). A grave converts into a <see cref="Tree"/> at the end of
/// the turn belonging to the player whose color the tile shares —
/// see <see cref="TreeRules.ConvertGravesToTrees"/>.
/// </summary>
public sealed class Grave : HexOccupant
{
}
