using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Immutable capture of the player-intent slice of <see cref="SessionState"/>:
/// the selected territory (by anchor coord, not reference, so it survives
/// territory rebuilds), the pending <see cref="SessionState.ActionMode"/>,
/// the move source (if any), the repeated-movement sticky bit, and the
/// visited-territory capitals that drive Tab-cycle ordering.
/// Pairs with <see cref="GameStateSnapshot"/> inside an <see cref="UndoEntry"/>
/// so undo/redo can restore where the player was, not just what was on the
/// board.
/// </summary>
public sealed record SessionStateSnapshot(
    HexCoord? SelectedAnchor,
    SessionState.ActionMode Mode,
    HexCoord? MoveSource,
    bool RepeatedMovement,
    IReadOnlyList<HexCoord> VisitedCapitals)
{
    /// <summary>
    /// Snapshot the player-intent fields of <paramref name="session"/>.
    /// The selected territory is reduced to an anchor coord (capital if
    /// present, otherwise the first coord) so a later restore can find
    /// the matching territory by membership instead of relying on
    /// reference identity. The visited set is captured sorted so two
    /// snapshots of the same set compare equal regardless of hash order.
    /// </summary>
    public static SessionStateSnapshot Capture(SessionState session)
    {
        HexCoord? anchor = null;
        Territory? selected = session.SelectedTerritory;
        if (selected != null)
        {
            if (selected.Capital.HasValue)
            {
                anchor = selected.Capital;
            }
            else
            {
                foreach (HexCoord c in selected.Coords)
                {
                    anchor = c;
                    break;
                }
            }
        }
        HexCoord[] visited = session.VisitedTerritoryCapitals.OrderBy(c => c).ToArray();
        return new SessionStateSnapshot(
            anchor, session.Mode, session.MoveSource, session.RepeatedMovement, visited);
    }

    /// <summary>
    /// Restore <paramref name="session"/>'s intent fields from this
    /// snapshot. <paramref name="restoredTerritories"/> is the list
    /// produced by the paired <see cref="GameStateSnapshot.ApplyTo"/>
    /// — we look up the matching territory by anchor membership.
    /// If the anchor no longer maps to any territory, selection is
    /// cleared.
    /// </summary>
    public void ApplyTo(SessionState session, IReadOnlyList<Territory> restoredTerritories)
    {
        Territory? match = null;
        if (SelectedAnchor.HasValue)
        {
            HexCoord anchor = SelectedAnchor.Value;
            foreach (Territory t in restoredTerritories)
            {
                if (t.Coords.Contains(anchor))
                {
                    match = t;
                    break;
                }
            }
        }
        session.SelectedTerritory = match;
        session.Mode = Mode;
        session.MoveSource = MoveSource;
        session.RepeatedMovement = RepeatedMovement;
        session.VisitedTerritoryCapitals.Clear();
        session.VisitedTerritoryCapitals.UnionWith(VisitedCapitals);
    }

    /// <summary>
    /// Value equality including the visited list (sequence compare —
    /// the synthesized record Equals would compare the list by
    /// reference, breaking TrackHandler's pushed-iff-changed de-dup).
    /// </summary>
    public bool Equals(SessionStateSnapshot? other) =>
        other is not null
        && Nullable.Equals(SelectedAnchor, other.SelectedAnchor)
        && Mode == other.Mode
        && Nullable.Equals(MoveSource, other.MoveSource)
        && RepeatedMovement == other.RepeatedMovement
        && VisitedCapitals.SequenceEqual(other.VisitedCapitals);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SelectedAnchor);
        hash.Add(Mode);
        hash.Add(MoveSource);
        hash.Add(RepeatedMovement);
        foreach (HexCoord c in VisitedCapitals)
        {
            hash.Add(c);
        }
        return hash.ToHashCode();
    }
}
