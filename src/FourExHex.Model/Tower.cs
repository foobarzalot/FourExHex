// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// A defensive tower on a hex tile. Radiates defense level 2 (the
/// same as a Soldier) to its own tile and every adjacent
/// same-territory tile — one rank stronger than a Capital. Towers
/// have no upkeep, so a territory's bankruptcy does not destroy
/// them; only a capturing attacker of level 3+ (Captain or Commander)
/// can take a tower tile, which overwrites the Tower occupant with
/// the arriving unit. Towers block friendly placement completely:
/// a unit cannot move or be bought onto a tower-occupied own tile.
/// Towers can exist on singleton tiles (unlike Capitals) — they
/// are structural, not tied to territory size.
/// </summary>
public sealed class Tower : HexOccupant
{
}
