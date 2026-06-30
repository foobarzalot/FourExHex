using System.Collections.Generic;

/// <summary>
/// Pure, integer-only fog-of-war visibility rules from the single human
/// player's perspective. A tile is Visible if the human owns it or it lies
/// within one hex-ring of an owned tile; Stale if seen before but not currently
/// visible; Fog if never seen. Sight depends only on the human's territory, so
/// nothing here reads RNG, treasury, or AI state — AI behaviour and determinism
/// are unaffected by fog being on. Mirrors the static style of
/// <see cref="RisingTidesRules"/>.
/// </summary>
public static class VisibilityRules
{
    /// <summary>
    /// The set of coords currently in the human's sight: every tile they own,
    /// plus each owned tile's neighbours (a one-hex ring). Neighbours include
    /// water and off-map coords — the coastline and ocean immediately around the
    /// human's land are in sight; everything further out stays fogged until seen.
    /// </summary>
    public static HashSet<HexCoord> ComputeVisible(GameState state, PlayerId human)
    {
        var visible = new HashSet<HexCoord>();
        if (human.IsNone) return visible;

        foreach (HexTile tile in state.Grid.Tiles)
        {
            if (tile.Owner != human) continue;
            visible.Add(tile.Coord);
            foreach (HexCoord n in tile.Coord.Neighbors())
            {
                visible.Add(n);
            }
        }
        return visible;
    }

    /// <summary>
    /// Recompute the human's sight and refresh their last-seen memory for every
    /// currently-visible tile (owner + a deep-copied occupant). Sticky: tiles
    /// no longer in sight keep their previous snapshot. Returns the visible set
    /// so callers (the controller) can hand it to the view without recomputing.
    /// </summary>
    public static HashSet<HexCoord> UpdateMemory(GameState state, PlayerId human)
    {
        HashSet<HexCoord> visible = ComputeVisible(state, human);
        foreach (HexCoord coord in visible)
        {
            HexTile? tile = state.Grid.Get(coord);
            // Land tiles remember owner + occupant; water / off-map coords are
            // remembered as "seen" only (no owner, no occupant) so they degrade
            // to the stale tier rather than re-fogging.
            state.SetRemembered(coord, tile != null
                ? new RememberedTile(tile.Owner, HexOccupant.Clone(tile.Occupant))
                : new RememberedTile(PlayerId.None, null));
        }
        return visible;
    }

    /// <summary>
    /// Classify <paramref name="coord"/> given the current <paramref name="visible"/>
    /// set: in sight → Visible; else ever-seen → Stale; else Fog.
    /// </summary>
    public static VisibilityTier TierOf(HexCoord coord, IReadOnlySet<HexCoord> visible, GameState state)
    {
        if (visible.Contains(coord)) return VisibilityTier.Visible;
        return state.IsRemembered(coord) ? VisibilityTier.Stale : VisibilityTier.Fog;
    }
}
