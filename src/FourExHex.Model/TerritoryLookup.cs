// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Pure lookups over a territory list. Shared by GameController
/// (real play) and AiSimulator (simulated play) so both paths
/// resolve territories identically.
/// </summary>
public static class TerritoryLookup
{
    /// <summary>
    /// First territory (in iteration order) whose coords contain
    /// <paramref name="coord"/>, regardless of owner. Territory partitions
    /// are disjoint per CapitalReconciler, so iteration order doesn't
    /// affect correctness.
    /// </summary>
    public static Territory? FindContaining(
        IReadOnlyList<Territory> territories, HexCoord coord)
    {
        foreach (Territory t in territories)
        {
            if (t.Contains(coord)) return t;
        }
        return null;
    }

    public static Territory? FindOwnedContaining(
        IReadOnlyList<Territory> territories, PlayerId owner, HexCoord coord)
    {
        foreach (Territory t in territories)
        {
            if (t.Owner == owner && t.Contains(coord))
            {
                return t;
            }
        }
        return null;
    }

    public static Territory? FindByCapital(
        IReadOnlyList<Territory> territories, HexCoord capital)
    {
        foreach (Territory t in territories)
        {
            if (t.HasCapital && t.Capital!.Value == capital)
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>
    /// A territory's stable identity coord: its capital, or — for the
    /// capital-less neutral (viking) territories the AI also iterates — its
    /// lex-min coord. Used as the visited-set key and ordering tie-break in
    /// <see cref="ComputerAi.ChooseNextAction"/>; identical to the capital
    /// for every capital-bearing territory, so player behavior is unchanged.
    /// </summary>
    public static HexCoord AnchorCoord(Territory territory)
    {
        if (territory.HasCapital) return territory.Capital!.Value;
        HexCoord min = default;
        bool first = true;
        foreach (HexCoord c in territory.Coords)
        {
            if (first || c.CompareTo(min) < 0)
            {
                min = c;
                first = false;
            }
        }
        return min;
    }

    /// <summary>
    /// Every territory owned by <paramref name="owner"/> that still has a
    /// capital — i.e. the territories that own a treasury and can still
    /// take actions. The "alive" set from this owner's perspective:
    /// territories that lost their capital to capture are excluded.
    /// </summary>
    public static IEnumerable<Territory> OwnedCapitalBearing(
        IReadOnlyList<Territory> territories, PlayerId owner)
    {
        foreach (Territory t in territories)
        {
            if (t.Owner == owner && t.HasCapital) yield return t;
        }
    }
}
