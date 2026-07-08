using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Coverage for the turn-scoped visited-territory state (#126):
/// selecting a territory marks it visited for the whole turn
/// (<see cref="SessionState.VisitedThisTurnCapitals"/>), which
/// (a) suppresses its capital's pending-action highlight (the visited
///     set is threaded to <c>IHexMapView.RefreshOccupantVisuals</c>),
/// (b) lights the Next-Territory CTA when a previously-visited
///     territory is re-selected (a revisit), and
/// (c) lights the End Turn CTA once every actionable territory has
///     been visited — taking priority over the Next-Territory CTA.
/// Undo unwinds the visited state; the Tab-cycle's own round tracker
/// (<see cref="SessionState.VisitedTerritoryCapitals"/>) keeps its
/// mid-turn reset behavior untouched.
/// </summary>
public class VisitedTerritoryCtaTests
{
    /// <summary>
    /// Four 2-tile Red territories on a 13x1 row, separated by Blue
    /// tiles: R R B R R B R R B R R B B. Each gets its own capital and
    /// a 10g seed from StartGame, so all four are actionable (recruit
    /// affordable) and stay actionable throughout a test that only
    /// selects. autoSelect is off so tests control every selection.
    /// </summary>
    private static ControllerHarness BuildFourRedTerritories()
    {
        var red = PlayerId.FromIndex(0);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            cols: 13, rows: 1,
            ownerOverrides: new[]
            {
                (0, 0, red), (1, 0, red),
                (3, 0, red), (4, 0, red),
                (6, 0, red), (7, 0, red),
                (9, 0, red), (10, 0, red),
            });
        List<Territory> reds = h.State.Territories
            .Where(t => t.Owner == red).ToList();
        Assert.Equal(4, reds.Count);
        return h;
    }

    /// <summary>Red's territories sorted by capital coord → stable A/B/C/D naming.</summary>
    private static List<Territory> RedsSorted(ControllerHarness h)
    {
        List<Territory> reds = h.State.Territories
            .Where(t => t.Owner == h.Players[0].Id).ToList();
        reds.Sort((a, b) => a.Capital!.Value.CompareTo(b.Capital!.Value));
        return reds;
    }

    private static void ClickCapital(ControllerHarness h, Territory t) =>
        h.Map.SimulateClick(h.State.Grid.Get(t.Capital!.Value));

    [Fact]
    public void Selection_MarksVisitedThisTurn_ButKeepsWorkedCapitalUnsuppressed()
    {
        ControllerHarness h = BuildFourRedTerritories();
        Territory a = RedsSorted(h)[0];

        ClickCapital(h, a);

        HexCoord cap = a.Capital!.Value;
        Assert.Contains(cap, h.Session.VisitedThisTurnCapitals);
        Assert.False(h.Session.SelectionWasRevisit);
        // A newly-visited territory being actively worked keeps its
        // capital highlight — the suppression set handed to the view
        // excludes the selected first-visit capital (its pulse dies on
        // its own once the territory can't afford anything).
        Assert.DoesNotContain(cap, h.Map.LastVisitedCapitals);
    }

    [Fact]
    public void MovingToAnotherTerritory_SuppressesThePriorVisitedCapital()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        ClickCapital(h, reds[0]);
        ClickCapital(h, reds[1]);

        // A was left (visited, no longer worked) → suppressed. B is the
        // newly-visited worked territory → still unsuppressed.
        Assert.Contains(reds[0].Capital!.Value, h.Map.LastVisitedCapitals);
        Assert.DoesNotContain(reds[1].Capital!.Value, h.Map.LastVisitedCapitals);

        h.Map.SimulateClick(null); // deselect B → suppressed too

        Assert.Contains(reds[1].Capital!.Value, h.Map.LastVisitedCapitals);
    }

    [Fact]
    public void RevisitedSelection_StaysSuppressed()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        ClickCapital(h, reds[0]);
        ClickCapital(h, reds[1]);
        ClickCapital(h, reds[0]); // revisit — already toured, no re-light

        Assert.Contains(reds[0].Capital!.Value, h.Map.LastVisitedCapitals);
        Assert.Contains(reds[1].Capital!.Value, h.Map.LastVisitedCapitals);
    }

    [Fact]
    public void FirstSelection_WithActionsRemaining_DoesNotFireNextTerritoryCta()
    {
        ControllerHarness h = BuildFourRedTerritories();
        Territory a = RedsSorted(h)[0];

        ClickCapital(h, a);

        Assert.True(h.Hud.LastHasActionableRemaining);
        Assert.False(h.Hud.NextTerritoryCtaActive);
        Assert.False(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void ReclickInsideSelectedTerritory_IsNotARevisit()
    {
        ControllerHarness h = BuildFourRedTerritories();
        Territory a = RedsSorted(h)[0];

        ClickCapital(h, a);
        // Click the territory's OTHER tile — same territory, new tile.
        HexCoord otherTile = a.Coords.First(c => c != a.Capital!.Value);
        h.Map.SimulateClick(h.State.Grid.Get(otherTile));

        Assert.Same(h.Session.SelectedTerritory, a);
        Assert.False(h.Session.SelectionWasRevisit);
        Assert.False(h.Hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void Revisit_FiresNextTerritoryCta()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        ClickCapital(h, reds[0]);
        ClickCapital(h, reds[1]);
        ClickCapital(h, reds[0]); // revisit A

        Assert.True(h.Session.SelectionWasRevisit);
        Assert.True(h.Hud.NextTerritoryCtaActive);
        // C and D are still unvisited → End Turn CTA stays off.
        Assert.False(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void SelectingUnvisited_AfterRevisit_TurnsNextTerritoryCtaOff()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        ClickCapital(h, reds[0]);
        ClickCapital(h, reds[1]);
        ClickCapital(h, reds[0]); // revisit → CTA on
        Assert.True(h.Hud.NextTerritoryCtaActive);

        ClickCapital(h, reds[2]); // fresh territory (D still unvisited)

        Assert.False(h.Session.SelectionWasRevisit);
        Assert.False(h.Hud.NextTerritoryCtaActive);
        Assert.False(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void AllVisited_EndTurnCtaWaitsForLastSelectionToDeselect()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        foreach (Territory t in reds) ClickCapital(h, t);

        // All four are visited, but the last one is still selected and
        // still has pending actions (its 10g) — the player is presumed
        // to be working on it, so End Turn holds off.
        Assert.True(h.Hud.LastHasActionableRemaining);
        Assert.False(h.Hud.EndTurnCtaActive);

        h.Map.SimulateClick(null); // deselect

        Assert.Null(h.Session.SelectedTerritory);
        Assert.True(h.Hud.EndTurnCtaActive);
        // The null selection also satisfies the star's "exhausted"
        // trigger — End Turn wins, star suppressed.
        Assert.False(h.Hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void AllVisited_EndTurnCtaLightsWhenLastSelectedTerritoryExhausts()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        foreach (Territory t in reds) ClickCapital(h, t);
        Assert.False(h.Hud.EndTurnCtaActive);

        // Exhaust the still-selected last territory (drain its gold; it
        // has no units) — "finished acting on the last territory".
        Territory last = h.Session.SelectedTerritory!;
        h.State.Treasury.SetGold(last.Capital!.Value, 0);
        h.Controller.RefreshViewsForTutorial();

        Assert.True(h.Hud.EndTurnCtaActive);
        Assert.False(h.Hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void EndTurnCta_OnceLit_StaysLitThroughRevisitsAndTab()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        foreach (Territory t in reds) ClickCapital(h, t);
        h.Map.SimulateClick(null); // deselect → lit
        Assert.True(h.Hud.EndTurnCtaActive);

        // Re-selecting an actionable visited territory does NOT un-light
        // End Turn, and the star stays unlit while End Turn is lit —
        // though visiting still works.
        ClickCapital(h, reds[0]);
        Assert.Same(reds[0], h.Session.SelectedTerritory);
        Assert.True(h.Hud.EndTurnCtaActive);
        Assert.False(h.Hud.NextTerritoryCtaActive);

        // Tab still cycles too, without disturbing the lit state.
        h.Hud.PressNextTerritory();
        Assert.NotNull(h.Session.SelectedTerritory);
        Assert.True(h.Hud.EndTurnCtaActive);
        Assert.False(h.Hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void EndTurnCta_UndoKeepsItLit_UntilTheLightingStepUnwinds()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        foreach (Territory t in reds) ClickCapital(h, t);
        h.Map.SimulateClick(null); // deselect → lit
        ClickCapital(h, reds[0]);  // revisit after lit
        Assert.True(h.Hud.EndTurnCtaActive);

        // Undo the post-lit revisit: still lit (the visit set is intact).
        h.Hud.ClickUndoLast();
        Assert.Null(h.Session.SelectedTerritory);
        Assert.True(h.Hud.EndTurnCtaActive);

        // Undo the deselect that lit it: back to "working the last
        // territory" → dark again.
        h.Hud.ClickUndoLast();
        Assert.NotNull(h.Session.SelectedTerritory);
        Assert.False(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void RevisitWhileAllVisited_ShowsNextTerritoryCta_NotEndTurn()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        foreach (Territory t in reds) ClickCapital(h, t);
        ClickCapital(h, reds[0]); // revisit; it still has actions

        Assert.True(h.Session.SelectionWasRevisit);
        Assert.False(h.Hud.EndTurnCtaActive);
        Assert.True(h.Hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void TabCycleNewRound_DoesNotClearVisitedThisTurn()
    {
        ControllerHarness h = BuildFourRedTerritories();

        // Four presses tour every territory; the fifth wraps into a new
        // Tab round, which resets the CYCLE set — the turn-scoped
        // visited set must survive that reset.
        for (int i = 0; i < 5; i++) h.Hud.PressNextTerritory();

        Assert.True(h.Session.VisitedTerritoryCapitals.Count < 4); // cycle set was reset
        Assert.Equal(4, h.Session.VisitedThisTurnCapitals.Count);
        // The wrap landed on a revisited, still-actionable territory:
        // star on, End Turn waiting for exhaust-or-deselect.
        Assert.True(h.Hud.NextTerritoryCtaActive);
        Assert.False(h.Hud.EndTurnCtaActive);

        h.Map.SimulateClick(null); // deselect → all visited, nothing selected

        Assert.True(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void Undo_UnwindsVisitedSetStepByStep()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        ClickCapital(h, reds[0]);
        ClickCapital(h, reds[1]);
        Assert.Equal(2, h.Session.VisitedThisTurnCapitals.Count);

        h.Hud.ClickUndoLast();

        Assert.Equal(
            new[] { reds[0].Capital!.Value },
            h.Session.VisitedThisTurnCapitals.OrderBy(c => c).ToArray());
        Assert.Same(reds[0], h.Session.SelectedTerritory);
    }

    [Fact]
    public void Undo_TurnsEndTurnCtaBackOff()
    {
        ControllerHarness h = BuildFourRedTerritories();
        List<Territory> reds = RedsSorted(h);

        foreach (Territory t in reds) ClickCapital(h, t);
        h.Map.SimulateClick(null); // deselect → CTA on
        Assert.True(h.Hud.EndTurnCtaActive);

        h.Hud.ClickUndoLast(); // last territory selected again (actionable)

        Assert.Equal(4, h.Session.VisitedThisTurnCapitals.Count);
        Assert.NotNull(h.Session.SelectedTerritory);
        Assert.False(h.Hud.EndTurnCtaActive);

        h.Hud.ClickUndoLast(); // unwind the last visit itself

        Assert.Equal(3, h.Session.VisitedThisTurnCapitals.Count);
        Assert.False(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void AutomateExhausted_MarksAllActionableVisited_AndFiresEndTurnCta()
    {
        // Two Red territories; the automate chooser declines to act at
        // all (returns null immediately), so neither is visited by an
        // acting-territory selection. Running automation to completion
        // must still light End Turn — exhaustion marks every actionable
        // territory visited, including ones automation never acted on.
        var red = PlayerId.FromIndex(0);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            ownerOverrides: new[]
            {
                (0, 1, red), (1, 1, red),
                (3, 1, red), (4, 1, red),
            },
            automateChooser: (s, c, visited, rng) => null);

        h.Hud.ClickAutomate();

        Assert.False(h.Controller.IsAutomating);
        Assert.True(h.Hud.LastHasActionableRemaining);
        Assert.Equal(2, h.Session.VisitedThisTurnCapitals.Count);
        Assert.True(h.Hud.EndTurnCtaActive);
    }

    [Fact]
    public void AiTurn_DoesNotLightEndTurnCta_EvenWithNothingActionable()
    {
        // AI current player with no gold and no units: the human rule
        // (!hasActionable) would light End Turn — AI turns keep every
        // CTA dark (all HUD affordances are human-only).
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        // Skip StartGame (no gold seeded, AI loop dormant) and refresh.
        var controller = new GameController(
            state, session, map, hud,
            aiChooser: (_, _, _, _) => null,
            aiPacer: new QueuedAiPacer());

        controller.RefreshViewsForTutorial();

        Assert.True(state.Turns.CurrentPlayer.IsAi);
        Assert.False(hud.LastHasActionableRemaining);
        Assert.False(hud.EndTurnCtaActive);
        Assert.False(hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void AutomateExhausted_WithActionableTerritorySelected_StillFiresEndTurnCta()
    {
        // The chooser plays one buy then declines; automation leaves the
        // acting territory selected with gold remaining (actionable).
        // The manual rule would hold End Turn off, but a completed
        // automation run always lights it (exhausted latch). Undo clears
        // the latch and the manual rule takes over again.
        var red = PlayerId.FromIndex(0);
        int played = 0;
        ControllerHarness h = TestHelpers.BuildControllerGame(
            ownerOverrides: new[] { (0, 1, red), (1, 1, red) },
            automateChooser: (s, c, visited, rng) => played++ == 0
                ? new AiBuyUnitAction(
                    HexCoord.FromOffset(0, 1), HexCoord.FromOffset(1, 1), UnitLevel.Recruit)
                : null);
        HexCoord redCap = h.State.Territories
            .First(t => t.Owner == red).Capital!.Value;
        h.State.Treasury.SetGold(redCap, 100);

        h.Hud.ClickAutomate();

        Assert.False(h.Controller.IsAutomating);
        Assert.NotNull(h.Session.SelectedTerritory); // acting territory kept selected
        Assert.True(h.Hud.LastHasActionableRemaining); // 90g left
        Assert.True(h.Hud.EndTurnCtaActive);

        h.Hud.ClickUndoLast(); // undo clears the exhausted latch

        Assert.False(h.Hud.EndTurnCtaActive);
    }
}
