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
///     the one from the largest old territory wins (tiebreaker: lex-min);
///     losing capitals are physically removed from the grid.
///   - Placement that stomps a unit: the unit is destroyed (no refund).
/// </summary>
public static class CapitalReconciler
{
    public static IReadOnlyList<Territory> Reconcile(
        IReadOnlyList<Territory> rawNewTerritories,
        IReadOnlyList<Territory> oldTerritories,
        HexGrid grid)
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
            // land belongs to no player and produces no income (issue #39).
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

            if (inheritedOldCaps.Count == 0)
            {
                // No inherited capital — place a fresh one if the territory
                // is big enough. May stomp a unit.
                chosenCapital = CapitalPlacer.Choose(newT.Coords, grid);
                if (chosenCapital.HasValue)
                {
                    HexTile placeTile = grid.Get(chosenCapital.Value)!;
                    // Replace whatever was there (empty slot or a unit).
                    placeTile.Occupant = new Capital();
                }
            }
            else if (inheritedOldCaps.Count == 1)
            {
                // Single inherited capital stays put; no grid mutation.
                chosenCapital = inheritedOldCaps[0];
            }
            else
            {
                // Merge: largest old territory's capital wins.
                HexCoord winner = inheritedOldCaps[0];
                for (int i = 1; i < inheritedOldCaps.Count; i++)
                {
                    HexCoord candidate = inheritedOldCaps[i];
                    if (oldCapitalSize[candidate] > oldCapitalSize[winner])
                    {
                        winner = candidate;
                    }
                    else if (oldCapitalSize[candidate] == oldCapitalSize[winner]
                             && candidate.CompareTo(winner) < 0)
                    {
                        winner = candidate;
                    }
                }
                chosenCapital = winner;

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
}
