// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Renders a board as agent-legible ASCII text for the headless
/// level-design harness. Each cell is two characters — owner then mark:
/// owner is the slot digit 0-5 (matching roster slot indices), '.' for
/// neutral land, '~' for water; the mark is the occupant if any
/// ('*' capital, 't' tower, 'T' tree, 'x' grave, unit-level digit),
/// else the terrain feature ('$' gold, '^' mountain), else '.'.
/// Odd rows are indented half a cell (odd-r offset layout), and a
/// summary of per-slot tile/capital counts follows the board.
/// </summary>
public static class MapTextRenderer
{
    public static string Render(
        HexGrid grid, IReadOnlySet<HexCoord> water, int cols, int rows)
    {
        var sb = new StringBuilder();

        sb.Append("   ");
        for (int c = 0; c < cols; c++)
        {
            if (c > 0) sb.Append(' ');
            sb.Append(c.ToString().PadLeft(2));
        }
        sb.Append('\n');

        for (int r = 0; r < rows; r++)
        {
            sb.Append(r.ToString().PadLeft(2)).Append(' ');
            if (r % 2 == 1) sb.Append("  ");
            for (int c = 0; c < cols; c++)
            {
                if (c > 0) sb.Append(' ');
                sb.Append(CellText(grid, water, HexCoord.FromOffset(c, r)));
            }
            sb.Append('\n');
        }

        sb.Append('\n');
        AppendSummary(sb, grid, water);
        sb.Append("legend: owner 0-5=slot, .=neutral, ~=water; ");
        sb.Append("mark *=capital t=tower T=tree x=grave 1-4=unit level ");
        sb.Append("$=gold ^=mountain .=plain\n");
        return sb.ToString();
    }

    private static string CellText(
        HexGrid grid, IReadOnlySet<HexCoord> water, HexCoord coord)
    {
        HexTile? tile = grid.Get(coord);
        if (tile == null) return water.Contains(coord) ? "~~" : "  ";

        char owner = tile.Owner == PlayerId.None
            ? '.'
            : (char)('0' + tile.Owner.Index);
        return $"{owner}{MarkFor(tile)}";
    }

    private static char MarkFor(HexTile tile) => tile.Occupant switch
    {
        Capital => '*',
        Tower => 't',
        Tree => 'T',
        Grave => 'x',
        Unit u => (char)('0' + (int)u.Level),
        _ => tile.Feature switch
        {
            TerrainFeature.Gold => '$',
            TerrainFeature.Mountain => '^',
            _ => '.',
        },
    };

    private static void AppendSummary(
        StringBuilder sb, HexGrid grid, IReadOnlySet<HexCoord> water)
    {
        int slotCount = GameSettings.PlayerConfig.Length;
        int[] tiles = new int[slotCount];
        int[] capitals = new int[slotCount];
        int neutral = 0;

        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Owner == PlayerId.None)
            {
                neutral++;
                continue;
            }
            int slot = tile.Owner.Index;
            tiles[slot]++;
            if (tile.Occupant is Capital) capitals[slot]++;
        }

        for (int slot = 0; slot < slotCount; slot++)
        {
            if (tiles[slot] == 0) continue;
            sb.Append($"slot {slot} ({GameSettings.PlayerConfig[slot].Name}): ");
            sb.Append($"{tiles[slot]} tiles, {capitals[slot]} capital");
            if (capitals[slot] != 1) sb.Append('s');
            sb.Append('\n');
        }
        sb.Append($"neutral: {neutral}  water: {water.Count}\n");
    }
}
