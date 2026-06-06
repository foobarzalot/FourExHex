using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Deterministic SHA-256 hex digest over the gameplay-relevant fields
/// of a <see cref="GameState"/>: tile owners + occupants, treasury
/// gold per capital, territory partition, and turn state. Used by
/// replay-fidelity tests to assert that the post-replay live state
/// matches the pre-replay saved state exactly.
///
/// Canonicalization rules — every collection is sorted so the digest
/// doesn't depend on iteration order:
///   • Tiles by (Q, R).
///   • Gold entries by capital (Q, R).
///   • Territories by capital (Q, R), with orphans (no capital) sorted
///     last by the lex-min coord in the territory.
/// Owners are written as player indices (<see cref="PlayerId.Index"/>,
/// or -1 for <see cref="PlayerId.None"/>) so the digest is invariant
/// under cosmetic color changes — same integers as before the
/// color → PlayerId migration, so the digest is byte-stable.
/// </summary>
public static class GameStateChecksum
{
    /// <summary>
    /// Compute the SHA-256 hex digest of <paramref name="state"/>.
    /// Two states with the same digest are functionally identical for
    /// save/load purposes — see <see cref="SaveSerializer.Serialize"/>.
    /// </summary>
    public static string Compute(GameState state)
    {
        string canonical = Stringify(state);
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
        return System.Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// The canonical pre-hash string used by <see cref="Compute"/>.
    /// Exposed so callers diagnosing a checksum mismatch can diff the
    /// stringified inputs directly instead of staring at hex digests.
    /// </summary>
    public static string Stringify(GameState state)
    {
        var sb = new StringBuilder();

        var tiles = new List<HexTile>(state.Grid.Tiles);
        tiles.Sort((a, b) => a.Coord.CompareTo(b.Coord));
        foreach (HexTile tile in tiles)
        {
            sb.Append("T|");
            sb.Append(tile.Coord.Q).Append(',').Append(tile.Coord.R).Append('|');
            sb.Append(OwnerIndex(tile.Owner)).Append('|');
            AppendOccupant(sb, tile.Occupant);
            sb.Append('\n');
        }

        var goldEntries = new List<(HexCoord Coord, int Gold)>();
        foreach (Territory t in state.Territories)
        {
            if (!t.HasCapital) continue;
            HexCoord cap = t.Capital!.Value;
            goldEntries.Add((cap, state.Treasury.GetGold(cap)));
        }
        goldEntries.Sort((a, b) => a.Coord.CompareTo(b.Coord));
        foreach ((HexCoord coord, int gold) in goldEntries)
        {
            sb.Append("G|").Append(coord.Q).Append(',').Append(coord.R)
              .Append('|').Append(gold).Append('\n');
        }

        var territoryRows = new List<(HexCoord Key, string Row)>();
        foreach (Territory t in state.Territories)
        {
            HexCoord key;
            if (t.HasCapital)
            {
                key = t.Capital!.Value;
            }
            else
            {
                // Orphan territories: sort by lex-min coord. Tagged
                // separately in the row so a "capital-at-(0,0)" entry
                // doesn't collide with an "orphan-min-at-(0,0)" entry.
                key = t.Coords.Min();
            }
            string row = $"R|{OwnerIndex(t.Owner)}|size={t.Coords.Count}|"
                + (t.HasCapital
                    ? $"cap={t.Capital!.Value.Q},{t.Capital!.Value.R}"
                    : $"orphan@{key.Q},{key.R}");
            territoryRows.Add((key, row));
        }
        // Two-key sort: capital-bearing territories (which always have
        // a unique capital coord) first, then orphans by lex-min coord.
        territoryRows.Sort((a, b) => a.Key.CompareTo(b.Key));
        foreach ((HexCoord _, string row) in territoryRows)
        {
            sb.Append(row).Append('\n');
        }

        sb.Append("Turn|").Append(state.Turns.TurnNumber)
          .Append('|').Append(state.Turns.CurrentPlayerIndex).Append('\n');

        return sb.ToString();
    }

    private static int OwnerIndex(PlayerId owner) => owner.IsNone ? -1 : owner.Index;

    private static void AppendOccupant(StringBuilder sb, HexOccupant? occupant)
    {
        switch (occupant)
        {
            case null:
                sb.Append("none");
                return;
            case Unit u:
                sb.Append("Unit:").Append(OwnerIndex(u.Owner))
                  .Append(':').Append(u.Level)
                  .Append(':').Append(u.HasMovedThisTurn);
                return;
            case Capital:
                sb.Append("Capital");
                return;
            case Tower:
                sb.Append("Tower");
                return;
            case Tree:
                sb.Append("Tree");
                return;
            case Grave:
                sb.Append("Grave");
                return;
            default:
                throw new System.InvalidOperationException(
                    $"Unknown HexOccupant subtype in checksum: {occupant.GetType().Name}");
        }
    }
}
