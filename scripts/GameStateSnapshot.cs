using System.Collections.Generic;
using Godot;

/// <summary>
/// Immutable deep copy of everything a turn can mutate: tile colors and
/// occupants (units, capitals, eventually towers/trees/graves), treasury
/// gold balances, and the list of territories. Used by the undo stack to
/// roll back to earlier in the current turn.
/// </summary>
public class GameStateSnapshot
{
    private readonly struct TileState
    {
        public Color Color { get; }
        public HexOccupant? Occupant { get; }

        public TileState(Color color, HexOccupant? occupant)
        {
            Color = color;
            Occupant = occupant;
        }
    }

    private readonly Dictionary<HexCoord, TileState> _tiles;
    private readonly Dictionary<HexCoord, int> _gold;
    private readonly IReadOnlyList<Territory> _territories;

    private GameStateSnapshot(
        Dictionary<HexCoord, TileState> tiles,
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
        var tiles = new Dictionary<HexCoord, TileState>();
        foreach (HexTile tile in grid.Tiles)
        {
            tiles[tile.Coord] = new TileState(
                color: tile.Color,
                occupant: HexOccupant.Clone(tile.Occupant));
        }

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
    /// Restore this snapshot's state back into <paramref name="grid"/> and
    /// <paramref name="treasury"/>. Returns the territory list from when
    /// the snapshot was captured (so the caller can assign it to the map).
    /// </summary>
    public IReadOnlyList<Territory> ApplyTo(HexGrid grid, Treasury treasury)
    {
        foreach (KeyValuePair<HexCoord, TileState> kvp in _tiles)
        {
            HexTile? tile = grid.Get(kvp.Key);
            if (tile == null) continue;

            tile.Color = kvp.Value.Color;
            // Clone again on apply so the snapshot remains independent and
            // restores remain idempotent across multiple calls.
            tile.Occupant = HexOccupant.Clone(kvp.Value.Occupant);
        }

        treasury.Clear();
        foreach (KeyValuePair<HexCoord, int> kvp in _gold)
        {
            treasury.SetGold(kvp.Key, kvp.Value);
        }

        return _territories;
    }

}
