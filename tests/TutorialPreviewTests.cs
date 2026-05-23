using System.Collections.Generic;
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
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static GameState PlayerZeroTurnState()
    {
        var players = new List<Player>
        {
            new("Red", Red, PlayerKind.Human),
            new("Blue", Blue, PlayerKind.Computer),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    private static GameState PlayerOneTurnState()
    {
        var players = new List<Player>
        {
            new("Red", Red, PlayerKind.Human),
            new("Blue", Blue, PlayerKind.Computer),
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
    public void TryAccept_OnPlayer0TurnButCursorOnNonPlayer0Beat_RejectsAsDesync()
    {
        // Strict shared-cursor contract: when control is on player 0,
        // the cursor MUST point at a player-0 beat (the AI side
        // consumes its own beats before transitioning back). If the
        // cursor still points at a non-player-0 beat when player 0
        // attempts an action, that's a sync bug — reject with a
        // clear "desync" reason rather than silently scanning forward
        // (which would mask the bug).
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 1,
                                  From = new HexCoord(2, 2), To = new HexCoord(2, 1) },
            new ReplayBuyBeat { Index = 1, Turn = 1, Actor = 0,
                                 Capital = new HexCoord(0, 0), To = new HexCoord(1, 0),
                                 Level = UnitLevel.Recruit },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());
        ReplayBeat? rejectedExpected = null;
        preview.PlayerActionRejected += (e, _) => rejectedExpected = e;

        bool ok = preview.TryAccept(new ReplayBuyBeat
        {
            Capital = new HexCoord(0, 0), To = new HexCoord(1, 0),
            Level = UnitLevel.Recruit,
        });

        Assert.False(ok);
        Assert.NotNull(rejectedExpected);
    }

    [Fact]
    public void TryAccept_WithSharedCursorAdvancedPastBlueBeats_AcceptsRedAction()
    {
        // Realistic flow: AI consumed Blue's beats and the cursor is
        // now positioned at Red's beat. TutorialPreview should accept
        // when the dev plays the expected Red action.
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat { Index = 0, Turn = 1, Actor = 1,
                                  From = new HexCoord(2, 2), To = new HexCoord(2, 1) },
            new ReplayEndTurnBeat { Index = 1, Turn = 1, Actor = 1 },
            new ReplayBuyBeat { Index = 2, Turn = 2, Actor = 0,
                                 Capital = new HexCoord(0, 0), To = new HexCoord(1, 0),
                                 Level = UnitLevel.Recruit },
        };
        var cursor = new ScriptCursor();
        // Simulate the AI side having consumed Blue's two beats.
        cursor.Advance();
        cursor.Advance();
        var preview = new TutorialPreview(script, PlayerZeroTurnState(), cursor);

        bool ok = preview.TryAccept(new ReplayBuyBeat
        {
            Capital = new HexCoord(0, 0), To = new HexCoord(1, 0),
            Level = UnitLevel.Recruit,
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
    public void TryAccept_Player0BeatBeforePendingNarration_DoesNotFireFinished()
    {
        // Regression: TutorialFinished must fire only when the entire
        // script is consumed, NOT when NextPlayer0Beat returns null
        // because a DisplayText narration beat is gating ahead. Before
        // the fix this fired "Tutorial complete." after the very first
        // turn whenever a narration beat followed.
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            new ReplayDisplayTextBeat { Index = 1, Turn = 1, Actor = -1, Text = "Nice work." },
            new ReplayMoveBeat
            {
                Index = 2, Turn = 2, Actor = 0,
                From = new HexCoord(0, 0), To = new HexCoord(1, 0),
            },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());
        int finishedCount = 0;
        preview.TutorialFinished += () => finishedCount++;

        bool ok = preview.TryAccept(new ReplayEndTurnBeat());

        Assert.True(ok);
        Assert.Equal(0, finishedCount);   // beats #1 and #2 still remain
        Assert.False(preview.IsComplete);
    }

    [Fact]
    public void IsComplete_TrueOnlyAfterAllBeatsConsumed()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        Assert.False(preview.IsComplete);
        preview.TryAccept(new ReplayEndTurnBeat());
        Assert.True(preview.IsComplete);
    }

    [Fact]
    public void TryAccept_BuyBeat_MatchesOnCapitalToAndLevel()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayBuyBeat { Index = 0, Turn = 1, Actor = 0,
                                 Capital = new HexCoord(0, 0), To = new HexCoord(1, 1),
                                 Level = UnitLevel.Soldier },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        Assert.False(preview.TryAccept(new ReplayBuyBeat
        {
            Capital = new HexCoord(0, 0), To = new HexCoord(1, 1),
            Level = UnitLevel.Recruit,  // wrong level
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
                                 Level = UnitLevel.Recruit },
        };
        var preview = new TutorialPreview(script, PlayerZeroTurnState());

        Assert.False(preview.TryAccept(new ReplayEndTurnBeat()));
    }
}
