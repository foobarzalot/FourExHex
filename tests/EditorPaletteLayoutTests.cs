// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pure layout math for the map editor's paint-tool grid. The
/// grid stays a single line on roomy screens and wraps to a second row
/// (portrait) / column (landscape) on compact phones so a 5th brush (gold)
/// can't overflow the bottom bar / side rail.
/// </summary>
public class EditorPaletteLayoutTests
{
    [Fact]
    public void PaintColumns_LandscapeExpanded_IsSingleColumn()
    {
        Assert.Equal(1, EditorPaletteLayout.PaintColumns(
            ScreenOrientation.Landscape, compact: false, buttonCount: 5));
    }

    [Fact]
    public void PaintColumns_LandscapeCompact_WrapsToTwoColumns()
    {
        Assert.Equal(2, EditorPaletteLayout.PaintColumns(
            ScreenOrientation.Landscape, compact: true, buttonCount: 5));
    }

    [Fact]
    public void PaintColumns_PortraitExpanded_IsSingleRow()
    {
        // One column per button → a single horizontal row.
        Assert.Equal(5, EditorPaletteLayout.PaintColumns(
            ScreenOrientation.Portrait, compact: false, buttonCount: 5));
    }

    [Fact]
    public void PaintColumns_PortraitCompact_WrapsToTwoRows()
    {
        // 5 buttons over 2 rows → 3 columns (rows of 3 + 2).
        int cols = EditorPaletteLayout.PaintColumns(
            ScreenOrientation.Portrait, compact: true, buttonCount: 5);
        Assert.Equal(3, cols);
        Assert.Equal(2, EditorPaletteLayout.RowsFor(5, cols));
    }

    [Fact]
    public void RowsFor_DividesAndRoundsUp()
    {
        Assert.Equal(1, EditorPaletteLayout.RowsFor(5, 5));
        Assert.Equal(3, EditorPaletteLayout.RowsFor(5, 2));
        Assert.Equal(2, EditorPaletteLayout.RowsFor(5, 3));
    }

    [Fact]
    public void LineExtent_AccountsForButtonsAndGaps()
    {
        // 2 lines = 2*68 + 1*8 = 144.
        Assert.Equal(144f, EditorPaletteLayout.LineExtent(2));
        Assert.Equal(68f, EditorPaletteLayout.LineExtent(1));
        Assert.Equal(0f, EditorPaletteLayout.LineExtent(0));
    }
}
