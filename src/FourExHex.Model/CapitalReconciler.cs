// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Post-processes raw flood-fill output to build a <see cref="Territory"/>
/// list with correct capital assignments, mutating the grid's
/// <see cref="Capital"/> occupants along the way. Handles:
///   - Initial placement on a fresh grid (all territories start with no
///     inherited capital; each multi-hex one gets a new capital via
///     <see cref="CapitalPlacer"/>).
///   - Preservation of unchanged capitals across captures.
///   - Splits: the piece containing the old capital keeps it; other
///     pieces get a new capital placed.
///   - Merges: when multiple old capitals end up in one new territory,
///     the one matching <c>originCapital</c> (the capital of the territory
///     the acting unit came from, when the caller supplies it) wins; else
///     the one from the largest old territory wins (tiebreaker: lex-min);
///     losing capitals are physically removed from the grid.
///   - Placement that stomps a unit: the unit is destroyed (no refund).
///
/// When <c>randomize</c> is true the two tie-breaks that historically
/// resolved to the lex-min coord — fresh placement within the chosen
/// occupant tier and the equal-old-size merge tiebreak — instead pick a
/// seed-deterministic random candidate. The randomness is drawn from a
/// <see cref="DeterministicRng"/> seeded purely from the territory's own coords
/// (<see cref="SeedFromCoords"/>), so it never touches the live per-turn RNG
/// stream and the AI's cloned 1-ply simulation reproduces the identical pick.
/// </summary>
public static class CapitalReconciler
{
    public static IReadOnlyList<Territory> Reconcile(
        IReadOnlyList<Territory> rawNewTerritories,
        IReadOnlyList<Territory> oldTerritories,
        HexGrid grid,
        bool randomize = false,
        HexCoord? originCapital = null)
    {
        // Remember each old territory's capital + size so merge ties can
        // be broken.
        var oldCapitalSize = new Dictionary<HexCoord, int>();
        foreach (Territory old in oldTerritories)
        {
            if (old.HasCapital)
            {
                oldCapitalSize[old.Capital!.Value] = old.Size;
            }
        }

        var result = new List<Territory>(rawNewTerritories.Count);
        foreach (Territory newT in rawNewTerritories)
        {
            // Neutral (unowned) territories never get a capital — neutral
            // land belongs to no player and produces no income.
            // A Capital occupant sitting on neutral land is an upstream
            // paint bug, so surface it by throwing rather than silently
            // stripping it.
            if (newT.Owner.IsNone)
            {
                foreach (HexCoord c in newT.Coords)
                {
                    if (grid.Get(c)?.Occupant is Capital)
                    {
                        throw new System.InvalidOperationException(
                            $"Capital occupant found on neutral (unowned) tile {c}; " +
                            "neutral land must never hold a capital.");
                    }
                }
                result.Add(new Territory(newT.Owner, newT.Coords, capital: null));
                continue;
            }

            // Singletons never have a capital. If the new territory
            // shrank to one tile (e.g. a split stranded the old capital
            // alone), strip any lingering Capital occupant so the grid
            // state matches the Territory record.
            if (newT.Coords.Count < 2)
            {
                foreach (HexCoord c in newT.Coords)
                {
                    HexTile? tile = grid.Get(c);
                    if (tile?.Occupant is Capital)
                    {
                        tile.Occupant = null;
                    }
                }
                result.Add(new Territory(newT.Owner, newT.Coords, capital: null));
                continue;
            }

            // Find every coord in this new territory that currently holds
            // a Capital occupant AND was a capital in the old layout.
            var inheritedOldCaps = new List<HexCoord>();
            foreach (HexCoord c in newT.Coords)
            {
                HexTile? tile = grid.Get(c);
                if (tile?.Occupant is Capital && oldCapitalSize.ContainsKey(c))
                {
                    inheritedOldCaps.Add(c);
                }
            }

            HexCoord? chosenCapital;

            // Per-territory RNG seeded from this territory's own coords, so the
            // pick is reproducible everywhere the same board is reconciled
            // (live capture, the AI's cloned simulation, replay re-derivation)
            // without consuming the live per-turn stream. Every in-game
            // reconcile passes randomize: true; the null path (lex-min) is
            // the editor/fixture affordance.
            DeterministicRng? capitalRng = randomize ? new DeterministicRng(SeedFromCoords(newT.Coords)) : null;

            if (inheritedOldCaps.Count == 0)
            {
                // No inherited capital — place a fresh one if the territory is
                // big enough. May stomp a unit. Any 2+ region now has a legal
                // site (mountains included), so Choose returns null
                // only for the impossible all-Capital case — guarded defensively.
                chosenCapital = CapitalPlacer.Choose(newT.Coords, grid, capitalRng);
                if (chosenCapital.HasValue)
                {
                    HexTile placeTile = grid.Get(chosenCapital.Value)!;
                    // Replace whatever was there (empty slot or a unit).
                    placeTile.Occupant = new Capital();
                    Log.Debug(Log.LogCategory.Capture,
                        $"[reconcile] owner={newT.Owner.Index} size={newT.Coords.Count} " +
                        $"placed capital {chosenCapital.Value} ({(randomize ? "randomized" : "lex-min")})");
                }
                else
                {
                    Log.Debug(Log.LogCategory.Turn,
                        $"[reconcile] region owner={newT.Owner.Index} " +
                        $"size={newT.Coords.Count} left capital-less (no placeable tile)");
                }
            }
            else if (inheritedOldCaps.Count == 1)
            {
                // Single inherited capital stays put; no grid mutation.
                chosenCapital = inheritedOldCaps[0];
            }
            else
            {
                // Merge: the capital of the territory the acting unit
                // originated from wins outright when the caller supplies it.
                // Otherwise (no unit action, or the origin had no capital
                // among the merged ones) the capital from the largest old
                // territory wins; among capitals tied on largest old size,
                // the lex-min coord wins (capitalRng == null) or a random
                // one of them does.
                HexCoord winner;
                string rule;
                if (originCapital.HasValue && inheritedOldCaps.Contains(originCapital.Value))
                {
                    winner = originCapital.Value;
                    rule = "origin";
                }
                else
                {
                    int maxOldSize = int.MinValue;
                    foreach (HexCoord cap in inheritedOldCaps)
                    {
                        if (oldCapitalSize[cap] > maxOldSize) maxOldSize = oldCapitalSize[cap];
                    }
                    var topTied = new List<HexCoord>();
                    foreach (HexCoord cap in inheritedOldCaps)
                    {
                        if (oldCapitalSize[cap] == maxOldSize) topTied.Add(cap);
                    }
                    topTied.Sort();
                    winner = capitalRng == null
                        ? topTied[0]
                        : topTied[capitalRng.NextBounded(topTied.Count)];
                    rule = topTied.Count == 1
                        ? "largest"
                        : (capitalRng == null ? "tiebreak-lexmin" : "tiebreak-random");
                }
                chosenCapital = winner;

                var candidateDesc = new System.Text.StringBuilder();
                foreach (HexCoord cap in inheritedOldCaps)
                {
                    if (candidateDesc.Length > 0) candidateDesc.Append(' ');
                    candidateDesc.Append($"{cap}(size {oldCapitalSize[cap]})");
                }
                Log.Debug(Log.LogCategory.Capture,
                    $"[reconcile] merge owner={newT.Owner.Index} candidates: {candidateDesc} " +
                    $"origin={(originCapital.HasValue ? originCapital.Value.ToString() : "(none)")} " +
                    $"winner={winner} rule={rule}");

                // Demote losers: remove their Capital occupant so the tile
                // becomes empty.
                foreach (HexCoord loser in inheritedOldCaps)
                {
                    if (loser != winner)
                    {
                        HexTile loserTile = grid.Get(loser)!;
                        if (loserTile.Occupant is Capital)
                        {
                            loserTile.Occupant = null;
                        }
                    }
                }
            }

            result.Add(new Territory(newT.Owner, newT.Coords, chosenCapital));
        }

        return result;
    }

    /// <summary>
    /// Deterministic 32-bit seed derived purely from a territory's coords.
    /// The coords are sorted first so the seed is independent of enumeration
    /// order; an FNV-1a fold over (Q, R) then a splitmix32 avalanche spreads
    /// adjacent coord sets to uncorrelated seeds. Integer-only (the no-floats
    /// rule). Two reconciles of the same board → same coords → same seed → the
    /// same randomized capital, which is what keeps live play, the AI's cloned
    /// simulation, and replay re-derivation in lockstep.
    /// </summary>
    private static int SeedFromCoords(IReadOnlyCollection<HexCoord> coords)
    {
        var sorted = new List<HexCoord>(coords);
        sorted.Sort();
        unchecked
        {
            uint x = 2166136261u; // FNV-1a offset basis
            foreach (HexCoord c in sorted)
            {
                x = (x ^ (uint)c.Q) * 16777619u;
                x = (x ^ (uint)c.R) * 16777619u;
            }
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (int)x;
        }
    }
}
