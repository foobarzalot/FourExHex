using System.Collections.Generic;

/// <summary>
/// Post-processes the raw output of <see cref="TerritoryFinder.FindAll"/>
/// after a capture, overriding capital choices for territories that inherit
/// multiple old capitals (merges). The rule: the biggest old territory's
/// capital wins; ties break on lex-min (R, Q).
/// </summary>
public static class TerritoryReconciler
{
    /// <summary>
    /// For each new territory that contains two or more old capitals,
    /// replace it with an equivalent Territory whose Capital is the capital
    /// of the largest old territory (tiebreaker: lex-min). Territories with
    /// zero or one inherited old capital are returned unchanged.
    /// </summary>
    public static IReadOnlyList<Territory> OverrideMergeWinners(
        IReadOnlyList<Territory> rawNewTerritories,
        IReadOnlyList<Territory> oldTerritories)
    {
        // Index old capitals -> size of their old territory.
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
            if (!newT.HasCapital)
            {
                result.Add(newT);
                continue;
            }

            // Find every old capital coord that's still in the new territory.
            var inherited = new List<HexCoord>();
            foreach (HexCoord coord in newT.Coords)
            {
                if (oldCapitalSize.ContainsKey(coord))
                {
                    inherited.Add(coord);
                }
            }

            if (inherited.Count == 0)
            {
                // Brand-new territory (split piece with no old capital in
                // it) — CapitalAssigner's lex-min pick stands.
                result.Add(newT);
                continue;
            }

            // At least one inherited old capital exists — the new capital
            // MUST come from the inherited set, even if there's only one.
            // Otherwise CapitalAssigner's lex-min pick could displace a
            // preserved capital when a smaller coord joins the territory
            // (e.g., merging with a singleton hex).
            HexCoord winner = inherited[0];
            for (int i = 1; i < inherited.Count; i++)
            {
                HexCoord candidate = inherited[i];
                if (oldCapitalSize[candidate] > oldCapitalSize[winner])
                {
                    winner = candidate;
                }
                else if (oldCapitalSize[candidate] == oldCapitalSize[winner]
                         && IsLessThan(candidate, winner))
                {
                    winner = candidate;
                }
            }

            if (winner == newT.Capital)
            {
                result.Add(newT);
            }
            else
            {
                result.Add(new Territory(newT.Owner, newT.Coords, winner));
            }
        }

        return result;
    }

    private static bool IsLessThan(HexCoord a, HexCoord b) =>
        a.R < b.R || (a.R == b.R && a.Q < b.Q);
}
