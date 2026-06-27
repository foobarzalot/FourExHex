/// <summary>
/// One locked-in tide action for the current player's turn (issue #85). Rising
/// Tides now <i>forecasts</i> the erosion at the start of a player's turn and
/// only <i>applies</i> it at the end, so the player (and the AI) have a full
/// turn of foreknowledge. A <see cref="TideStep"/> records which tile is doomed
/// and whether this turn's hit merely demotes a mountain (a reprieve) or
/// actually submerges the tile.
/// </summary>
/// <param name="Coord">The shore tile selected to erode this turn.</param>
/// <param name="DemoteOnly">
/// True iff the tile was a mountain at forecast time, so this turn's erosion
/// only clears its mountain status (it can submerge on a later turn). False
/// means the tile will actually submerge. Integer/bool only — no floats, so
/// this stays in the model assembly.
/// </param>
public readonly record struct TideStep(HexCoord Coord, bool DemoteOnly);
