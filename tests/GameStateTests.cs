// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class GameStateTests
{
    [Fact]
    public void DifficultyOf_SparseRoster_ResolvesBySlot()
    {
        // Compact roster with a gap: slots 0, 2, 5 present (1,3,4 are None).
        // The slot-5 player sits at position 2 in the 3-element roster, so
        // the lookup must match by PlayerId, not index the roster at slot 5.
        // Purchase costs (AiCommon buy gates, AiActionCore) ride on this.
        PlayerId orange = PlayerId.FromIndex(5);
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, orange);
        IReadOnlyList<Territory> terr = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human, Difficulty.Captain),
            new Player("Green", PlayerId.FromIndex(2), PlayerKind.Computer),
            new Player("Orange", orange, PlayerKind.Computer, Difficulty.Commander),
        };
        var state = new GameState(grid, terr, players, new TurnState(players), new Treasury());

        Assert.Equal(Difficulty.Commander, state.DifficultyOf(orange));
        Assert.Equal(Difficulty.Captain, state.DifficultyOf(PlayerId.FromIndex(0)));
        // Neutral land and ids not in the roster fall back to Soldier.
        Assert.Equal(Difficulty.Soldier, state.DifficultyOf(PlayerId.None));
        Assert.Equal(Difficulty.Soldier, state.DifficultyOf(PlayerId.FromIndex(3)));
    }
}
