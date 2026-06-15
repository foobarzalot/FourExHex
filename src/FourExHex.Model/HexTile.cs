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
    /// nothing. Authored only via the map editor; never created by
    /// <c>MapGenerator</c>. Defaults <c>false</c>; plain state — changing it
    /// does not repaint anything.
    /// </summary>
    public bool IsGold { get; set; }

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
