using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The game-over overlay's presentation decision (Godot-free, Controller
/// layer): a viking total wipeout — <see cref="PlayerId.None"/> declared
/// winner — and an AI winner are both presented as a DEFEAT for the human
/// players (no Replay offer); only a human winner gets the ordinary
/// VICTORY presentation.
/// </summary>
public class EndgameOverlayContentTests
{
    [Fact]
    public void For_VikingsWipeout_IsDefeatWithoutReplay()
    {
        EndgameOverlayContent.Content content = EndgameOverlayContent.For(
            PlayerId.None, winnerName: "ignored", winnerIsHuman: false,
            defeatedHumanName: null);

        Assert.Equal("DEFEAT", content.Eyebrow);
        Assert.Equal("The Vikings have conquered the island!", content.Title);
        Assert.False(content.OfferReplay);
    }

    [Fact]
    public void For_HumanWinner_IsVictoryWithReplay()
    {
        EndgameOverlayContent.Content content = EndgameOverlayContent.For(
            PlayerId.FromIndex(0), winnerName: "Red", winnerIsHuman: true,
            defeatedHumanName: null);

        Assert.Equal("VICTORY", content.Eyebrow);
        Assert.Equal("Red wins!", content.Title);
        Assert.True(content.OfferReplay);
    }

    [Fact]
    public void For_AiWinner_IsDefeatInMidGameEliminationVoice()
    {
        // Issue #121: a human losing to an AI must not be congratulated
        // with the AI's victory screen — the framing matches the mid-game
        // elimination overlay ("<Loser> defeated").
        EndgameOverlayContent.Content content = EndgameOverlayContent.For(
            PlayerId.FromIndex(1), winnerName: "Blue", winnerIsHuman: false,
            defeatedHumanName: "Orange");

        Assert.Equal("DEFEAT", content.Eyebrow);
        Assert.Equal("Orange defeated", content.Title);
        Assert.False(content.OfferReplay);
    }

    [Fact]
    public void For_AiWinner_NoGameEndingHumanElimination_AnnouncesAiVictory()
    {
        // The game outlived every human (each already saw their personal
        // mid-game defeat screen) and ran on as an AI-vs-AI endgame — the
        // finish is the surviving AI's victory, announced as such.
        EndgameOverlayContent.Content content = EndgameOverlayContent.For(
            PlayerId.FromIndex(1), winnerName: "Blue", winnerIsHuman: false,
            defeatedHumanName: null);

        Assert.Equal("VICTORY", content.Eyebrow);
        Assert.Equal("Blue wins!", content.Title);
        Assert.True(content.OfferReplay);
    }

    // --- DefeatedHumanFor: whose name goes on the AI-winner overlay ------

    private static readonly Player HumanRed =
        new("Red", PlayerId.FromIndex(0));
    private static readonly Player HumanBlue =
        new("Blue", PlayerId.FromIndex(1));
    private static readonly Player AiGreen =
        new("Green", PlayerId.FromIndex(2), PlayerKind.Computer);

    [Fact]
    public void DefeatedHumanFor_PendingDefeatNamesAHuman_PicksThatHuman()
    {
        // The elimination that ended the game (PendingDefeatScreen survives
        // the winner declaration) beats any roster counting — pass-and-play
        // included.
        var players = new List<Player> { HumanRed, HumanBlue, AiGreen };

        Player? defeated = EndgameOverlayContent.DefeatedHumanFor(
            HumanBlue.Id, players);

        Assert.Same(HumanBlue, defeated);
    }

    [Fact]
    public void DefeatedHumanFor_NoPendingDefeat_IsNull()
    {
        // No game-ending human elimination — even with a sole human in the
        // roster the finish belongs to the winning AI (the human's own
        // defeat was announced when it happened, mid-game).
        var players = new List<Player> { HumanRed, AiGreen };

        Assert.Null(EndgameOverlayContent.DefeatedHumanFor(
            pendingDefeatScreen: null, players));
    }

    [Fact]
    public void DefeatedHumanFor_PendingDefeatNamesAnAi_IsIgnored()
    {
        // Defensive: PendingDefeatScreen is only ever set for humans, but a
        // stale/foreign id must not put an AI's name on the defeat overlay.
        var players = new List<Player> { HumanRed, AiGreen };

        Assert.Null(EndgameOverlayContent.DefeatedHumanFor(
            AiGreen.Id, players));
    }
}
