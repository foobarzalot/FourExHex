// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Coverage for the Next-Territory star-button CTA. The CTA fires
/// exactly when (1) the current player has at least one territory with
/// pending actions AND (2) there is either no selection OR the selected
/// territory is itself exhausted — and only on a human's turn. The
/// game-side trigger lives in <c>GameOperations.RefreshViews</c>; the
/// per-territory predicate is <c>TerritoryHasAvailableAction</c>.
/// </summary>
public class NextTerritoryCtaTests
{
    /// <summary>
    /// Build a single-row board with two non-adjacent Red territories
    /// separated by one Blue tile, so tests can drive "selected
    /// territory exhausted, the OTHER one still actionable". Layout:
    /// R R B R R B B — Red gets two size-2 territories, each with its
    /// own capital and 10g seeded by StartGame.
    /// </summary>
    private static (GameState state, SessionState session, MockHexMapView map, MockHudView hud,
                    GameController controller, Player red, Player blue,
                    Territory redA, Territory redB) BuildTwoRedTerritoriesFixture()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        var grid = TestHelpers.BuildRectGrid(7, 1, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = red.Id;

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        List<Territory> reds = state.Territories.Where(t => t.Owner == red.Id).ToList();
        Assert.Equal(2, reds.Count);
        // Sort by capital so test names "A" / "B" map deterministically.
        reds.Sort((a, b) => a.Capital!.Value.CompareTo(b.Capital!.Value));
        return (state, session, map, hud, controller, red, blue, reds[0], reds[1]);
    }

    [Fact]
    public void NoSelection_AndAnyTerritoryActionable_FiresCta()
    {
        // TestGame: Red owns 2 tiles, 10g seeded at capital, no units.
        // Fresh turn → SelectedTerritory == null → can afford recruit
        // somewhere → CTA on.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud,
            autoSelectFirstTerritory: false); // exercise the no-selection branch
        controller.StartGame();

        Assert.Null(session.SelectedTerritory);
        Assert.True(hud.LastHasActionableRemaining);
        Assert.True(hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void NoSelection_AndNothingActionable_DoesNotFireCta()
    {
        // Drain Red's only territory of gold. No units exist either, so
        // hasActionable is false. Star button is disabled in this case;
        // the CTA must also be off (no flashing-but-disabled button).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud,
            autoSelectFirstTerritory: false); // exercise the no-selection branch
        controller.StartGame();

        HexCoord redCapital = state.Territories.First(t => t.Owner == red.Id).Capital!.Value;
        state.Treasury.SetGold(redCapital, 0);
        controller.RefreshViewsForTutorial();

        Assert.Null(session.SelectedTerritory);
        Assert.False(hud.LastHasActionableRemaining);
        Assert.False(hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void SelectedTerritoryHasPendingActions_DoesNotFireCta()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();

        // Click Red's capital → SelectedTerritory = Red's territory. Red
        // can still afford a recruit there → selection is NOT exhausted.
        Territory redT = state.Territories.First(t => t.Owner == red.Id);
        map.SimulateClick(grid.Get(redT.Capital!.Value));
        Assert.Same(redT, session.SelectedTerritory);

        Assert.True(hud.LastHasActionableRemaining);
        Assert.False(hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void SelectedTerritoryExhausted_OtherActionable_FiresCta()
    {
        var f = BuildTwoRedTerritoriesFixture();

        // Drain Territory A's capital. Red has no units anywhere, so A
        // is now exhausted (no recruit affordable, no unmoved units in
        // it). Territory B keeps its 10g seed → still actionable.
        f.state.Treasury.SetGold(f.redA.Capital!.Value, 0);
        // Select Territory A by clicking its capital.
        f.map.SimulateClick(f.state.Grid.Get(f.redA.Capital!.Value));

        Assert.Same(f.redA, f.session.SelectedTerritory);
        Assert.True(f.hud.LastHasActionableRemaining); // B still actionable
        Assert.True(f.hud.NextTerritoryCtaActive);
    }

    [Fact]
    public void SelectedTerritoryExhausted_NoOtherActionable_DoesNotFireCta()
    {
        var f = BuildTwoRedTerritoriesFixture();

        // Drain BOTH Red capitals. No units anywhere. Red has nothing
        // actionable → CTA off (and EndTurn CTA on).
        f.state.Treasury.SetGold(f.redA.Capital!.Value, 0);
        f.state.Treasury.SetGold(f.redB.Capital!.Value, 0);
        f.map.SimulateClick(f.state.Grid.Get(f.redA.Capital!.Value));

        Assert.Same(f.redA, f.session.SelectedTerritory);
        Assert.False(f.hud.LastHasActionableRemaining);
        Assert.False(f.hud.NextTerritoryCtaActive);
        Assert.True(f.hud.EndTurnCtaActive); // existing behavior, sanity check
    }

    [Fact]
    public void CurrentPlayerComputer_SuppressesCta_EvenWhenConditionsHold()
    {
        // Red is a Computer player with an actionable territory and no
        // selection — i.e., the human-only gate is the ONLY thing
        // keeping the CTA off. Use a QueuedAiPacer + no-op chooser so
        // the AI loop never runs and we observe the first RefreshViews
        // with Red still the current player.
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;

        // Seed Red's capital manually (StartGame would also do this, but
        // skipping StartGame keeps the AI loop dormant).
        HexCoord redCapital = territories.First(t => t.Owner == red.Id).Capital!.Value;
        state.Treasury.SetGold(redCapital, 10);

        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(
            state, session, map, hud,
            aiChooser: (_, _, _, _, _) => null,
            aiPacer: new QueuedAiPacer());

        controller.RefreshViewsForTutorial();

        Assert.True(state.Turns.CurrentPlayer.IsAi);
        Assert.Null(session.SelectedTerritory);
        Assert.True(hud.LastHasActionableRemaining); // condition (1) holds
        Assert.False(hud.NextTerritoryCtaActive);    // human gate suppresses
    }
}
