/// <summary>
/// A tree on a hex tile. Blocks income on its tile (the tile doesn't
/// count for its territory's gold collection) and doesn't block unit
/// placement — moving a unit onto a tree clears it, consuming the
/// unit's action. New trees appear only at the START of a player's
/// turn via <see cref="TreeRules.RunStartOfTurnGrowth"/>: graves on
/// the starting player's tiles convert to trees, and any empty cell
/// of that color with two or more neighboring trees becomes a tree.
/// </summary>
public sealed class Tree : HexOccupant
{
}
