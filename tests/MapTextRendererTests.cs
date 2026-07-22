// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class MapTextRendererTests
{
    // Board lines only (between the column header and the blank line
    // before the summary), without trailing whitespace concerns.
    private static string[] BoardLines(string rendered)
    {
        string[] all = rendered.Split('\n');
        // Line 0 is the column header; board rows follow until a blank line.
        return all.Skip(1).TakeWhile(l => l.Length > 0).ToArray();
    }

    private static HashSet<HexCoord> AllWater(int cols, int rows)
    {
        var water = new HashSet<HexCoord>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                water.Add(HexCoord.FromOffset(c, r));
        return water;
    }

    [Fact]
    public void BlankBoard_IsAllWaterCells()
    {
        var grid = new HexGrid();
        string text = MapTextRenderer.Render(grid, AllWater(4, 3), 4, 3);
        string[] rows = BoardLines(text);

        Assert.Equal(3, rows.Length);
        foreach (string line in rows)
            Assert.Equal(4, CountOccurrences(line, "~~"));
    }

    [Fact]
    public void OddRows_AreIndentedHalfCell()
    {
        var grid = new HexGrid();
        string text = MapTextRenderer.Render(grid, AllWater(2, 2), 2, 2);
        string[] rows = BoardLines(text);

        int even = rows[0].IndexOf("~~", StringComparison.Ordinal);
        int odd = rows[1].IndexOf("~~", StringComparison.Ordinal);
        Assert.Equal(even + 2, odd);
    }

    [Fact]
    public void ColumnHeader_AlignsWithEvenRowCells()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), PlayerId.None));
        var water = AllWater(4, 2);
        water.Remove(HexCoord.FromOffset(2, 0));

        string text = MapTextRenderer.Render(grid, water, 4, 2);
        string[] all = text.Split('\n');
        string header = all[0];
        string row0 = all[1];

        // The "2" column label sits exactly above the cell it labels.
        int labelPos = header.IndexOf('2');
        Assert.Equal("..", row0.Substring(labelPos - 1, 2));
    }

    [Theory]
    [InlineData(null, TerrainFeature.None, "0.")]
    [InlineData(typeof(Capital), TerrainFeature.None, "0*")]
    [InlineData(typeof(Tower), TerrainFeature.None, "0t")]
    [InlineData(typeof(Tree), TerrainFeature.None, "0T")]
    [InlineData(typeof(Grave), TerrainFeature.None, "0x")]
    [InlineData(null, TerrainFeature.Gold, "0$")]
    [InlineData(null, TerrainFeature.Mountain, "0^")]
    // Occupant wins over feature (tree on a mountain shows the tree).
    [InlineData(typeof(Tree), TerrainFeature.Mountain, "0T")]
    public void Cell_ShowsOwnerSlotAndMark(
        Type? occupantType, TerrainFeature feature, string expected)
    {
        var grid = new HexGrid();
        var tile = new HexTile(HexCoord.FromOffset(0, 0), PlayerId.FromIndex(0))
        {
            Feature = feature,
            Occupant = occupantType == null
                ? null
                : (HexOccupant)Activator.CreateInstance(occupantType)!,
        };
        grid.Add(tile);
        var water = AllWater(2, 1);
        water.Remove(tile.Coord);

        string text = MapTextRenderer.Render(grid, water, 2, 1);
        Assert.Contains(expected, BoardLines(text)[0]);
    }

    [Fact]
    public void Cell_ShowsUnitLevelDigit()
    {
        var grid = new HexGrid();
        var tile = new HexTile(HexCoord.FromOffset(0, 0), PlayerId.FromIndex(3))
        {
            Occupant = new Unit(PlayerId.FromIndex(3), UnitLevel.Captain),
        };
        grid.Add(tile);
        var water = AllWater(1, 1);
        water.Remove(tile.Coord);

        string text = MapTextRenderer.Render(grid, water, 1, 1);
        Assert.Contains("33", BoardLines(text)[0]);
    }

    [Fact]
    public void NeutralLand_RendersDotOwner()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), PlayerId.None)
        {
            Feature = TerrainFeature.Gold,
        });
        var water = AllWater(2, 1);
        water.Remove(HexCoord.FromOffset(1, 0));

        string text = MapTextRenderer.Render(grid, water, 2, 1);
        Assert.Contains(".$", BoardLines(text)[0]);
    }

    [Fact]
    public void Summary_CountsTilesAndCapitalsPerSlot()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), PlayerId.FromIndex(0))
        {
            Occupant = new Capital(),
        });
        grid.Add(new HexTile(HexCoord.FromOffset(1, 0), PlayerId.FromIndex(0)));
        grid.Add(new HexTile(HexCoord.FromOffset(2, 0), PlayerId.None));
        var water = AllWater(4, 1);
        water.Remove(HexCoord.FromOffset(0, 0));
        water.Remove(HexCoord.FromOffset(1, 0));
        water.Remove(HexCoord.FromOffset(2, 0));

        string text = MapTextRenderer.Render(grid, water, 4, 1);

        Assert.Contains("slot 0 (Red): 2 tiles, 1 capital", text);
        Assert.Contains("neutral: 1", text);
        Assert.Contains("water: 1", text);
    }

    [Fact]
    public void Summary_OmitsSlotsWithNoTiles()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(HexCoord.FromOffset(0, 0), PlayerId.FromIndex(2)));
        var water = AllWater(2, 1);
        water.Remove(HexCoord.FromOffset(0, 0));

        string text = MapTextRenderer.Render(grid, water, 2, 1);

        Assert.Contains("slot 2 (Green)", text);
        Assert.DoesNotContain("slot 1", text);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }
        return count;
    }
}
