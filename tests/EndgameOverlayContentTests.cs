using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The game-over overlay's presentation decision (Godot-free, Controller
/// layer): a viking total wipeout — <see cref="PlayerId.None"/> declared
/// winner — is presented as a DEFEAT for the players (no Replay offer),
/// while a roster player's win stays the ordinary VICTORY presentation.
/// </summary>
public class EndgameOverlayContentTests
{
    [Fact]
    public void For_VikingsWipeout_IsDefeatWithoutReplay()
    {
        EndgameOverlayContent.Content content =
            EndgameOverlayContent.For(PlayerId.None, winnerName: "ignored");

        Assert.Equal("DEFEAT", content.Eyebrow);
        Assert.Equal("The Vikings have conquered the island!", content.Title);
        Assert.False(content.OfferReplay);
    }

    [Fact]
    public void For_PlayerWinner_IsVictoryWithReplay()
    {
        EndgameOverlayContent.Content content =
            EndgameOverlayContent.For(PlayerId.FromIndex(0), winnerName: "Red");

        Assert.Equal("VICTORY", content.Eyebrow);
        Assert.Equal("Red wins!", content.Title);
        Assert.True(content.OfferReplay);
    }
}
