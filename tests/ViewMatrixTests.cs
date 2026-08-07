// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

// The view-matrix cell table is the harness's coverage claim, so its
// justification is executable here rather than a comment: walking the cells in
// order must actually drive ScreenLayout and PanelFitMath through the branches
// the table says it does. If someone later "tidies" a cell size, these fail.
public class ViewMatrixTests
{
    private static IReadOnlyList<ViewMatrixCell> Cells => ViewMatrix.Default;

    [Fact]
    public void CellNames_AreUniqueAndNonEmpty()
    {
        Assert.All(Cells, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        Assert.Equal(Cells.Count, Cells.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void EveryCell_HasPositiveLogicalSizeAndScale()
    {
        Assert.All(Cells, c =>
        {
            Assert.True(c.LogicalWidth > 0, $"{c.Name} width");
            Assert.True(c.LogicalHeight > 0, $"{c.Name} height");
            Assert.True(c.UiScale > 0f, $"{c.Name} scale");
        });
    }

    // DisplayScale derives ContentScaleFactor from monitor DPI, so an unpinned
    // cell lands on different sides of the 668/732 compact thresholds on a
    // retina Mac vs xvfb vs a 4K desktop. Every cell must pin it.
    [Fact]
    public void EveryCell_PinsItsUiScale()
    {
        Assert.All(Cells, c => Assert.True(c.UiScale > 0f, $"{c.Name} must pin FOUREXHEX_UI_SCALE"));
    }

    [Fact]
    public void PhysicalSize_IsLogicalTimesScale()
    {
        ViewMatrixCell cell = ViewMatrix.ByName("mobile-scale-portrait");

        Assert.Equal((int)(cell.LogicalWidth * cell.UiScale), cell.PhysicalWidth);
        Assert.Equal((int)(cell.LogicalHeight * cell.UiScale), cell.PhysicalHeight);
    }

    // The whole reason for in-process resize (rather than a cold start per cell):
    // IsCompact's dead band is path-dependent, so a hold can only be observed by
    // arriving at the same size from two different prior states.
    [Fact]
    public void WalkingCellsInOrder_ExercisesBothCompactHysteresisDirections()
    {
        bool compact = false;
        bool sawFlipToCompact = false;
        bool sawFlipToExpanded = false;

        foreach (ViewMatrixCell cell in Cells)
        {
            bool next = ScreenLayout.IsCompact(cell.LogicalWidth, cell.LogicalHeight, compact);
            if (next && !compact) sawFlipToCompact = true;
            if (!next && compact) sawFlipToExpanded = true;
            compact = next;
        }

        Assert.True(sawFlipToCompact, "no expanded→compact flip in the matrix");
        Assert.True(sawFlipToExpanded, "no compact→expanded flip in the matrix");
    }

    [Fact]
    public void MatrixContains_ADeadBandHoldFromEachDirection()
    {
        bool compact = false;
        bool heldExpanded = false;
        bool heldCompact = false;

        foreach (ViewMatrixCell cell in Cells)
        {
            float minSide = System.Math.Min(cell.LogicalWidth, cell.LogicalHeight);
            bool inDeadBand =
                minSide >= ScreenLayout.CompactBreakpointPx - ScreenLayout.CompactDeadBandPx &&
                minSide <= ScreenLayout.CompactBreakpointPx + ScreenLayout.CompactDeadBandPx;

            bool next = ScreenLayout.IsCompact(cell.LogicalWidth, cell.LogicalHeight, compact);
            if (inDeadBand && next == compact)
            {
                if (compact) heldCompact = true;
                else heldExpanded = true;
            }
            compact = next;
        }

        Assert.True(heldExpanded, "no dead-band cell that holds expanded");
        Assert.True(heldCompact, "no dead-band cell that holds compact");
    }

    [Fact]
    public void Matrix_CoversBothOrientationsAndTheSquareTie()
    {
        var orientations = Cells
            .Select(c => ScreenLayout.Resolve(c.LogicalWidth, c.LogicalHeight))
            .Distinct()
            .ToList();

        Assert.Contains(ScreenOrientation.Portrait, orientations);
        Assert.Contains(ScreenOrientation.Landscape, orientations);
        Assert.Contains(Cells, c => c.LogicalWidth == c.LogicalHeight);
    }

    // Non-zero left/right insets only ever occur with the notch rotated into
    // landscape, so that case needs its own cell.
    [Fact]
    public void Matrix_CoversEachSafeAreaEdge()
    {
        Assert.Contains(Cells, c => c.Insets.Top > 0f);
        Assert.Contains(Cells, c => c.Insets.Bottom > 0f);
        Assert.Contains(Cells, c => c.Insets.Left > 0f || c.Insets.Right > 0f);
        Assert.Contains(Cells, c => c.Insets == LogicalSafeInsets.Zero);
    }

    // The matrix targets shapes real devices and windows actually produce, not
    // degenerate ones: PanelFitMath's floors (0.65 shrink, 200px card interior)
    // are reachable only by viewports narrower or shorter than any shipping
    // device, and designing for those was costing more than it protected.
    // Every cell must therefore sit at or above the declared floor.
    [Fact]
    public void EveryCell_IsAtOrAboveTheDeclaredMinimumSupportedSize()
    {
        Assert.All(Cells, c =>
        {
            Assert.True(c.LogicalWidth >= ViewMatrix.MinSupportedWidth,
                $"{c.Name} is {c.LogicalWidth} wide, below the supported floor");
            Assert.True(c.LogicalHeight >= ViewMatrix.MinSupportedHeight,
                $"{c.Name} is {c.LogicalHeight} tall, below the supported floor");
        });
    }

    // The floor is the smallest shipping phone in either orientation; a cell at
    // exactly the floor must still leave a workable content box, or the floor
    // is set below what the layout can actually serve.
    [Fact]
    public void AtTheDeclaredFloor_TheContentBoxIsStillWorkable()
    {
        (float availW, float availH) = PanelFitMath.AvailableBox(
            ViewMatrix.MinSupportedWidth, ViewMatrix.MinSupportedHeight,
            LogicalSafeInsets.Zero, marginPerSide: 24f);

        Assert.True(availW >= PanelFitMath.MinCardInteriorWidthPx, $"availW={availW}");
        Assert.True(availH > 0f, $"availH={availH}");
    }

    [Fact]
    public void ByName_ResolvesAndRejects()
    {
        Assert.Equal("square-tie", ViewMatrix.ByName("square-tie").Name);
        Assert.Throws<KeyNotFoundException>(() => ViewMatrix.ByName("no-such-cell"));
    }

    [Fact]
    public void Parse_FiltersToTheNamedCellsInMatrixOrder()
    {
        IReadOnlyList<ViewMatrixCell> picked = ViewMatrix.Parse("square-tie portrait-phone");

        Assert.Equal(2, picked.Count);
        // Matrix order, not the order the caller listed them.
        Assert.Equal(
            Cells.Where(c => c.Name is "square-tie" or "portrait-phone").Select(c => c.Name),
            picked.Select(c => c.Name));
    }

    [Fact]
    public void Parse_EmptyOrNull_YieldsTheWholeMatrix()
    {
        Assert.Equal(Cells.Count, ViewMatrix.Parse(null).Count);
        Assert.Equal(Cells.Count, ViewMatrix.Parse("   ").Count);
    }

    // Sweeping back down re-enters each size from the opposite direction, which
    // is what makes the dead-band holds observable in both senses.
    [Fact]
    public void WithReverseSweep_IsAPalindromeWithoutDuplicatingTheTurnaround()
    {
        IReadOnlyList<ViewMatrixCell> sweep = ViewMatrix.WithReverseSweep(Cells);

        Assert.Equal(Cells.Count * 2 - 1, sweep.Count);
        Assert.Equal(Cells[0].Name, sweep[0].Name);
        Assert.Equal(Cells[0].Name, sweep[^1].Name);
        Assert.Equal(Cells[^1].Name, sweep[Cells.Count - 1].Name);
    }

}
