using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Immutable deep copy of the map editor's draft state (tile owners +
/// occupants, water set, territory list). Capable of restoring all three
/// onto a live <see cref="HexGrid"/> + water set, including adding tiles
/// that were removed and removing tiles that were added — the editor's
/// paint actions can do either, so the play scene's
/// <see cref="GameStateSnapshot"/> (which only updates existing tiles)
/// isn't sufficient.
/// </summary>
public sealed class EditorSnapshot
{
    private readonly struct TileState
    {
        public PlayerId Owner { get; }
        public HexOccupant? Occupant { get; }

        public TileState(PlayerId owner, HexOccupant? occupant)
        {
            Owner = owner;
            Occupant = occupant;
        }
    }

    private readonly Dictionary<HexCoord, TileState> _tiles;
    private readonly HashSet<HexCoord> _water;
    private readonly IReadOnlyList<Territory> _territories;

    private EditorSnapshot(
        Dictionary<HexCoord, TileState> tiles,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> territories)
    {
        _tiles = tiles;
        _water = water;
        _territories = territories;
    }

    public static EditorSnapshot Capture(
        HexGrid grid,
        IReadOnlySet<HexCoord> water,
        IReadOnlyList<Territory> territories)
    {
        var tiles = new Dictionary<HexCoord, TileState>();
        foreach (HexTile tile in grid.Tiles)
        {
            tiles[tile.Coord] = new TileState(tile.Owner, HexOccupant.Clone(tile.Occupant));
        }
        return new EditorSnapshot(
            tiles,
            new HashSet<HexCoord>(water),
            new List<Territory>(territories));
    }

    /// <summary>
    /// Restore this snapshot onto <paramref name="grid"/> +
    /// <paramref name="water"/>, mutating both in place. Returns the
    /// territory list at capture time for the caller to reassign.
    /// </summary>
    public IReadOnlyList<Territory> ApplyTo(HexGrid grid, HashSet<HexCoord> water)
    {
        // Snapshot the live coords first; we can't enumerate Grid.Tiles
        // while mutating it via Remove.
        List<HexCoord> liveCoords = grid.Tiles.Select(t => t.Coord).ToList();
        foreach (HexCoord c in liveCoords) grid.Remove(c);

        foreach (KeyValuePair<HexCoord, TileState> kvp in _tiles)
        {
            var tile = new HexTile(kvp.Key, kvp.Value.Owner)
            {
                Occupant = HexOccupant.Clone(kvp.Value.Occupant),
            };
            grid.Add(tile);
        }

        water.Clear();
        foreach (HexCoord c in _water) water.Add(c);

        return _territories;
    }

}
