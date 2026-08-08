// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Which player's difficulty sets the price of a purchase: the one who
/// OWNS the territory paying for it, never whoever's turn it happens to
/// be. That's the rule <see cref="AiActionCore"/>, <c>AiCommon</c> and
/// <c>AiActionLowering</c> already implement via
/// <see cref="GameState.DifficultyOf"/>; these tests pin the human
/// controller path to the same rule.
///
/// The two agree on every path reachable in ordinary play (the click
/// handler refuses to select a territory the current player doesn't own),
/// so the divergence is only observable two ways, both exercised here:
/// a purchase from a foreign territory via the owner-unchecked selection
/// seam, and a gate-vs-charge mismatch where the affordability check and
/// the deduction disagree about the price.
/// </summary>
public class PurchaseDifficultySourceTests
{
    // Captain pays 11 per unit tier, Soldier 10, tower 16 vs 15
    // (DifficultyRules). The gaps are exactly one gold, so the seeded
    // treasuries below sit precisely between the two prices — a
    // wrong-difficulty read flips the affordability gate outright.
    private static ControllerHarness BuildMixedDifficultyGame(int blueGold)
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human, Difficulty.Captain);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human, Difficulty.Soldier);
        return TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            // Blue isn't the current player, so it never collected income;
            // seed its capital directly.
            beforeStart: state =>
            {
                Territory blueTerritory = state.Territories.First(t => t.Owner == blue.Id);
                state.Treasury.SetGold(blueTerritory.Capital!.Value, blueGold);
            });
    }

    private static Territory BlueTerritory(ControllerHarness h) =>
        h.State.Territories.First(t => t.Owner == h.Players[1].Id);

    /// <summary>An empty, non-capital tile inside the territory — a legal
    /// placement target for any unit level.</summary>
    private static HexTile EmptyTileIn(ControllerHarness h, Territory territory) =>
        territory.Coords
            .Select(c => h.State.Grid.Get(c)!)
            .First(t => t.Occupant == null);

    [Fact]
    public void Buy_FromForeignTerritory_ChargesTheOwnersDifficultyNotTheCurrentPlayers()
    {
        ControllerHarness h = BuildMixedDifficultyGame(blueGold: 100);
        Territory blue = BlueTerritory(h);
        HexCoord blueCapital = blue.Capital!.Value;

        // The one selection seam with no owner check — the same one
        // TutorialPreviewCues.ApplyBuyCue drives.
        h.Controller.SelectTerritoryForTutorial(blue);
        int goldBefore = h.State.Treasury.GetGold(blueCapital);

        h.Hud.ClickBuyUnit(UnitLevel.Recruit);
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, h.Session.Mode);
        h.Map.SimulateClick(EmptyTileIn(h, blue));

        int spent = goldBefore - h.State.Treasury.GetGold(blueCapital);
        Assert.Equal(PurchaseRules.CostFor(UnitLevel.Recruit, Difficulty.Soldier), spent);
    }

    [Fact]
    public void BuildTower_GoldBetweenTheTwoPrices_IsOfferedAndChargedAtTheOwnersPrice()
    {
        // 15 gold: affordable at the owner's Soldier price (15), not at the
        // current player's Captain price (16). The charge is already
        // owner-derived (AiActionCore.BuildTower), so an affordability gate
        // reading the current player refuses a tower the territory can pay for.
        ControllerHarness h = BuildMixedDifficultyGame(blueGold: 0);
        Territory blue = BlueTerritory(h);
        HexCoord blueCapital = blue.Capital!.Value;

        h.Controller.SelectTerritoryForTutorial(blue);
        // Set after StartGame's first upkeep so the gate sees exactly 15.
        h.State.Treasury.SetGold(blueCapital, 15);
        int goldBefore = h.State.Treasury.GetGold(blueCapital);
        Assert.Equal(15, goldBefore);

        h.Hud.ClickBuildTower();
        Assert.Equal(SessionState.ActionMode.BuildingTower, h.Session.Mode);
        HexTile site = EmptyTileIn(h, blue);
        h.Map.SimulateClick(site);

        Assert.IsType<Tower>(site.Occupant);
        int spent = goldBefore - h.State.Treasury.GetGold(blueCapital);
        Assert.Equal(PurchaseRules.TowerCostFor(Difficulty.Soldier), spent);
    }

    [Fact]
    public void Buy_OnOwnTerritory_IsUnaffectedByTheRule()
    {
        // Guard: the ordinary path (current player owns the territory) must
        // keep charging that player's own price — this is the case the two
        // expressions agree on, and it must stay green through the change.
        ControllerHarness h = BuildMixedDifficultyGame(blueGold: 0);
        Territory red = h.State.Territories.First(t => t.Owner == h.Players[0].Id);
        HexCoord redCapital = red.Capital!.Value;
        h.State.Treasury.SetGold(redCapital, 100);

        h.Map.SimulateClick(h.State.Grid.Get(redCapital)!);
        int goldBefore = h.State.Treasury.GetGold(redCapital);

        h.Hud.ClickBuyUnit(UnitLevel.Recruit);
        h.Map.SimulateClick(EmptyTileIn(h, red));

        int spent = goldBefore - h.State.Treasury.GetGold(redCapital);
        Assert.Equal(PurchaseRules.CostFor(UnitLevel.Recruit, Difficulty.Captain), spent);
    }
}
