using System;
using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="ReplayDrivenAi"/> — the AI chooser used by
/// Tutorial Preview to drive non-player-0 players' recorded moves
/// through the standard AI step machine.
/// </summary>
public class ReplayDrivenAiTests
{
    private static readonly Color Red = new(1f, 0f, 0f);
    private static readonly Color Blue = new(0f, 0f, 1f);

    private static IReadOnlyList<Player> TwoPlayerRoster() => new List<Player>
    {
        new("Red", Red, AiKind.Human),
        new("Blue", Blue, AiKind.Heuristic),
    };

    private static GameState TrivialState(IReadOnlyList<Player> players)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    [Fact]
    public void Choose_ForCurrentActor_ReturnsAiActionAndAdvancesCursor()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 1,
                                  From = new HexCoord(0, 0), To = new HexCoord(1, 0) },
            new ReplayEndTurnBeat { Index = 1, Turn = 1, Actor = 1 },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var state = TrivialState(roster);

        AiAction? first = ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1));
        Assert.IsType<AiMoveAction>(first);
        var mv = (AiMoveAction)first!;
        Assert.Equal(new HexCoord(0, 0), mv.Source);
        Assert.Equal(new HexCoord(1, 0), mv.Destination);

        // Second call: next beat is EndTurn for Blue → return null,
        // advance cursor. The AI step machine reads null as "this
        // player is done."
        AiAction? second = ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1));
        Assert.Null(second);
    }

    [Fact]
    public void Choose_ForOtherActor_ReturnsNullWithoutAdvancingCursor()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 1,
                                  From = new HexCoord(0, 0), To = new HexCoord(1, 0) },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var state = TrivialState(roster);

        // Ask for Red's action — next beat is Blue's. Should return
        // null and NOT consume the Blue beat.
        AiAction? red = ai.ChooseNextAction(state, Red, new HashSet<HexCoord>(), new Random(1));
        Assert.Null(red);

        // Now ask for Blue — the beat should still be there.
        AiAction? blue = ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1));
        Assert.IsType<AiMoveAction>(blue);
    }

    [Fact]
    public void Choose_EndOfScript_ReturnsNull()
    {
        var roster = TwoPlayerRoster();
        var ai = new ReplayDrivenAi(new List<ReplayBeat>(), roster);
        var state = TrivialState(roster);

        Assert.Null(ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1)));
    }

    [Fact]
    public void Reset_RewindsCursor()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 1,
                                  From = new HexCoord(0, 0), To = new HexCoord(1, 0) },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var state = TrivialState(roster);

        Assert.IsType<AiMoveAction>(ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1)));
        Assert.Null(ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1)));

        ai.Reset();
        Assert.IsType<AiMoveAction>(ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1)));
    }

    [Fact]
    public void Choose_MovesBeat_MapsToAiMoveAction()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 1,
                                  From = new HexCoord(2, 3), To = new HexCoord(4, 5) },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var result = (AiMoveAction)ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1))!;
        Assert.Equal(new HexCoord(2, 3), result.Source);
        Assert.Equal(new HexCoord(4, 5), result.Destination);
    }

    [Fact]
    public void Choose_BuyBeat_MapsToAiBuyUnitAction()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayBuyBeat { Index = 0, Turn = 1, Actor = 1,
                                 Capital = new HexCoord(1, 1), To = new HexCoord(2, 2),
                                 Level = UnitLevel.Spearman },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var result = (AiBuyUnitAction)ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1))!;
        Assert.Equal(new HexCoord(1, 1), result.Capital);
        Assert.Equal(new HexCoord(2, 2), result.Destination);
        Assert.Equal(UnitLevel.Spearman, result.Level);
    }

    [Fact]
    public void Choose_BuildTowerBeat_MapsToAiBuildTowerAction()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayBuildTowerBeat { Index = 0, Turn = 1, Actor = 1,
                                        Capital = new HexCoord(1, 1), To = new HexCoord(3, 3) },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var result = (AiBuildTowerAction)ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1))!;
        Assert.Equal(new HexCoord(1, 1), result.Capital);
        Assert.Equal(new HexCoord(3, 3), result.Destination);
    }

    [Fact]
    public void Choose_LongPressRallyBeat_MapsToAiLongPressRallyAction()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayLongPressRallyBeat { Index = 0, Turn = 1, Actor = 1,
                                            Target = new HexCoord(4, 4) },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var result = (AiLongPressRallyAction)ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1))!;
        Assert.Equal(new HexCoord(4, 4), result.Target);
    }

    [Fact]
    public void Choose_ClaimVictoryBeat_MapsToAiClaimVictoryAction()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayClaimVictoryBeat { Index = 0, Turn = 1, Actor = 1, ThresholdPercent = 75 },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var result = (AiClaimVictoryAction)ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1))!;
        Assert.Equal(75, result.ThresholdPercent);
    }

    [Fact]
    public void Choose_DismissClaimBeat_MapsToAiDismissClaimAction()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayDismissClaimBeat { Index = 0, Turn = 1, Actor = 1, ThresholdPercent = 50 },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var result = (AiDismissClaimAction)ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1))!;
        Assert.Equal(50, result.ThresholdPercent);
    }

    [Fact]
    public void Choose_DismissDefeatBeat_MapsToAiDismissDefeatAction()
    {
        var roster = TwoPlayerRoster();
        var beats = new List<ReplayBeat>
        {
            new ReplayDismissDefeatBeat { Index = 0, Turn = 1, Actor = 1 },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        AiAction? result = ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1));
        Assert.IsType<AiDismissDefeatAction>(result);
    }
}
