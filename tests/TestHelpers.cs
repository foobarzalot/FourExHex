using System.Collections.Generic;

namespace FourExHex.Tests;

/// <summary>
/// Shared helpers for tests that want a fully-built territory list from a
/// grid (flood-fill + capital placement). Single-call replacement for the
/// old <c>TerritoryFinder.FindAll(grid)</c> behavior before the occupant
/// refactor split capital placement into its own step.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Run <see cref="TerritoryFinder.FindAll"/> followed by
    /// <see cref="CapitalReconciler.Reconcile"/> against no prior
    /// territories, producing a territory list with capitals placed.
    /// Mutates <paramref name="grid"/> by adding Capital occupants.
    /// </summary>
    public static IReadOnlyList<Territory> BuildTerritoriesFromGrid(HexGrid grid)
    {
        var raw = TerritoryFinder.FindAll(grid);
        return CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);
    }
}
