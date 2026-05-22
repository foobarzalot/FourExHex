using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="ReplayDrivenAi"/> — the AI chooser used by
/// Tutorial Preview to drive non-player-0 players' recorded moves
/// through the standard AI step machine.
/// </summary>
public class ReplayDrivenAiTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static IReadOnlyList<Player> TwoPlayerRoster() => new List<Player>
    {
        new("Red", Red, PlayerKind.Human),
        new("Blue", Blue, PlayerKind.Computer),
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
                                 Level = UnitLevel.Soldier },
        };
        var ai = new ReplayDrivenAi(beats, roster);
        var result = (AiBuyUnitAction)ai.ChooseNextAction(TrivialState(roster), Blue, new HashSet<HexCoord>(), new Random(1))!;
        Assert.Equal(new HexCoord(1, 1), result.Capital);
        Assert.Equal(new HexCoord(2, 2), result.Destination);
        Assert.Equal(UnitLevel.Soldier, result.Level);
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

    // --- Cursor-sync integration (regression for the live bug) -----------

    /// <summary>
    /// Regression: in Tutorial Preview, the human plays player 0 via
    /// <see cref="TutorialPreview"/> while non-player-0 actions come
    /// from <see cref="ReplayDrivenAi"/>. Both consume beats from the
    /// same totally-ordered script; their cursors MUST stay in sync.
    ///
    /// Symptom of de-sync (caught here): the dev plays Red's full
    /// turn (TutorialPreview consumes Red's beats), then the
    /// controller transitions to Blue. The AI step machine asks
    /// ReplayDrivenAi.ChooseNextAction for Blue. Without sync, AI's
    /// cursor still points at script[0] (Red's move) — actor 0, not
    /// the requested Blue (actor 1) — so it returns null. Controller
    /// reads "Blue is done; end turn." Every AI turn no-ops; only
    /// Red ever moves.
    ///
    /// Expected behavior: after TutorialPreview consumes Red's beats,
    /// ReplayDrivenAi must see the cursor advanced past them and
    /// deliver Blue's recorded move.
    /// </summary>
    [Fact]
    public void HumanConsumesRedBeats_AiThenSeesBlueBeats()
    {
        var roster = TwoPlayerRoster();
        // Script: Red move, Red EndTurn, Blue move, Blue EndTurn.
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 0,
                                  From = new HexCoord(0, 0), To = new HexCoord(1, 0) },
            new ReplayEndTurnBeat { Index = 1, Turn = 1, Actor = 0 },
            new ReplayMoveBeat { Index = 2, Turn = 1, Actor = 1,
                                  From = new HexCoord(3, 3), To = new HexCoord(2, 3) },
            new ReplayEndTurnBeat { Index = 3, Turn = 1, Actor = 1 },
        };

        var cursor = new ScriptCursor();
        var ai = new ReplayDrivenAi(script, roster, cursor);
        GameState state = TrivialState(roster);
        var preview = new TutorialPreview(script, state, cursor);

        // Red plays their move + ends turn — both consumed via TutorialPreview.
        Assert.True(preview.TryAccept(new ReplayMoveBeat
        {
            From = new HexCoord(0, 0), To = new HexCoord(1, 0),
        }));
        Assert.True(preview.TryAccept(new ReplayEndTurnBeat()));

        // Controller transitions to Blue's turn and asks the AI.
        // Bug: AI returns null because its cursor still points at
        // script[0] (Red's beat) — TutorialPreview's separate cursor
        // advance didn't reach the AI.
        AiAction? blueAction = ai.ChooseNextAction(
            state, Blue, new HashSet<HexCoord>(), new Random(1));

        Assert.IsType<AiMoveAction>(blueAction);
        var mv = (AiMoveAction)blueAction!;
        Assert.Equal(new HexCoord(3, 3), mv.Source);
        Assert.Equal(new HexCoord(2, 3), mv.Destination);
    }
}
