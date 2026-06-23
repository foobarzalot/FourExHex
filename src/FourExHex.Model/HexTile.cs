/// <summary>
/// One hex on the board. Pure game-state model (coordinate, owner,
/// occupant) — no view coupling. The rendered fill is kept in sync by the
/// view's <c>RebuildAfterTerritoryChange</c> (the coalesced repaint path),
/// NOT by a setter side-effect.
/// </summary>
public class HexTile
{
    public HexCoord Coord { get; }

    /// <summary>The tile's owning player (<see cref="PlayerId.None"/> if
    /// unowned). Plain state — changing it does not repaint anything; the
    /// view resyncs fills on its next <c>RebuildAfterTerritoryChange</c>.</summary>
    public PlayerId Owner { get; set; }

    /// <summary>
    /// The thing occupying this tile (unit, capital, later tower/tree/grave),
    /// or null if the tile is empty. A tile may hold at most one occupant.
    /// </summary>
    public HexOccupant? Occupant { get; set; }

    /// <summary>
    /// A gold tile (issue #45): an income hotspot that pays its controlling
    /// player double the per-turn income of an ordinary tile. A per-tile
    /// terrain attribute, orthogonal to <see cref="Owner"/> and
    /// <see cref="Occupant"/> — a gold tile may be owned by any player or
    /// neutral, and may hold any occupant. The bonus is applied in
    /// <see cref="IncomeRules.IncomeFor"/>; like every income-producing tile,
    /// a gold tile occupied by a <see cref="Tree"/>/<see cref="Grave"/> pays
    /// nothing. Authored via the map editor and scattered as contested gold
    /// clusters by <c>MapGenerator</c> when its gold density is &gt; 0
    /// (issue #48). Defaults <c>false</c>; plain state — changing it
    /// does not repaint anything.
    /// </summary>
    public bool IsGold { get; set; }

    /// <summary>
    /// A mountain tile (issue #37): defensive terrain that contributes
    /// tower-strength defense (<see cref="DefenseRules.MountainDefense"/>) to
    /// itself and, when owned, radiates it to same-owner neighbors. A per-tile
    /// terrain attribute, orthogonal to <see cref="Owner"/>,
    /// <see cref="Occupant"/>, and <see cref="IsGold"/> — a mountain may be
    /// neutral or owned by any player, may hold a unit (units move onto,
    /// through, and die on it without leaving a grave), and may also be gold.
    /// Mountains never hold a capital or tower, trees never spread onto them,
    /// and a Captain/Commander can capture one without destroying it. No income
    /// behavior of its own. Authored via the map editor and scattered as
    /// mountain ranges by <c>MapGenerator</c> when its mountain density is
    /// &gt; 0 (issue #48). Defaults <c>false</c>; plain state — changing it
    /// does not repaint anything.
    /// </summary>
    public bool IsMountain { get; set; }

    /// <summary>
    /// Convenience read-only accessor: the tile's occupant cast to
    /// <see cref="global::Unit"/>, or null if the occupant is something
    /// else (capital, tower, etc.) or the tile is empty. For setting, use
    /// <see cref="Occupant"/>.
    /// </summary>
    public Unit? Unit => Occupant as Unit;

    public HexTile(HexCoord coord, PlayerId owner)
    {
        Coord = coord;
        Owner = owner;
    }
}
