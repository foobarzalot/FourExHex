// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// A capital building occupying a hex. Contributes defense 1 to its own
/// tile and radiates the same value to adjacent same-territory tiles.
/// A tile's owner color already identifies which player it belongs to,
/// so the Capital itself carries no data.
/// </summary>
public sealed class Capital : HexOccupant
{
}
