/// <summary>
/// Pure layout math for the map editor's paint-tool grid (issue #45). With a
/// 5th brush (gold) the paint cluster can no longer fit a single line on a
/// compact phone (portrait bottom bar / landscape side rail are already at
/// capacity at four 68-px buttons). This decides how many grid columns the
/// paint tools use so they wrap to a second row (portrait) / column
/// (landscape) only on compact screens, keeping full-size tap targets.
///
/// Godot-free (plain floats / the <see cref="ScreenOrientation"/> enum) so it
/// is unit-tested; the view applies the columns to a GridContainer and uses
/// <see cref="LineExtent"/> to grow the bottom bar / widen the rail to fit.
/// </summary>
public static class EditorPaletteLayout
{
    /// <summary>Square palette-button edge, logical px (HexPaletteButton).</summary>
    public const float ButtonSize = 68f;

    /// <summary>Gap between adjacent buttons in the grid, logical px.</summary>
    public const float ButtonGap = 8f;

    /// <summary>
    /// Columns for the paint-tool grid. Landscape lays the tools out
    /// vertically — one column normally, two columns when compact (so a tall
    /// stack wraps before it runs off a short rail). Portrait lays them
    /// horizontally — one column per button normally (a single row), wrapping
    /// to two rows when compact (so a wide row can't run off a narrow bar).
    /// </summary>
    public static int PaintColumns(ScreenOrientation orientation, bool compact, int buttonCount)
    {
        if (buttonCount <= 0) return 1;
        if (orientation == ScreenOrientation.Landscape)
            return compact ? System.Math.Min(2, buttonCount) : 1;
        // Portrait: one row when roomy; split into two rows when compact.
        return compact ? (buttonCount + 1) / 2 : buttonCount;
    }

    /// <summary>Rows (lines along the wrap axis) for a grid of
    /// <paramref name="buttonCount"/> buttons at <paramref name="columns"/>
    /// columns — integer ceil-divide.</summary>
    public static int RowsFor(int buttonCount, int columns)
        => columns <= 0 ? buttonCount : (buttonCount + columns - 1) / columns;

    /// <summary>Pixel extent of <paramref name="lines"/> button lines along
    /// the wrap axis: <c>lines·ButtonSize + (lines−1)·ButtonGap</c>.</summary>
    public static float LineExtent(int lines)
        => lines <= 0 ? 0f : lines * ButtonSize + (lines - 1) * ButtonGap;
}
