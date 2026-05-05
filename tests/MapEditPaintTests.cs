using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class MapEditPaintTests
{
    private const int Cols = 10;
    private const int Rows = 10;

    private static (HexGrid grid, HashSet<HexCoord> water) MakeBlankBoard()
    {
        var grid = new HexGrid();
        var water = new HashSet<HexCoord>();
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                water.Add(HexCoord.FromOffset(col, row));
            }
        }
        return (grid, water);
    }

    private static int CountCapitals(HexGrid grid) =>
        grid.Tiles.Count(t => t.Occupant is Capital);

    [Fact]
    public void PaintLand_OnWater_AddsTileAndRemovesFromWater()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = new Color(1f, 0f, 0f);
        var coord = HexCoord.FromOffset(2, 3);

        MapEditPaint.PaintLand(grid, water, new List<Territory>(), Cols, Rows, coord, color);

        Assert.True(grid.Contains(coord));
        Assert.Equal(color, grid.Get(coord)!.Color);
        Assert.DoesNotContain(coord, water);
    }

    [Fact]
    public void PaintLand_OutOfBounds_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();
        int waterBefore = water.Count;

        territories = MapEditPaint.PaintLand(
            grid, water, territories, Cols, Rows,
            HexCoord.FromOffset(-1, 0), new Color(1f, 0f, 0f));

        Assert.Equal(0, grid.Count);
        Assert.Equal(waterBefore, water.Count);
        Assert.Empty(territories);
    }

    [Fact]
    public void PaintWater_OnLand_RemovesTileAndAddsToWater()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = new Color(1f, 0f, 0f);
        var coord = HexCoord.FromOffset(2, 3);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        MapEditPaint.PaintWater(grid, water, territories, Cols, Rows, coord);

        Assert.False(grid.Contains(coord));
        Assert.Contains(coord, water);
    }

    [Fact]
    public void PaintTreeToggle_OnEmptyLand_PlacesTree()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = new Color(1f, 0f, 0f);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);

        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.IsType<Tree>(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTreeToggle_OnExistingTree_RemovesTree()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = new Color(1f, 0f, 0f);
        var coord = HexCoord.FromOffset(2, 2);
        IReadOnlyList<Territory> territories = MapEditPaint.PaintLand(
            grid, water, new List<Territory>(), Cols, Rows, coord, color);
        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        territories = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.Null(grid.Get(coord)!.Occupant);
    }

    [Fact]
    public void PaintTreeToggle_OnWater_IsNoop()
    {
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        IReadOnlyList<Territory> territories = new List<Territory>();
        var coord = HexCoord.FromOffset(2, 2);

        IReadOnlyList<Territory> after = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, coord);

        Assert.Same(territories, after);
        Assert.False(grid.Contains(coord));
    }

    [Fact]
    public void PaintTreeToggle_OnTileWithCapital_DoesNotReplaceCapital()
    {
        // A capital is gameplay state placed by CapitalReconciler. The tree
        // palette mustn't trash it — only empty land or existing trees.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = new Color(1f, 0f, 0f);
        IReadOnlyList<Territory> territories = new List<Territory>();
        for (int col = 0; col < 3; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }
        // After three adjacent same-color paints we have one capital
        // somewhere in the row.
        HexCoord capitalCoord = territories[0].Capital!.Value;

        IReadOnlyList<Territory> after = MapEditPaint.PaintTreeToggle(
            grid, water, territories, Cols, Rows, capitalCoord);

        Assert.Same(territories, after);
        Assert.IsType<Capital>(grid.Get(capitalCoord)!.Occupant);
    }

    [Fact]
    public void PaintLand_FourAdjacentSameColorTiles_LeavesExactlyOneCapital()
    {
        // Reproduces the editor's duplicate-capital bug: each paint
        // reconciles, and without threading the previous territory list
        // back in, CapitalReconciler doesn't recognize the existing
        // Capital occupant as inherited and places a fresh one without
        // clearing the old one. After painting a strip of same-color
        // tiles the grid ends up with multiple Capital occupants.
        (HexGrid grid, HashSet<HexCoord> water) = MakeBlankBoard();
        var color = new Color(1f, 0f, 0f);
        IReadOnlyList<Territory> territories = new List<Territory>();

        for (int col = 0; col < 4; col++)
        {
            territories = MapEditPaint.PaintLand(
                grid, water, territories, Cols, Rows,
                HexCoord.FromOffset(col, 0), color);
        }

        Assert.Equal(1, CountCapitals(grid));
    }
}
