using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class SessionStateSnapshotTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static (HexGrid grid, IReadOnlyList<Territory> territories) BuildTwoColorGrid()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(3, 2, Blue);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = Red;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return (grid, territories);
    }

    [Fact]
    public void Capture_Then_Apply_RoundTripsSelectionAndModeAndSource()
    {
        (HexGrid grid, IReadOnlyList<Territory> territories) = BuildTwoColorGrid();
        Territory red = territories[0].Owner == Red ? territories[0] : territories[1];

        var session = new SessionState
        {
            SelectedTerritory = red,
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = new HexCoord(2, 3),
        };

        SessionStateSnapshot snap = SessionStateSnapshot.Capture(session);

        // Mutate the live session.
        session.SelectedTerritory = null;
        session.Mode = SessionState.ActionMode.None;
        session.MoveSource = null;

        snap.ApplyTo(session, territories);

        Assert.Same(red, session.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.MovingUnit, session.Mode);
        Assert.Equal(new HexCoord(2, 3), session.MoveSource);
    }

    [Fact]
    public void Capture_WithNoSelection_RoundTripsAsNullAnchor()
    {
        var session = new SessionState
        {
            SelectedTerritory = null,
            Mode = SessionState.ActionMode.None,
            MoveSource = null,
        };

        SessionStateSnapshot snap = SessionStateSnapshot.Capture(session);

        Assert.Null(snap.SelectedAnchor);

        // Apply to a fresh session that has selection set — should clear it.
        (_, IReadOnlyList<Territory> territories) = BuildTwoColorGrid();
        var other = new SessionState
        {
            SelectedTerritory = territories[0],
            Mode = SessionState.ActionMode.BuyingRecruit,
            MoveSource = new HexCoord(1, 1),
        };
        snap.ApplyTo(other, territories);

        Assert.Null(other.SelectedTerritory);
        Assert.Equal(SessionState.ActionMode.None, other.Mode);
        Assert.Null(other.MoveSource);
    }

    [Fact]
    public void Apply_LooksUpTerritoryByAnchorMembership_NotReferenceEquality()
    {
        // Snapshot taken with one territory list; apply against a freshly
        // built (different-reference) territory list. Anchor coord should
        // resolve to the new instance that contains the same coord.
        (HexGrid grid, IReadOnlyList<Territory> originalTerritories) = BuildTwoColorGrid();
        Territory originalRed = originalTerritories[0].Owner == Red
            ? originalTerritories[0] : originalTerritories[1];

        var session = new SessionState { SelectedTerritory = originalRed };
        SessionStateSnapshot snap = SessionStateSnapshot.Capture(session);

        // Build a fresh territory list — different Territory instances
        // covering the same coords.
        IReadOnlyList<Territory> rebuilt = TestHelpers.BuildTerritoriesFromGrid(grid);
        Territory rebuiltRed = rebuilt[0].Owner == Red ? rebuilt[0] : rebuilt[1];
        Assert.NotSame(originalRed, rebuiltRed);

        var fresh = new SessionState();
        snap.ApplyTo(fresh, rebuilt);

        Assert.Same(rebuiltRed, fresh.SelectedTerritory);
    }

    [Fact]
    public void Apply_WithAnchorThatNoLongerMapsToAnyTerritory_ClearsSelection()
    {
        // Anchor points at a coord that exists in no territory (e.g.,
        // because the territory was wiped or the coord never existed).
        var snap = new SessionStateSnapshot(
            SelectedAnchor: new HexCoord(99, 99),
            Mode: SessionState.ActionMode.BuyingRecruit,
            MoveSource: null,
            RepeatedMovement: false,
            VisitedCapitals: System.Array.Empty<HexCoord>(),
            VisitedThisTurnCapitals: System.Array.Empty<HexCoord>(),
            SelectionWasRevisit: false,
            EndTurnCtaLatched: false);

        (_, IReadOnlyList<Territory> territories) = BuildTwoColorGrid();
        var session = new SessionState();
        snap.ApplyTo(session, territories);

        Assert.Null(session.SelectedTerritory);
        // Mode is still restored — only selection failed to resolve.
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, session.Mode);
    }

    [Fact]
    public void Equals_TreatsSnapshotsWithSameAnchorAndModeAndSource_AsEqual()
    {
        // Record equality is what powers TrackHandler's de-dup check.
        var a = new SessionStateSnapshot(
            new HexCoord(1, 2), SessionState.ActionMode.BuyingSoldier, new HexCoord(3, 4), false,
            System.Array.Empty<HexCoord>(), System.Array.Empty<HexCoord>(), false, false);
        var b = new SessionStateSnapshot(
            new HexCoord(1, 2), SessionState.ActionMode.BuyingSoldier, new HexCoord(3, 4), false,
            System.Array.Empty<HexCoord>(), System.Array.Empty<HexCoord>(), false, false);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equals_TreatsDifferentModes_AsUnequal()
    {
        var a = new SessionStateSnapshot(
            null, SessionState.ActionMode.None, null, false, System.Array.Empty<HexCoord>(),
            System.Array.Empty<HexCoord>(), false, false);
        var b = new SessionStateSnapshot(
            null, SessionState.ActionMode.BuyingRecruit, null, false, System.Array.Empty<HexCoord>(),
            System.Array.Empty<HexCoord>(), false, false);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Equals_ComparesVisitedCapitalsBySequence_NotReference()
    {
        // Distinct array instances with the same contents must compare
        // equal (TrackHandler's de-dup) and differing contents unequal.
        var a = new SessionStateSnapshot(
            null, SessionState.ActionMode.None, null, false,
            new[] { new HexCoord(1, 2), new HexCoord(3, 4) },
            System.Array.Empty<HexCoord>(), false, false);
        var same = new SessionStateSnapshot(
            null, SessionState.ActionMode.None, null, false,
            new[] { new HexCoord(1, 2), new HexCoord(3, 4) },
            System.Array.Empty<HexCoord>(), false, false);
        var different = new SessionStateSnapshot(
            null, SessionState.ActionMode.None, null, false,
            new[] { new HexCoord(1, 2) },
            System.Array.Empty<HexCoord>(), false, false);

        Assert.Equal(a, same);
        Assert.NotEqual(a, different);
    }

    [Fact]
    public void Capture_Then_Apply_RoundTripsVisitedCapitals()
    {
        var session = new SessionState();
        session.VisitedTerritoryCapitals.Add(new HexCoord(1, 2));
        session.VisitedTerritoryCapitals.Add(new HexCoord(3, 4));

        SessionStateSnapshot snap = SessionStateSnapshot.Capture(session);

        session.VisitedTerritoryCapitals.Clear();
        session.VisitedTerritoryCapitals.Add(new HexCoord(9, 9));

        (_, IReadOnlyList<Territory> territories) = BuildTwoColorGrid();
        snap.ApplyTo(session, territories);

        Assert.Equal(
            new HashSet<HexCoord> { new HexCoord(1, 2), new HexCoord(3, 4) },
            session.VisitedTerritoryCapitals);
    }

    [Fact]
    public void Capture_Then_Apply_RoundTripsVisitedThisTurnAndRevisitAndLatch()
    {
        var session = new SessionState();
        session.VisitedThisTurnCapitals.Add(new HexCoord(1, 2));
        session.VisitedThisTurnCapitals.Add(new HexCoord(3, 4));
        session.SelectionWasRevisit = true;
        session.EndTurnCtaLatched = true;

        SessionStateSnapshot snap = SessionStateSnapshot.Capture(session);

        session.VisitedThisTurnCapitals.Clear();
        session.VisitedThisTurnCapitals.Add(new HexCoord(9, 9));
        session.SelectionWasRevisit = false;
        session.EndTurnCtaLatched = false;

        (_, IReadOnlyList<Territory> territories) = BuildTwoColorGrid();
        snap.ApplyTo(session, territories);

        Assert.Equal(
            new HashSet<HexCoord> { new HexCoord(1, 2), new HexCoord(3, 4) },
            session.VisitedThisTurnCapitals);
        Assert.True(session.SelectionWasRevisit);
        Assert.True(session.EndTurnCtaLatched);
    }

    [Fact]
    public void Equals_DetectsVisitedThisTurnAndRevisitAndLatchDifferences()
    {
        // TrackHandler's pushed-iff-changed de-dup must see a visited-
        // this-turn mark (or a revisit/latch flip) as a session change.
        var baseline = new SessionState();
        SessionStateSnapshot a = SessionStateSnapshot.Capture(baseline);

        var visitedDiffers = new SessionState();
        visitedDiffers.VisitedThisTurnCapitals.Add(new HexCoord(1, 2));
        Assert.NotEqual(a, SessionStateSnapshot.Capture(visitedDiffers));

        var revisitDiffers = new SessionState { SelectionWasRevisit = true };
        Assert.NotEqual(a, SessionStateSnapshot.Capture(revisitDiffers));

        var latchDiffers = new SessionState { EndTurnCtaLatched = true };
        Assert.NotEqual(a, SessionStateSnapshot.Capture(latchDiffers));
    }
}
