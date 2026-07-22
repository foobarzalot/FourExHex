// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class MapBoundsTests
{
    [Fact]
    public void Infer_EmptyBoard_IsZeroByZero()
    {
        (int cols, int rows) = MapBounds.Infer(new HexGrid(), new HashSet<HexCoord>());
        Assert.Equal(0, cols);
        Assert.Equal(0, rows);
    }

    [Fact]
    public void Infer_FullRectGrid_MatchesItsDimensions()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 4, PlayerId.FromIndex(0));
        (int cols, int rows) = MapBounds.Infer(grid, new HashSet<HexCoord>());
        Assert.Equal(5, cols);
        Assert.Equal(4, rows);
    }

    [Fact]
    public void Infer_WaterOnlyBoard_UsesWaterExtent()
    {
        var water = new HashSet<HexCoord>();
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 7; col++)
                water.Add(HexCoord.FromOffset(col, row));

        (int cols, int rows) = MapBounds.Infer(new HexGrid(), water);
        Assert.Equal(7, cols);
        Assert.Equal(3, rows);
    }

    [Fact]
    public void Infer_TakesMaxAcrossTilesAndWater()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(9, 2), PlayerId.FromIndex(1)));
        var water = new HashSet<HexCoord> { HexCoord.FromOffset(3, 11) };

        (int cols, int rows) = MapBounds.Infer(grid, water);
        Assert.Equal(10, cols);
        Assert.Equal(12, rows);
    }

    [Fact]
    public void Infer_MatchesAuthoredWorkspaceDimensions()
    {
        var ws = new LevelWorkspace(22, 17);
        ws.PaintLand(0, HexCoord.FromOffset(1, 1));

        (int cols, int rows) = MapBounds.Infer(ws.Grid, ws.Water);
        Assert.Equal(22, cols);
        Assert.Equal(17, rows);
    }
}
