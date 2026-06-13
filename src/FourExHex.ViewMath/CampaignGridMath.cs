using System;

/// <summary>
/// Pointy-top honeycomb layout math for the campaign screen's tier grids
/// (issue #2). A tier is a block of 64 hex cells laid out
/// <c>columns</c> wide (8 in portrait, 16 in landscape — at 16 each row
/// is one 0x10 block, making the grid self-indexing). Odd rows shift
/// right by half a column step; rows interlock at the standard honeycomb
/// vertical pitch of 0.75 × hex height (plus the gap).
///
/// Float math is fine here: this is view-side pixel geometry, the same
/// reason the rest of FourExHex.ViewMath exists.
/// </summary>
public static class CampaignGridMath
{
    /// <summary>Center of cell <paramref name="index"/> within its block,
    /// relative to the block's top-left origin.</summary>
    public static (float x, float y) CellCenter(
        int index, int columns, float hexWidth, float hexHeight, float gap)
    {
        int row = index / columns;
        int col = index % columns;
        float stepX = hexWidth + gap;
        float pitchY = 0.75f * hexHeight + gap;
        float x = col * stepX + (row % 2 == 1 ? stepX / 2f : 0f) + hexWidth / 2f;
        float y = row * pitchY + hexHeight / 2f;
        return (x, y);
    }

    /// <summary>Tight pixel size of a <paramref name="count"/>-cell block.
    /// Width includes the odd-row half-step overhang when the block has
    /// more than one row.</summary>
    public static (float width, float height) BlockSize(
        int count, int columns, float hexWidth, float hexHeight, float gap)
    {
        int rows = (count + columns - 1) / columns;
        float stepX = hexWidth + gap;
        float pitchY = 0.75f * hexHeight + gap;
        float width = columns * stepX - gap + (rows > 1 ? stepX / 2f : 0f);
        float height = (rows - 1) * pitchY + hexHeight;
        return (width, height);
    }

    /// <summary>Which cell of the block contains the point, or null when
    /// the point is in a gap / notch / outside the block entirely. Exact
    /// point-in-hexagon test, so the interlocking overlap bands resolve
    /// to the correct row.</summary>
    public static int? HitTest(
        float px, float py, int count, int columns,
        float hexWidth, float hexHeight, float gap)
    {
        for (int i = 0; i < count; i++)
        {
            (float cx, float cy) = CellCenter(i, columns, hexWidth, hexHeight, gap);
            if (InsideHex(px - cx, py - cy, hexWidth, hexHeight)) return i;
        }
        return null;
    }

    /// <summary>
    /// Point-in-pointy-top-hexagon, offsets relative to the hex center.
    /// Vertices (normalized half-extents u right, v down): top (0,-1),
    /// upper sides (±1,-0.5), lower sides (±1,0.5), bottom (0,1) — the
    /// CSS clip-path polygon(50% 0, 100% 25%, 100% 75%, 50% 100%, 0 75%,
    /// 0 25%) of the design. By symmetry: inside iff |u| ≤ 1 and
    /// |v| ≤ 1 − |u|/2.
    /// </summary>
    private static bool InsideHex(float dx, float dy, float hexWidth, float hexHeight)
    {
        float u = Math.Abs(dx) / (hexWidth / 2f);
        float v = Math.Abs(dy) / (hexHeight / 2f);
        return u <= 1f && v <= 1f - u / 2f;
    }
}
