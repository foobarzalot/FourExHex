using System;
using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TutorialAiTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

    private static GameState BuildState()
    {
        var grid = TestHelpers.BuildRectGrid(3, 3, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var players = new List<Player>
        {
            new("Red", Red, AiKind.Tutorial),
            new("Blue", Blue, AiKind.Heuristic),
        };
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    [Fact]
    public void ChooseNextAction_AlwaysReturnsNull()
    {
        // TutorialAi is fully passive — must return null so the step
        // machine ends the player's turn immediately.
        GameState state = BuildState();

        AiAction? action = TutorialAi.ChooseNextAction(
            state, Red, new HashSet<HexCoord>(), new Random(42));

        Assert.Null(action);
    }
}
