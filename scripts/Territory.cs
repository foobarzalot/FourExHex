using System.Collections.Generic;
using Godot;

/// <summary>
/// A maximally-connected group of same-color hexes. Immutable snapshot of
/// what the map looked like when the territory was discovered — consumers
/// should re-run territory detection after any tile changes color.
/// </summary>
public class Territory
{
    public Color Owner { get; }
    public IReadOnlyCollection<HexCoord> Coords { get; }
    public int Size => Coords.Count;

    public Territory(Color owner, IReadOnlyCollection<HexCoord> coords)
    {
        Owner = owner;
        Coords = coords;
    }
}
