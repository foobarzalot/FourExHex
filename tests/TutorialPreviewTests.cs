using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="TutorialPreview"/> — the player-0 input
/// validator used by Tutorial Preview. TryAccept matches an attempted
/// action against the next player-0-owned script beat; on match it
/// advances; on mismatch it fires PlayerActionRejected and the
/// controller aborts the action.
/// </summary>
public class TutorialPreviewTests
{
    private static readonly Color Red = new(1f, 0f, 0f);
    private static readonly Color Blue = new(0f, 0f, 1f);

    private static GameState PlayerZeroTurnState()
    {
        var players = new List<Player>
        {
            new("Red", Red, AiKind.Human),
            new("Blue", Blue, AiKind.Heuristic),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    private static GameState PlayerOneTurnState()
    {
        var players = new List<Player>
        {
            new("Red", Red, AiKind.Human),
            new("Blue", Blue, AiKind.Heuristic),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(grid, territories, players,
            new TurnState(players, currentPlayerIndex: 1, turnNumber: 1), new Treasury());
    }

    [Fact]
    public void TryAccept_MatchingMoveBeat_AdvancesAndReturnsTrue()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 0,
                                  From = new HexCoord(0, 0), To = new HexCoord(1, 0) },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        bool ok = preview.TryAccept(new ReplayMoveBeat
        {
            From = new HexCoord(0, 0), To = new HexCoord(1, 0),
        });

        Assert.True(ok);
    }

    [Fact]
    public void TryAccept_MismatchingMoveDestination_RejectsAndReturnsFalse()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 0,
                                  From = new HexCoord(0, 0), To = new HexCoord(1, 0) },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());
        ReplayBeat? rejectedExpected = null;
        string? rejectedReason = null;
        preview.PlayerActionRejected += (expected, reason) =>
        {
            rejectedExpected = expected;
            rejectedReason = reason;
        };

        bool ok = preview.TryAccept(new ReplayMoveBeat
        {
            From = new HexCoord(0, 0), To = new HexCoord(2, 0),
        });

        Assert.False(ok);
        Assert.NotNull(rejectedExpected);
        Assert.NotNull(rejectedReason);
    }

    [Fact]
    public void TryAccept_WhenCurrentPlayerIsNotZero_RejectsEvenIfBeatMatches()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 0,
                                  From = new HexCoord(0, 0), To = new HexCoord(1, 0) },
        };
        var preview = new TutorialPreview(script, PlayerOneTurnState());

        bool ok = preview.TryAccept(new ReplayMoveBeat
        {
            From = new HexCoord(0, 0), To = new HexCoord(1, 0),
        });

        Assert.False(ok);
    }

    [Fact]
    public void TryAccept_SkipsOverNonPlayer0Beats_ToFindNextPlayer0Beat()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 1,
                                  From = new HexCoord(2, 2), To = new HexCoord(2, 1) },
            new ReplayEndTurnBeat { Index = 1, Turn = 1, Actor = 1 },
            new ReplayBuyBeat { Index = 2, Turn = 2, Actor = 0,
                                 Capital = new HexCoord(0, 0), To = new HexCoord(1, 0),
                                 Level = UnitLevel.Peasant },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        bool ok = preview.TryAccept(new ReplayBuyBeat
        {
            Capital = new HexCoord(0, 0), To = new HexCoord(1, 0),
            Level = UnitLevel.Peasant,
        });

        Assert.True(ok);
    }

    [Fact]
    public void TryAccept_FinalPlayer0Beat_FiresTutorialFinished()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());
        int finishedCount = 0;
        preview.TutorialFinished += () => finishedCount++;

        bool ok = preview.TryAccept(new ReplayEndTurnBeat());
        Assert.True(ok);
        Assert.Equal(1, finishedCount);
    }

    [Fact]
    public void TryAccept_BuyBeat_MatchesOnCapitalToAndLevel()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayBuyBeat { Index = 0, Turn = 1, Actor = 0,
                                 Capital = new HexCoord(0, 0), To = new HexCoord(1, 1),
                                 Level = UnitLevel.Spearman },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        Assert.False(preview.TryAccept(new ReplayBuyBeat
        {
            Capital = new HexCoord(0, 0), To = new HexCoord(1, 1),
            Level = UnitLevel.Peasant,  // wrong level
        }));
    }

    [Fact]
    public void TryAccept_BuildTowerBeat_MatchesOnCapitalAndTo()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayBuildTowerBeat { Index = 0, Turn = 1, Actor = 0,
                                        Capital = new HexCoord(0, 0), To = new HexCoord(1, 1) },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        Assert.True(preview.TryAccept(new ReplayBuildTowerBeat
        {
            Capital = new HexCoord(0, 0), To = new HexCoord(1, 1),
        }));
    }

    [Fact]
    public void TryAccept_RallyBeat_MatchesOnTarget()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayLongPressRallyBeat { Index = 0, Turn = 1, Actor = 0,
                                            Target = new HexCoord(2, 2) },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        Assert.False(preview.TryAccept(new ReplayLongPressRallyBeat
        {
            Target = new HexCoord(3, 3),  // wrong target
        }));
        Assert.True(preview.TryAccept(new ReplayLongPressRallyBeat
        {
            Target = new HexCoord(2, 2),
        }));
    }

    [Fact]
    public void TryAccept_EndTurnWhenScriptExpectsBuy_Rejects()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayBuyBeat { Index = 0, Turn = 1, Actor = 0,
                                 Capital = new HexCoord(0, 0), To = new HexCoord(1, 0),
                                 Level = UnitLevel.Peasant },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        Assert.False(preview.TryAccept(new ReplayEndTurnBeat()));
    }
}
