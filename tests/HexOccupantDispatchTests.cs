using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins loud-failure behavior for every site that switches on the
/// <see cref="HexOccupant"/> subtype hierarchy. Adding a new
/// <c>HexOccupant</c> subclass without updating all dispatch sites
/// must throw <see cref="InvalidOperationException"/>, never silently
/// produce a default value or an "Unknown:" string. These tests cover
/// the two dispatch sites that must throw instead of returning a silent
/// fallback. Three of the five
/// dispatch sites — <see cref="HexOccupant.Clone"/>,
/// <c>SaveSerializer.SerializeOccupant</c>, and
/// <c>SaveSerializer.DeserializeOccupant</c> — already throw via their
/// existing default arms; these tests cover the two that historically
/// returned a silent fallback.
/// </summary>
public class HexOccupantDispatchTests
{
    private sealed class UnknownHexOccupant : HexOccupant { }

    [Fact]
    public void DefenseRules_ContributionOf_ThrowsOnUnknownSubtype()
    {
        Assert.Throws<InvalidOperationException>(
            () => DefenseRules.ContributionOf(new UnknownHexOccupant()));
    }

    [Fact]
    public void GameStateChecksum_Compute_ThrowsOnUnknownSubtype()
    {
        PlayerId owner = PlayerId.FromIndex(0);
        HexGrid grid = TestHelpers.BuildRectGrid(2, 1, owner);
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new UnknownHexOccupant();
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player> { new Player("P0", owner) };

        var state = new GameState(
            grid,
            territories,
            players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 1),
            new Treasury());

        Assert.Throws<InvalidOperationException>(
            () => GameStateChecksum.Compute(state));
    }
}
