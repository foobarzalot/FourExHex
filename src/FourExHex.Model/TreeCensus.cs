// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// A whole-map snapshot of tree/grave incidence, used to quantify how far
/// forests have overrun the board (issue #100). Pure count of the current
/// <see cref="HexGrid"/> — no interpretation, no floats. <see cref="Trees"/>
/// is the headline treepocalypse-incidence figure; <see cref="LandTiles"/>
/// is its coverage denominator (it shrinks in Rising Tides as shore submerges).
/// The ownership split (<see cref="OwnedTrees"/> vs <see cref="NeutralTrees"/>)
/// is a factual readout of where the trees sit.
/// </summary>
public readonly struct TreeCensus
{
    /// <summary>Tiles still on the grid (land only — submerged tiles are gone).</summary>
    public int LandTiles { get; }

    /// <summary>Tiles whose occupant is a <see cref="Tree"/>.</summary>
    public int Trees { get; }

    /// <summary>Tiles whose occupant is a <see cref="Grave"/> (rots into a tree next owner-turn).</summary>
    public int Graves { get; }

    /// <summary>Trees on a tile owned by a real player (<c>!Owner.IsNone</c>).</summary>
    public int OwnedTrees { get; }

    /// <summary>Trees on a neutral tile (<see cref="PlayerId.None"/>).</summary>
    public int NeutralTrees { get; }

    public TreeCensus(int landTiles, int trees, int graves, int ownedTrees, int neutralTrees)
    {
        LandTiles = landTiles;
        Trees = trees;
        Graves = graves;
        OwnedTrees = ownedTrees;
        NeutralTrees = neutralTrees;
    }

    /// <summary>Walk the grid once and tally tree/grave incidence.</summary>
    public static TreeCensus Of(HexGrid grid)
    {
        int land = 0;
        int trees = 0;
        int graves = 0;
        int ownedTrees = 0;
        int neutralTrees = 0;
        foreach (HexTile tile in grid.Tiles)
        {
            land++;
            switch (tile.Occupant)
            {
                case Tree:
                    trees++;
                    if (tile.Owner.IsNone) neutralTrees++;
                    else ownedTrees++;
                    break;
                case Grave:
                    graves++;
                    break;
            }
        }
        return new TreeCensus(land, trees, graves, ownedTrees, neutralTrees);
    }
}
