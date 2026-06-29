using System.Collections.Generic;

/// <summary>
/// Immutable deep copy of everything a turn can mutate: tile owners and
/// occupants (units, capitals, eventually towers/trees/graves), treasury
/// gold balances, and the list of territories. Used by the undo stack to
/// roll back to earlier in the current turn.
/// </summary>
public class GameStateSnapshot
{
    private readonly Dictionary<HexCoord, SnapshotTileState> _tiles;
    private readonly Dictionary<HexCoord, int> _gold;
    private readonly IReadOnlyList<Territory> _territories;

    private GameStateSnapshot(
        Dictionary<HexCoord, SnapshotTileState> tiles,
        Dictionary<HexCoord, int> gold,
        IReadOnlyList<Territory> territories)
    {
        _tiles = tiles;
        _gold = gold;
        _territories = territories;
    }

    /// <summary>
    /// Capture a deep-copy snapshot of <paramref name="grid"/>,
    /// <paramref name="treasury"/>, and <paramref name="territories"/>.
    /// </summary>
    public static GameStateSnapshot Capture(
        HexGrid grid,
        Treasury treasury,
        IReadOnlyList<Territory> territories)
    {
        var tiles = SnapshotTileState.CaptureTiles(grid);

        var gold = new Dictionary<HexCoord, int>();
        foreach (Territory t in territories)
        {
            if (t.HasCapital)
            {
                HexCoord capital = t.Capital!.Value;
                gold[capital] = treasury.GetGold(capital);
            }
        }

        // Territory objects are immutable; the list itself could be
        // mutated later, so copy.
        var territoriesCopy = new List<Territory>(territories);

        return new GameStateSnapshot(tiles, gold, territoriesCopy);
    }

    /// <summary>
    /// Captured (owner, occupant) for every tile, in the iteration order
    /// of the underlying dictionary. The save serializer iterates this to
    /// persist the snapshot inside a <c>ReplayDto.InitialState</c> without
    /// having to re-apply the snapshot to a throwaway grid first.
    /// </summary>
    public IEnumerable<(HexCoord Coord, PlayerId Owner, HexOccupant? Occupant, bool IsGold, bool IsMountain)> EnumerateTiles()
    {
        foreach (KeyValuePair<HexCoord, SnapshotTileState> kvp in _tiles)
        {
            yield return (kvp.Key, kvp.Value.Owner, kvp.Value.Occupant, kvp.Value.IsGold, kvp.Value.IsMountain);
        }
    }

    /// <summary>
    /// Captured (capital, gold) for every territory that had a capital
    /// at snapshot time. Empty entries are not present.
    /// </summary>
    public IEnumerable<(HexCoord Capital, int Gold)> EnumerateGold()
    {
        foreach (KeyValuePair<HexCoord, int> kvp in _gold)
        {
            yield return (kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Captured territory list (by reference, since <see cref="Territory"/>
    /// is immutable). The save serializer maps these into
    /// <c>TerritoryDto</c>s when persisting the snapshot.
    /// </summary>
    public IReadOnlyList<Territory> Territories => _territories;

    /// <summary>
    /// Restore this snapshot's state back into <paramref name="grid"/> and
    /// <paramref name="treasury"/>. Returns the territory list from when
    /// the snapshot was captured (so the caller can assign it to the map).
    /// </summary>
    public IReadOnlyList<Territory> ApplyTo(HexGrid grid, Treasury treasury)
    {
        foreach (KeyValuePair<HexCoord, SnapshotTileState> kvp in _tiles)
        {
            HexTile? tile = grid.Get(kvp.Key);
            if (tile == null)
            {
                // The tile was removed from the grid since capture — in Rising
                // Tides a submerged shore tile is Grid.Remove'd. A replay rewind
                // restores the full initial board onto this shrunken grid, so
                // re-add the missing tile rather than silently skipping it
                // (otherwise the rewound board is missing every sunk tile and
                // the replay diverges). Non-Rising-Tides callers never hit this
                // branch — their grid still holds every captured coord.
                tile = new HexTile(kvp.Key, kvp.Value.Owner);
                grid.Add(tile);
            }

            tile.Owner = kvp.Value.Owner;
            // Clone again on apply so the snapshot remains independent and
            // restores remain idempotent across multiple calls.
            tile.Occupant = HexOccupant.Clone(kvp.Value.Occupant);
            tile.IsGold = kvp.Value.IsGold;
            tile.IsMountain = kvp.Value.IsMountain;
        }

        treasury.Clear();
        foreach (KeyValuePair<HexCoord, int> kvp in _gold)
        {
            treasury.SetGold(kvp.Key, kvp.Value);
        }

        return _territories;
    }

}
