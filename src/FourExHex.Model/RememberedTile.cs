/// <summary>
/// The human player's last-seen snapshot of a tile under fog of war: the
/// owner and a deep copy of the occupant captured the last time the tile was
/// in sight. Terrain (gold/mountain) is static for a game and read live, so it
/// is not remembered here.
/// </summary>
public readonly record struct RememberedTile(PlayerId Owner, HexOccupant? Occupant);
