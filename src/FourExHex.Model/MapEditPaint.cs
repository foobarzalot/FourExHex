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
        if (t.Coords.Count < CapitalPlacer.MinTerritorySizeForCapital) return previousTerritories;

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
    /// Toggle a <see cref="Unit"/> of <paramref name="level"/> onto the tile
    /// at <paramref name="coord"/> — the editor's starting-garrison brush.
    /// The unit's owner is derived from the tile, never chosen separately:
    /// a player-owned tile yields that player's unit, a neutral tile yields a
    /// <see cref="PlayerId.None"/> unit — a viking raider. A unit of the same
    /// level already on the tile is cleared (the toggle); any other unit, or a
    /// <see cref="Tree"/> or <see cref="Grave"/>, is replaced.
    ///
    /// No-op out of bounds, on water, and on tiles holding a
    /// <see cref="Capital"/> or <see cref="Tower"/> — both are inviolable to
    /// every place-a-unit path (<c>MovementRules.ValidTargets</c> skips them
    /// too). Two rule-driven rejections:
    /// <list type="bullet">
    /// <item>a <b>non-neutral singleton</b>, because a one-tile territory has
    /// no capital and therefore no treasury, so
    /// <see cref="UpkeepRules.ApplyUpkeep"/> would grave the unit on its first
    /// upkeep tick. Neutral singletons are fine — vikings are upkeep-exempt;</item>
    /// <item><see cref="UnitLevel.Commander"/> on neutral land, because viking
    /// waves are never Commander (see
    /// <c>VikingRaidersRules.WaveComposition</c>) — there is no valid level-4
    /// neutral unit.</item>
    /// </list>
    /// Returns the up-to-date territory list to thread into the next call.
    /// </summary>
    public static IReadOnlyList<Territory> PaintUnitToggle(
        HexGrid grid,
        HashSet<HexCoord> water,
        IReadOnlyList<Territory> previousTerritories,
        int cols,
        int rows,
        HexCoord coord,
        UnitLevel level)
    {
        if (!InBounds(coord, cols, rows)) return previousTerritories;
        HexTile? tile = grid.Get(coord);
        if (tile == null)
        {
            LogUnit($"reject {Off(coord)} level={level}: water");
            return previousTerritories;
        }
        if (tile.Occupant is Capital || tile.Occupant is Tower)
        {
            LogUnit($"reject {Off(coord)} level={level}: tile holds a {tile.Occupant.GetType().Name}");
            return previousTerritories;
        }

        if (tile.Occupant is Unit existing && existing.Level == level)
        {
            tile.Occupant = null;
            LogUnit($"clear {Off(coord)} level={level}");
            return Reconcile(grid, previousTerritories);
        }

        if (IsUnsustainableForUnit(previousTerritories, coord))
        {
            LogUnit($"reject {Off(coord)} level={level}: non-neutral singleton (no capital)");
            return previousTerritories;
        }
        if (tile.Owner.IsNone && level == UnitLevel.Commander)
        {
            LogUnit($"reject {Off(coord)} level={level}: no level-4 viking exists");
            return previousTerritories;
        }

        tile.Occupant = new Unit(tile.Owner, level);
        LogUnit($"place {Off(coord)} level={level} owner={OwnerLabel(tile.Owner)}");
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
    /// reassigns an existing tile in place otherwise. A <see cref="Capital"/>
    /// on the tile is discarded — the invariant
    /// <see cref="CapitalReconciler.Reconcile"/> enforces, no capital on
    /// neutral land. Every other occupant survives the repaint: terrain-like
    /// <see cref="Tower"/>, <see cref="Tree"/> and <see cref="Grave"/> because
    /// neutral ground legitimately holds them (trees spread onto and graves rot
    /// on neutral tiles), and a <see cref="Unit"/> because
    /// <see cref="ReconcileUnits"/> re-owns it into a viking raider (or removes
    /// it, for a <see cref="UnitLevel.Commander"/>). Out-of-bounds coords are
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
            // A Capital is the one occupant neutral land can't hold (the
            // invariant CapitalReconciler enforces). A Unit stays and is
            // re-owned into a viking raider by ReconcileUnits.
            if (existing.Occupant is Capital)
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
        IReadOnlyList<Territory> territories =
            TerritoryFinder.Recompute(grid, previousTerritories);
        ReconcileUnits(grid, territories);
        return territories;
    }

    /// <summary>
    /// True when a unit on <paramref name="coord"/> could not sustain itself:
    /// the containing territory is owned by a player and is a singleton, so it
    /// has no capital and no treasury to pay upkeep from. Neutral singletons
    /// are sustainable — viking units are upkeep-exempt.
    /// </summary>
    private static bool IsUnsustainableForUnit(
        IReadOnlyList<Territory> territories, HexCoord coord)
    {
        Territory? t = TerritoryLookup.FindContaining(territories, coord);
        return t != null && !t.Owner.IsNone
            && t.Coords.Count < CapitalPlacer.MinTerritorySizeForCapital;
    }

    /// <summary>
    /// Keep every placed unit valid for the tile it stands on, after the
    /// partition has been recomputed. Ownership is derived from the
    /// territory continuously, not just at placement time, so a repaint that
    /// changes a garrisoned tile's owner re-owns the unit — including onto
    /// neutral land, where it becomes a viking raider. A unit is removed only
    /// where no valid unit can exist: on a non-neutral singleton (see
    /// <see cref="IsUnsustainableForUnit"/>), and for a
    /// <see cref="UnitLevel.Commander"/> turned neutral (no level-4 viking).
    /// Units on vanished tiles need no handling — the tile carries the
    /// occupant away with it.
    /// </summary>
    private static void ReconcileUnits(
        HexGrid grid, IReadOnlyList<Territory> territories)
    {
        foreach (Territory t in territories)
        {
            bool unsustainable = !t.Owner.IsNone
                && t.Coords.Count < CapitalPlacer.MinTerritorySizeForCapital;
            foreach (HexCoord coord in t.Coords)
            {
                HexTile? tile = grid.Get(coord);
                if (tile?.Occupant is not Unit unit) continue;

                if (unsustainable)
                {
                    tile.Occupant = null;
                    LogUnit($"remove {Off(coord)} level={unit.Level}: stranded on a non-neutral singleton");
                    continue;
                }
                if (unit.Owner == tile.Owner) continue;

                if (tile.Owner.IsNone && unit.Level == UnitLevel.Commander)
                {
                    tile.Occupant = null;
                    LogUnit($"remove {Off(coord)} level={unit.Level}: no level-4 viking exists");
                    continue;
                }

                tile.Occupant = new Unit(tile.Owner, unit.Level)
                {
                    HasMovedThisTurn = unit.HasMovedThisTurn,
                };
                LogUnit($"re-own {Off(coord)} level={unit.Level} " +
                    $"{OwnerLabel(unit.Owner)} -> {OwnerLabel(tile.Owner)}");
            }
        }
    }

    /// <summary>Offset col,row — the labeling the board render and the
    /// level-designer op grammar both use.</summary>
    private static string Off(HexCoord coord)
    {
        (int col, int row) = coord.ToOffset();
        return $"{col},{row}";
    }

    private static string OwnerLabel(PlayerId owner) =>
        owner.IsNone ? "viking" : $"p{owner.Index}";

    private static void LogUnit(string message) =>
        Log.Debug(Log.LogCategory.LevelDesign, $"[level] unit {message}");
}
