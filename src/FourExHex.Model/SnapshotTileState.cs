// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Deep-copy of a single tile's mutable state — owner, occupant (cloned), and
/// the terrain flags. The shared per-tile cell of <see cref="GameStateSnapshot"/>
/// and <see cref="EditorSnapshot"/>, so the two snapshots can't drift in what
/// they capture.
/// </summary>
internal readonly struct SnapshotTileState
{
    public PlayerId Owner { get; }
    public HexOccupant? Occupant { get; }
    public bool IsGold { get; }
    public bool IsMountain { get; }

    public SnapshotTileState(PlayerId owner, HexOccupant? occupant, bool isGold, bool isMountain)
    {
        Owner = owner;
        Occupant = occupant;
        IsGold = isGold;
        IsMountain = isMountain;
    }

    /// <summary>Deep-copy every tile of <paramref name="grid"/> into a
    /// coord→state map, cloning each occupant.</summary>
    public static Dictionary<HexCoord, SnapshotTileState> CaptureTiles(HexGrid grid)
    {
        var tiles = new Dictionary<HexCoord, SnapshotTileState>();
        foreach (HexTile tile in grid.Tiles)
        {
            tiles[tile.Coord] = new SnapshotTileState(
                tile.Owner, HexOccupant.Clone(tile.Occupant), tile.IsGold, tile.IsMountain);
        }
        return tiles;
    }
}
