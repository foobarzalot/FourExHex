// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure-logic helpers for the map editor's paint actions. Mutates a
/// <see cref="HexGrid"/> and water set in place, then re-runs the
/// territory finder + capital reconciler so the model stays consistent
/// across successive edits.
///
/// The previously-returned territory list MUST be threaded back in on
/// each call. Without it, <see cref="CapitalReconciler.Reconcile"/>
/// can't recognize already-placed <see cref="Capital"/> occupants as
/// inherited, so it places a fresh capital somewhere else without
/// clearing the old one — leaving orphan capitals behind.
/// </summary>
public static class MapEditPaint
{
    /// <summary>
    /// Set the tile at <paramref name="coord"/> to be owned by
    /// <paramref name="owner"/>. Creates a tile (and removes the coord from
    /// <paramref name="water"/>) if it was previously water; reassigns an
    /// existing tile in place if it was already land. Out-of-bounds coords
    /// and same-owner repaints are no-ops. Returns the up-to-date territory
    /// list to be threaded into the next call.
    /// </summary>
    public static IReadOnlyList<Territory> PaintLand(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord,
        PlayerId owner)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;

        HexTile? existing = grid.Get(coord);
        if (existing != null)
        {
            if (existing.Owner == owner) return previousTerritories;
            existing.Owner = owner;
        }
        else
        {
            grid.Add(new HexTile(coord, owner));
            water.Remove(coord);
        }

        return Reconcile(grid, previousTerritories);
    }

    /// <summary>
    /// Move the capital of the territory containing <paramref name="coord"/>
    /// to that coord. No-ops if the coord is out of bounds, water,
    /// already a capital, or in a singleton territory (singletons can't
    /// have capitals). The previous capital's <see cref="Capital"/>
    /// occupant is cleared from its tile, and any non-capital occupant
    /// (typically a <see cref="Tree"/>) on the target coord is replaced
    /// by the new <see cref="Capital"/>. Returns a fresh territory list
    /// with this territory's <see cref="Territory.Capital"/> updated; the
    /// other territories are passed through unchanged.
    ///
    /// Doesn't run <see cref="CapitalReconciler"/> because that placer
    /// uses its own tier-list to pick a capital coord — we want to honor
    /// the user's exact pick instead.
    /// </summary>
    public static IReadOnlyList<Territory> PaintCapital(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        HexTile? tile = grid.Get(coord);
        if (tile == null) return previousTerritories;
        if (tile.Occupant is Capital) return previousTerritories;
        // Capitals may sit on mountains — the flag is left in place.

        int territoryIdx = -1;
        for (int i = 0; i < previousTerritories.Count; i++)
        {
            if (previousTerritories[i].Contains(coord))
            {
                territoryIdx = i;
                break;
            }
        }
        if (territoryIdx < 0) return previousTerritories;

        Territory t = previousTerritories[territoryIdx];
        if (t.Coords.Count < 2) return previousTerritories;

        if (t.HasCapital)
        {
            HexTile? oldCapTile = grid.Get(t.Capital!.Value);
            if (oldCapTile?.Occupant is Capital) oldCapTile.Occupant = null;
        }
        tile.Occupant = new Capital();

        var result = new List<Territory>(previousTerritories.Count);
        for (int i = 0; i < previousTerritories.Count; i++)
        {
            if (i == territoryIdx)
            {
                result.Add(new Territory(t.Owner, t.Coords, capital: coord));
            }
            else
            {
                result.Add(previousTerritories[i]);
            }
        }
        return result;
    }

    /// <summary>
    /// Toggle a tower on the tile at <paramref name="coord"/>. Empty land
    /// gets a fresh <see cref="Tower"/>; an existing tower is cleared;
    /// a <see cref="Tree"/> on the tile is replaced by the tower (the
    /// inverse of <see cref="PaintTreeToggle"/>'s tower→tree path).
    /// No-op on water and on tiles holding a <see cref="Capital"/> —
    /// capitals are owned by territory state and the tower palette must
    /// not stomp them.
    /// </summary>
    public static IReadOnlyList<Territory> PaintTowerToggle(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        HexTile? tile = grid.Get(coord);
        if (tile == null) return previousTerritories;

        if (tile.Occupant is Capital) return previousTerritories;
        if (tile.Occupant is Tower)
        {
            tile.Occupant = null;
        }
        else
        {
            // Empty or Tree (or anything else non-Capital): replace with a
            // tower. Tree → Tower is the cross-type swap; empty → Tower is the
            // place case. A tower may coexist with a mountain — it
            // earns the +1 high-ground bonus — so the mountain flag is left as-is.
            tile.Occupant = new Tower();
        }
        return Reconcile(grid, previousTerritories);
    }

    /// <summary>
    /// Toggle a tree on the tile at <paramref name="coord"/>. Empty land
    /// gets a fresh <see cref="Tree"/>; an existing tree is cleared; a
    /// <see cref="Tower"/> on the tile is replaced by the tree (mirror of
    /// <see cref="PaintTowerToggle"/>'s tree→tower path). No-op on water
    /// and on tiles holding a <see cref="Capital"/>.
    /// </summary>
    public static IReadOnlyList<Territory> PaintTreeToggle(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        HexTile? tile = grid.Get(coord);
        if (tile == null) return previousTerritories;

        if (tile.Occupant is Capital) return previousTerritories;
        if (tile.Occupant is Tree)
        {
            tile.Occupant = null;
        }
        else
        {
            // Empty or Tower (or any other non-Capital): replace with a
            // tree. Tower → Tree is the cross-type swap; empty → Tree is
            // the place case. A tree coexists with a mountain,
            // so the mountain flag is left untouched.
            tile.Occupant = new Tree();
        }
        return Reconcile(grid, previousTerritories);
    }

    /// <summary>
    /// Convert the tile at <paramref name="coord"/> back to water. No-op if
    /// the coord is out of bounds or already water. Returns the up-to-date
    /// territory list.
    /// </summary>
    public static IReadOnlyList<Territory> PaintWater(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        if (!grid.Contains(coord)) return previousTerritories;

        grid.Remove(coord);
        water.Add(coord);

        return Reconcile(grid, previousTerritories);
    }

    /// <summary>
    /// Set the tile at <paramref name="coord"/> to be neutral (unowned,
    /// <see cref="PlayerId.None"/>) — a land tile owned by no player, but
    /// capturable by any adjacent player. Creates a tile (and
    /// removes the coord from <paramref name="water"/>) if it was water;
    /// reassigns an existing tile in place otherwise. Only player-bound
    /// occupants are discarded: a <see cref="Capital"/> (the invariant
    /// <see cref="CapitalReconciler.Reconcile"/> enforces — no capital on
    /// neutral land) or a <see cref="Unit"/> (owned by a specific player).
    /// Terrain-like, owner-agnostic occupants — <see cref="Tower"/>,
    /// <see cref="Tree"/>, <see cref="Grave"/> — survive the repaint:
    /// neutral ground legitimately holds them (trees spread onto and
    /// graves rot on neutral tiles). Out-of-bounds coords are
    /// no-ops. Returns the up-to-date territory list to thread into the
    /// next call.
    /// </summary>
    public static IReadOnlyList<Territory> PaintNeutral(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;

        HexTile? existing = grid.Get(coord);
        if (existing != null)
        {
            existing.Owner = PlayerId.None;
            // Drop only player-bound occupants — neutral land keeps its
            // terrain (Tower/Tree/Grave) but can't hold a Capital or Unit.
            if (existing.Occupant is Capital || existing.Occupant is Unit)
            {
                existing.Occupant = null;
            }
        }
        else
        {
            grid.Add(new HexTile(coord, PlayerId.None));
            water.Remove(coord);
        }

        return Reconcile(grid, previousTerritories);
    }

    /// <summary>
    /// Toggle the <see cref="HexTile.IsGold"/> flag on the land tile at
    /// <paramref name="coord"/>. Gold is a per-tile income
    /// modifier orthogonal to owner and occupant, so this preserves both — a
    /// gold tile may be owned by any player or neutral and may hold any
    /// occupant. Gold and mountain are mutually exclusive: turning
    /// gold ON clears any mountain on the tile. No-op out of bounds or on water
    /// (no tile there). The territory partition is unaffected; the previous
    /// list is returned unchanged for call-shape parity with the other paint
    /// helpers.
    /// </summary>
    public static IReadOnlyList<Territory> PaintGoldToggle(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        HexTile? tile = grid.Get(coord);
        if (tile == null) return previousTerritories;

        // Toggling gold ON retargets the tile's single TerrainFeature, so any
        // mountain clears automatically — gold and mountain are exclusive.
        tile.IsGold = !tile.IsGold;
        return previousTerritories;
    }

    /// <summary>
    /// Toggle the <see cref="HexTile.IsMountain"/> flag on the land tile at
    /// <paramref name="coord"/>. Mountains are high-ground terrain
    /// that coexist with any occupant — trees, graves, towers, and capitals:
    /// turning a mountain ON leaves the occupant in place. Mountains
    /// are mutually exclusive with <see cref="HexTile.IsGold"/>:
    /// turning a mountain ON clears any gold on the tile (and, symmetrically,
    /// <see cref="PaintGoldToggle"/> clears the mountain when it places gold).
    /// <see cref="HexTile.Owner"/> is preserved (a mountain may be owned by any
    /// player or neutral). No-op out of bounds or on water. The territory
    /// partition is unaffected, so the previous list is returned unchanged.
    /// </summary>
    public static IReadOnlyList<Territory> PaintMountainToggle(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        HexTile? tile = grid.Get(coord);
        if (tile == null) return previousTerritories;

        if (tile.IsMountain)
        {
            tile.IsMountain = false;
        }
        else
        {
            // Setting mountain retargets the tile's single TerrainFeature, so
            // any gold clears automatically — gold and mountain are exclusive.
            // Trees, graves and towers stay — they coexist with a mountain.
            tile.IsMountain = true;
        }
        return previousTerritories;
    }

    private static bool InBounds(HexCoord coord, int cols, int rows)
    {
        (int col, int row) = coord.ToOffset();
        return col >= 0 && col < cols && row >= 0 && row < rows;
    }

    private static IReadOnlyList<Territory> Reconcile(
        HexGrid grid,
        IReadOnlyList<Territory> previousTerritories)
    {
        return TerritoryFinder.Recompute(grid, previousTerritories);
    }
}
