// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// What a <see cref="BarbarianRules.PropagateAggro"/> pass flipped, so
/// live call sites can log the cause; simulator calls ignore it.
/// </summary>
public readonly record struct AggroFlipResult(int Compromised, int Spread)
{
    public bool Any => Compromised > 0 || Spread > 0;
}

/// <summary>
/// Barbarian aggro-state rules. Barbarians are <see cref="PlayerId.None"/>
/// units on neutral territories: passive (<see cref="Unit.IsAggro"/> false)
/// units only wander within their own territory; aggro ones run the viking
/// expansion logic. Aggro flips are one-way and state-derived — this class
/// is called after every territory recompute, from both the live capture
/// path and the AI simulator, so 1-ply lookahead and live play agree.
/// Static, Godot-free, integer-only; logging stays at the live call sites
/// (this code runs inside simulator clones).
/// </summary>
public static class BarbarianRules
{
    /// <summary>
    /// True iff <paramref name="territory"/> is a passive barbarian
    /// territory: neutral-owned, holding at least one unit, none aggro.
    /// The provoke-avoidance scoring and the wander gate both key off this.
    /// </summary>
    public static bool IsNonAggroBarbarianTerritory(Territory territory, HexGrid grid)
    {
        if (!territory.Owner.IsNone) return false;
        bool hasUnit = false;
        foreach (HexCoord coord in territory.Coords)
        {
            if (grid.Get(coord)?.Occupant is not Unit unit) continue;
            if (unit.IsAggro) return false;
            hasUnit = true;
        }
        return hasUnit;
    }

    /// <summary>
    /// Run after a territory recompute. Two passes:
    /// (a) compromise — a previously-neutral territory any of whose coords
    /// now has a real owner had ground captured from it; its surviving
    /// neutral units flip aggro. A coord that merely vanished (tide) or
    /// turned neutral is NOT a compromise.
    /// (b) spread — any current neutral territory containing an aggro unit
    /// flips all its units (aggro spreads on territory joins).
    /// </summary>
    public static AggroFlipResult PropagateAggro(
        GameState state, IReadOnlyList<Territory> previousTerritories)
    {
        int compromised = 0;
        int spread = 0;

        // (a) Compromise: a previously-neutral territory lost a coord to a
        // real owner — its surviving neutral units turn hostile.
        foreach (Territory prev in previousTerritories)
        {
            if (!prev.Owner.IsNone) continue;
            bool wasCompromised = false;
            foreach (HexCoord coord in prev.Coords)
            {
                PlayerId nowOwner = state.Grid.Get(coord)?.Owner ?? PlayerId.None;
                if (!nowOwner.IsNone)
                {
                    wasCompromised = true;
                    break;
                }
            }
            if (!wasCompromised) continue;
            foreach (HexCoord coord in prev.Coords)
            {
                HexTile? tile = state.Grid.Get(coord);
                if (tile == null || !tile.Owner.IsNone) continue;
                if (tile.Occupant is Unit unit && !unit.IsAggro)
                {
                    unit.IsAggro = true;
                    compromised++;
                }
            }
        }

        // (b) Spread: aggro is territory-wide — one aggro unit joining a
        // neutral territory (via a capture-merge) flips everyone in it.
        foreach (Territory territory in state.Territories)
        {
            if (!territory.Owner.IsNone) continue;
            bool anyAggro = false;
            foreach (HexCoord coord in territory.Coords)
            {
                if (state.Grid.Get(coord)?.Occupant is Unit { IsAggro: true })
                {
                    anyAggro = true;
                    break;
                }
            }
            if (!anyAggro) continue;
            foreach (HexCoord coord in territory.Coords)
            {
                if (state.Grid.Get(coord)?.Occupant is Unit { IsAggro: false } unit)
                {
                    unit.IsAggro = true;
                    spread++;
                }
            }
        }

        return new AggroFlipResult(compromised, spread);
    }

    /// <summary>
    /// Coords the locked tide forecast will actually sink (demote-only
    /// mountain steps keep their tile, so units there are not doomed).
    /// </summary>
    public static HashSet<HexCoord> TideDoomedCoords(GameState state)
    {
        var doomed = new HashSet<HexCoord>();
        foreach (TideStep step in state.PendingTide)
        {
            if (!step.DemoteOnly) doomed.Add(step.Coord);
        }
        return doomed;
    }

    /// <summary>
    /// Legal wander destinations for a passive barbarian: pure repositions
    /// only — empty tiles of its own neutral territory (no chops, graves,
    /// or captures), excluding tiles the tide is about to sink.
    /// </summary>
    public static List<HexCoord> WanderDestinations(
        GameState state,
        Territory territory,
        HexCoord unitCoord,
        UnitLevel level,
        IReadOnlySet<HexCoord>? doomed = null)
    {
        var results = new List<HexCoord>();
        foreach (HexCoord target in MovementRules.ValidTargets(
            level, territory, state.Grid, state.Territories))
        {
            if (target == unitCoord) continue;
            HexTile? tile = state.Grid.Get(target);
            if (tile == null || tile.Owner != territory.Owner) continue;
            if (tile.Occupant != null) continue;
            if (doomed != null && doomed.Contains(target)) continue;
            results.Add(target);
        }
        return results;
    }

    /// <summary>
    /// The Rising Tides "cornered" trigger, run at the neutral seat's turn
    /// start once the forecast is locked: a passive barbarian standing on a
    /// doomed tile with no wander escape is about to be lost — the tide
    /// itself is not an attacker, but a certain unit loss aggros its whole
    /// territory (retreat first, aggro only when cornered). Returns the
    /// number of units flipped.
    /// </summary>
    public static int AggroCorneredByTide(GameState state)
    {
        HashSet<HexCoord> doomed = TideDoomedCoords(state);
        if (doomed.Count == 0) return 0;

        int flipped = 0;
        foreach (HexCoord coord in doomed)
        {
            HexTile? tile = state.Grid.Get(coord);
            if (tile == null || !tile.Owner.IsNone) continue;
            if (tile.Occupant is not Unit { IsAggro: false } unit) continue;
            Territory? territory = TerritoryLookup.FindOwnedContaining(
                state.Territories, PlayerId.None, coord);
            if (territory == null) continue;
            if (WanderDestinations(state, territory, coord, unit.Level, doomed).Count > 0)
            {
                continue; // an escape exists — the wander beat retreats instead
            }
            foreach (HexCoord c in territory.Coords)
            {
                if (state.Grid.Get(c)?.Occupant is Unit { IsAggro: false } u)
                {
                    u.IsAggro = true;
                    flipped++;
                }
            }
        }
        return flipped;
    }
}
