using Xunit;

namespace FourExHex.Tests;

public class TutorialValidatorTests
{
    [Fact]
    public void MatchesEndTurn_AlwaysTrue()
    {
        // EndTurnBeat carries no per-beat data; any End Turn click
        // matches if the next beat is an EndTurnBeat. The wrapper
        // already checks the kind, so this method just confirms the
        // match symbolically.
        var beat = new EndTurnBeat { Index = 0, Turn = 1, Actor = 0 };
        Assert.True(TutorialValidator.MatchesEndTurn(beat));
    }

    [Fact]
    public void ReasonMismatch_NullExpected_SaysTutorialComplete()
    {
        string msg = TutorialValidator.ReasonMismatch(null, "tile click");
        Assert.Contains("complete", msg);
    }

    [Fact]
    public void ReasonMismatch_ExpectedBeat_MentionsKindAndAttempt()
    {
        var expected = new EndTurnBeat { Index = 2, Turn = 3, Actor = 1 };
        string msg = TutorialValidator.ReasonMismatch(expected, "tile click");
        Assert.Contains("EndTurn", msg);
        Assert.Contains("tile click", msg);
    }

    [Fact]
    public void MatchesBuyPeasant_TrueWhenCoordEqualsAt()
    {
        var beat = new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = new HexCoord(2, 3) };
        Assert.True(TutorialValidator.MatchesBuyPeasant(beat, new HexCoord(2, 3)));
    }

    [Fact]
    public void MatchesBuyPeasant_FalseWhenCoordDiffers()
    {
        var beat = new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = new HexCoord(2, 3) };
        Assert.False(TutorialValidator.MatchesBuyPeasant(beat, new HexCoord(2, 4)));
        Assert.False(TutorialValidator.MatchesBuyPeasant(beat, new HexCoord(3, 3)));
    }
}
