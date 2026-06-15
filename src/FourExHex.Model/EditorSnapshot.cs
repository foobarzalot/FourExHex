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
        public bool IsGold { get; }
        public bool IsMountain { get; }

        public TileState(PlayerId owner, HexOccupant? occupant, bool isGold, bool isMountain)
        {
            Owner = owner;
            Occupant = occupant;
            IsGold = isGold;
            IsMountain = isMountain;
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
            tiles[tile.Coord] = new TileState(tile.Owner, HexOccupant.Clone(tile.Occupant), tile.IsGold, tile.IsMountain);
        }
        return new EditorSnapshot(
            tiles,
            new HashSet<HexCoord>(water),
            new List<Territory>(territories));
    }

    /// <summary>
    /// True iff the live <paramref name="grid"/> + <paramref name="water"/>
    /// differ from this snapshot in any paint-relevant way — tile set, water
    /// set, per-tile owner, occupant (by kind / unit level+owner), gold flag,
    /// or mountain flag. The editor uses this to decide whether a paint stroke
    /// actually changed anything before pushing it onto the undo stack, so
    /// flag-only paints (gold #45, mountain #37) that leave the territory
    /// partition untouched are still recorded.
    /// </summary>
    public bool DiffersFromGrid(HexGrid grid, IReadOnlySet<HexCoord> water)
    {
        if (!_water.SetEquals(water)) return true;

        int liveCount = 0;
        foreach (HexTile tile in grid.Tiles)
        {
            liveCount++;
            if (!_tiles.TryGetValue(tile.Coord, out TileState s)) return true;
            if (s.Owner != tile.Owner) return true;
            if (s.IsGold != tile.IsGold) return true;
            if (s.IsMountain != tile.IsMountain) return true;
            if (OccupantSignature(s.Occupant) != OccupantSignature(tile.Occupant)) return true;
        }
        // A tile present in the snapshot but gone from the grid (without the
        // counts differing only when a different coord was added) is caught
        // by the count comparison below.
        return liveCount != _tiles.Count;
    }

    /// <summary>
    /// Paint-relevant identity of an occupant: kind tag plus a unit's level
    /// and owner. Distinct occupants compare unequal; two empty tiles compare
    /// equal. Used only by <see cref="DiffersFromGrid"/>.
    /// </summary>
    private static (int Kind, int Level, int Owner) OccupantSignature(HexOccupant? occupant) => occupant switch
    {
        null => (0, 0, 0),
        Unit u => (1, (int)u.Level, u.Owner.Index),
        Capital => (2, 0, 0),
        Tower => (3, 0, 0),
        Tree => (4, 0, 0),
        Grave => (5, 0, 0),
        _ => (6, 0, 0),
    };

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
                IsGold = kvp.Value.IsGold,
                IsMountain = kvp.Value.IsMountain,
            };
            grid.Add(tile);
        }

        water.Clear();
        foreach (HexCoord c in _water) water.Add(c);

        return _territories;
    }

}
