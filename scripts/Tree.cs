/// <summary>
/// A tree on a hex tile. Blocks income on its tile (the tile doesn't
/// count for its territory's gold collection) and doesn't block unit
/// placement — moving a unit onto a tree clears it, consuming the
/// unit's action. Trees spread at the end of each turn via
/// <see cref="TreeRules.SpreadTrees"/> and grow from graves on the
/// owning player's tiles at that player's end-of-turn via
/// <see cref="TreeRules.ConvertGravesToTrees"/>.
/// </summary>
public sealed class Tree : HexOccupant
{
}
