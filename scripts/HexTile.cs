using Godot;

/// <summary>
/// One hex on the board. Holds game state (coordinate, color/owner) and a
/// reference back to the Polygon2D that renders it.
/// </summary>
public class HexTile
{
    public HexCoord Coord { get; }
    public Color Color { get; set; }
    public Polygon2D? Visual { get; set; }

    public HexTile(HexCoord coord, Color color)
    {
        Coord = coord;
        Color = color;
    }
}
