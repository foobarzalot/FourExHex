using Godot;

/// <summary>
/// One hex on the board. Holds game state (coordinate, color/owner) and a
/// reference back to the Polygon2D that renders it.
/// </summary>
public class HexTile
{
    public HexCoord Coord { get; }

    private Color _color;
    /// <summary>
    /// The tile's owner color. Setting this also pushes the value to
    /// <see cref="Visual"/> if a visual is attached, so the rendered fill
    /// is always in sync with the logical ownership — no separate "refresh"
    /// step required.
    /// </summary>
    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            if (Visual != null) Visual.Color = value;
        }
    }

    public Polygon2D? Visual { get; set; }

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
        _color = color;
    }
}
