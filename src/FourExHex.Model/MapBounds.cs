// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Board-dimension inference for loaded states. Authored map files
/// carry no explicit Cols/Rows; the board rectangle is the offset
/// bounding box of everything on it (land tiles + water coords), since
/// authoring always covers the full rectangle with land-or-water.
/// </summary>
public static class MapBounds
{
    public static (int Cols, int Rows) Infer(
        HexGrid grid, IReadOnlySet<HexCoord> water)
    {
        int cols = 0, rows = 0;
        foreach (HexTile tile in grid.Tiles) Grow(tile.Coord, ref cols, ref rows);
        foreach (HexCoord coord in water) Grow(coord, ref cols, ref rows);
        return (cols, rows);
    }

    private static void Grow(HexCoord coord, ref int cols, ref int rows)
    {
        (int col, int row) = coord.ToOffset();
        if (col + 1 > cols) cols = col + 1;
        if (row + 1 > rows) rows = row + 1;
    }
}
