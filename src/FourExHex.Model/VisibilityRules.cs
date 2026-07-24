// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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
    /// The set of coords currently in the human's sight: every tile in one of
    /// their capital-bearing territories, plus each such tile's neighbours (a
    /// one-hex ring). Singletons — size-1 territories with no capital — grant no
    /// sight at all. Neighbours include water and off-map coords, so the
    /// coastline and ocean immediately around the human's land are in sight;
    /// everything further out stays fogged until seen.
    /// </summary>
    public static HashSet<HexCoord> ComputeVisible(GameState state, PlayerId human)
    {
        var visible = new HashSet<HexCoord>();
        if (human.IsNone) return visible;

        foreach (Territory territory in state.Territories)
        {
            if (territory.Owner != human) continue;
            if (!territory.HasCapital) continue; // singleton: part of no real territory
            foreach (HexCoord coord in territory.Coords)
            {
                visible.Add(coord);
                foreach (HexCoord n in coord.Neighbors())
                {
                    visible.Add(n);
                }
            }
        }
        return visible;
    }

    /// <summary>
    /// Recompute the human's sight and mark every currently-visible coord as
    /// seen. Sticky: coords already seen stay seen. The stale tier shows only
    /// static terrain (no owner, no occupant), so a seen-coord set is all that's
    /// recorded. Returns the visible set so callers (the controller) can hand it
    /// to the view without recomputing.
    /// </summary>
    public static HashSet<HexCoord> UpdateSeen(GameState state, PlayerId human)
    {
        HashSet<HexCoord> visible = ComputeVisible(state, human);
        foreach (HexCoord coord in visible)
        {
            state.MarkSeen(coord);
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
        return state.IsSeen(coord) ? VisibilityTier.Stale : VisibilityTier.Fog;
    }

    /// <summary>
    /// The single human the fog is drawn through, or null when fog doesn't
    /// apply and the whole map is rendered: not Fog Of War, not exactly one
    /// human, or that human is eliminated (no capital-bearing territory —
    /// defeat reveals everything, so they spectate the surviving AIs with full
    /// vision). A null perspective also means the win checks are ungated, which
    /// is what keeps all-AI runs (the FOUREXHEX_6AI diagnostic modes, the
    /// campaign winner sweep) on ordinary full-grid rules.
    /// </summary>
    private static PlayerId? SoleHumanPerspective(GameState state)
    {
        if (!state.FogEnabled) return null;
        PlayerId? human = null;
        foreach (Player p in state.Players)
        {
            if (p.Kind != PlayerKind.Human) continue;
            if (human != null) return null; // more than one human → no fog
            human = p.Id;
        }
        if (human == null) return null;
        if (WinConditionRules.IsEliminated(human.Value, state.Grid)) return null;
        return human;
    }

    /// <summary>
    /// How many land tiles are still fully hidden from the human: at
    /// <see cref="VisibilityTier.Fog"/> and not owned by them. Owning a tile is
    /// knowing it — sight comes only from capital-bearing territories, so an
    /// isolated singleton is owned yet never seen, and counting it would strand
    /// a player who has taken the whole map. Water is excluded structurally
    /// (water coords are not in the grid), so an unexplored ocean never counts.
    /// Always 0 when <see cref="SoleHumanPerspective"/> is null. Pure — unlike
    /// <see cref="UpdateSeen"/> this marks nothing, so the win checks can call
    /// it after every capture without granting free sight.
    /// </summary>
    public static int HiddenLandCount(GameState state)
    {
        PlayerId? human = SoleHumanPerspective(state);
        if (human == null) return 0;

        HashSet<HexCoord> visible = ComputeVisible(state, human.Value);
        int hidden = 0;
        foreach (HexTile tile in state.Grid.Tiles)
        {
            if (tile.Owner == human.Value) continue;
            if (TierOf(tile.Coord, visible, state) == VisibilityTier.Fog) hidden++;
        }
        return hidden;
    }

    /// <summary>
    /// True while any land tile remains fully hidden from the human
    /// (<see cref="HiddenLandCount"/> &gt; 0). Always false outside
    /// <see cref="GameMode.FogOfWar"/>. While true, no win condition may fire
    /// and the claim-victory prompt is never offered — see the mode-branched
    /// checks in GameOperations / GameController, which gate on this the same
    /// way Viking Raiders gates on
    /// <see cref="VikingRaidersRules.ThreatRemains"/>.
    /// </summary>
    public static bool HiddenLandRemains(GameState state) => HiddenLandCount(state) > 0;

    /// <summary>
    /// Build the fog projection the view renders, or null when fog doesn't apply
    /// (see <see cref="SoleHumanPerspective"/> — in which case the caller
    /// renders everything). Marks the human's newly-visible coords as seen as a
    /// side effect. Shared by the live controller and the menu map thumbnail so
    /// both pick the same perspective.
    /// </summary>
    public static FogView? BuildProjection(GameState state)
    {
        PlayerId? human = SoleHumanPerspective(state);
        if (human == null) return null;

        HashSet<HexCoord> visible = UpdateSeen(state, human.Value);
        return new FogView(visible, state.Seen);
    }
}
