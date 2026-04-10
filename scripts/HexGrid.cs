using System.Collections.Generic;

/// <summary>
/// Sparse hex map keyed by axial coordinate. Owns the game-state tiles; does
/// not know or care about rendering.
/// </summary>
public class HexGrid
{
    private readonly Dictionary<HexCoord, HexTile> _tiles = new();

    public int Count => _tiles.Count;
    public IEnumerable<HexTile> Tiles => _tiles.Values;

    public void Add(HexTile tile) => _tiles[tile.Coord] = tile;

    public bool Contains(HexCoord coord) => _tiles.ContainsKey(coord);

    public HexTile? Get(HexCoord coord) =>
        _tiles.TryGetValue(coord, out HexTile? tile) ? tile : null;

    /// <summary>
    /// Return the (up to six) neighbors of <paramref name="coord"/> that are
    /// actually present in the grid.
    /// </summary>
    public IEnumerable<HexTile> NeighborsOf(HexCoord coord)
    {
        foreach (HexCoord n in coord.Neighbors())
        {
            if (_tiles.TryGetValue(n, out HexTile? tile))
            {
                yield return tile;
            }
        }
    }
}
