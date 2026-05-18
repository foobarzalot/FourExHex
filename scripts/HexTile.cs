using Godot;

/// <summary>
/// One hex on the board. Pure game-state model (coordinate, owner color,
/// occupant) — no view coupling. The rendered fill is kept in sync by the
/// view's <c>RebuildAfterTerritoryChange</c> (the coalesced repaint path),
/// NOT by a setter side-effect.
/// </summary>
public class HexTile
{
    public HexCoord Coord { get; }

    /// <summary>The tile's owner color. Plain state — changing it does
    /// not repaint anything; the view resyncs fills on its next
    /// <c>RebuildAfterTerritoryChange</c>.</summary>
    public Color Color { get; set; }

    /// <summary>
    /// The thing occupying this tile (unit, capital, later tower/tree/grave),
    /// or null if the tile is empty. A tile may hold at most one occupant.
    /// </summary>
    public HexOccupant? Occupant { get; set; }

    /// <summary>
    /// Convenience read-only accessor: the tile's occupant cast to
    /// <see cref="global::Unit"/>, or null if the occupant is something
    /// else (capital, tower, etc.) or the tile is empty. For setting, use
    /// <see cref="Occupant"/>.
    /// </summary>
    public Unit? Unit => Occupant as Unit;

    public HexTile(HexCoord coord, Color color)
    {
        Coord = coord;
        Color = color;
    }
}
