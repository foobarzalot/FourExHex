using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class TutorialPlayerTests
{
    private static Tutorial MakeTutorial(int endTurnCount)
    {
        var beats = new List<Beat>();
        for (int i = 0; i < endTurnCount; i++)
        {
            beats.Add(new EndTurnBeat { Index = i, Turn = 1, Actor = 0 });
        }
        return new Tutorial { Beats = beats };
    }

    [Fact]
    public void Constructor_NextExpected_IsFirstBeat()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);

        Assert.Same(t.Beats[0], player.NextExpectedPlayerBeat);
        Assert.Equal(-1, player.CurrentBeatIndex);
        Assert.Empty(player.Snapshots);
    }

    [Fact]
    public void TryAdvanceForEndTurn_HappyPath_AdvancesAndFiresEvents()
    {
        Tutorial t = MakeTutorial(2);
        var player = new TutorialPlayer(t);
        var beatApplied = new List<int>();
        bool finished = false;
        player.BeatApplied += i => beatApplied.Add(i);
        player.TutorialFinished += () => finished = true;

        bool ok = player.TryAdvanceForEndTurn();

        Assert.True(ok);
        Assert.Equal(0, player.CurrentBeatIndex);
        Assert.Equal(new[] { 0 }, beatApplied);
        Assert.False(finished);
        Assert.Same(t.Beats[1], player.NextExpectedPlayerBeat);
    }

    [Fact]
    public void TryAdvanceForEndTurn_LastBeat_FiresTutorialFinished()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);
        bool finished = false;
        player.TutorialFinished += () => finished = true;

        bool ok = player.TryAdvanceForEndTurn();

        Assert.True(ok);
        Assert.True(finished);
        Assert.Null(player.NextExpectedPlayerBeat);
    }

    [Fact]
    public void TryAdvanceForEndTurn_AfterFinished_RejectsAndDoesNotAdvance()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);
        player.TryAdvanceForEndTurn();   // exhaust
        Beat? rejectedBeat = new EndTurnBeat { Index = 99 };  // sentinel non-null
        string? reason = null;
        player.PlayerActionRejected += (b, r) => { rejectedBeat = b; reason = r; };

        bool ok = player.TryAdvanceForEndTurn();

        Assert.False(ok);
        Assert.Null(rejectedBeat);              // NextExpectedPlayerBeat is null
        Assert.NotNull(reason);
        Assert.Contains("complete", reason!);
    }

    [Fact]
    public void NotifyRejected_FiresEventWithExpectedBeatAndAttempt()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);
        Beat? rejectedBeat = null;
        string? reason = null;
        player.PlayerActionRejected += (b, r) => { rejectedBeat = b; reason = r; };

        player.NotifyRejected("tile click");

        Assert.Same(t.Beats[0], rejectedBeat);
        Assert.NotNull(reason);
        Assert.Contains("tile click", reason!);
        Assert.Contains("EndTurn", reason);
    }

    private static Tutorial MakeBuyPeasantTutorial(HexCoord at, int after = 0)
    {
        // `after` EndTurn beats precede the BuyPeasantBeat — used to
        // verify "wrong-kind next beat" rejection.
        var beats = new List<Beat>();
        for (int i = 0; i < after; i++)
        {
            beats.Add(new EndTurnBeat { Index = i, Turn = 1, Actor = 0 });
        }
        beats.Add(new BuyPeasantBeat { Index = after, Turn = 1, Actor = 0, At = at });
        return new Tutorial { Beats = beats };
    }

    [Fact]
    public void TryArmBuyPeasant_HappyPath_ArmsAndReturnsTrue()
    {
        Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
        var player = new TutorialPlayer(t);

        bool ok = player.TryArmBuyPeasant();

        Assert.True(ok);
        Assert.True(player.IsArmedForBuyPeasant);
        Assert.Same(t.Beats[0], player.ArmedBeat);
        Assert.Equal(-1, player.CurrentBeatIndex);
        Assert.Same(t.Beats[0], player.NextExpectedPlayerBeat);
    }

    [Fact]
    public void TryArmBuyPeasant_AlreadyArmed_RejectsAndDoesNotChangeArm()
    {
        Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
        var player = new TutorialPlayer(t);
        player.TryArmBuyPeasant();
        bool rejected = false;
        player.PlayerActionRejected += (_, _) => rejected = true;

        bool ok = player.TryArmBuyPeasant();

        Assert.False(ok);
        Assert.True(rejected);
        Assert.True(player.IsArmedForBuyPeasant);
        Assert.Same(t.Beats[0], player.ArmedBeat);
    }

    [Fact]
    public void TryArmBuyPeasant_NextBeatIsEndTurn_RejectsAndDoesNotArm()
    {
        Tutorial t = MakeTutorial(1);
        var player = new TutorialPlayer(t);
        bool rejected = false;
        player.PlayerActionRejected += (_, _) => rejected = true;

        bool ok = player.TryArmBuyPeasant();

        Assert.False(ok);
        Assert.True(rejected);
        Assert.False(player.IsArmedForBuyPeasant);
        Assert.Null(player.ArmedBeat);
    }

    [Fact]
    public void TryAdvanceForBuyPeasantTile_HappyPath_AdvancesAndDisarms()
    {
        Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
        var player = new TutorialPlayer(t);
        player.TryArmBuyPeasant();
        var beatApplied = new List<int>();
        bool finished = false;
        player.BeatApplied += i => beatApplied.Add(i);
        player.TutorialFinished += () => finished = true;

        bool ok = player.TryAdvanceForBuyPeasantTile(new HexCoord(4, 5));

        Assert.True(ok);
        Assert.False(player.IsArmedForBuyPeasant);
        Assert.Null(player.ArmedBeat);
        Assert.Equal(0, player.CurrentBeatIndex);
        Assert.Equal(new[] { 0 }, beatApplied);
        Assert.True(finished);
        Assert.Null(player.NextExpectedPlayerBeat);
    }

    [Fact]
    public void TryAdvanceForBuyPeasantTile_WrongTile_RejectsAndKeepsArm()
    {
        Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
        var player = new TutorialPlayer(t);
        player.TryArmBuyPeasant();
        string? reason = null;
        player.PlayerActionRejected += (_, r) => reason = r;

        bool ok = player.TryAdvanceForBuyPeasantTile(new HexCoord(4, 4));

        Assert.False(ok);
        Assert.True(player.IsArmedForBuyPeasant);
        Assert.Equal(-1, player.CurrentBeatIndex);
        Assert.NotNull(reason);
        Assert.Contains("tile click", reason!);
        Assert.Contains("BuyPeasant", reason);
    }

    [Fact]
    public void TryAdvanceForBuyPeasantTile_NotArmed_RejectsAndPointerUnchanged()
    {
        Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
        var player = new TutorialPlayer(t);
        bool rejected = false;
        player.PlayerActionRejected += (_, _) => rejected = true;

        bool ok = player.TryAdvanceForBuyPeasantTile(new HexCoord(4, 5));

        Assert.False(ok);
        Assert.True(rejected);
        Assert.Equal(-1, player.CurrentBeatIndex);
    }

    [Fact]
    public void DisarmIfAny_ClearsArm_AndIsIdempotent()
    {
        Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
        var player = new TutorialPlayer(t);
        player.TryArmBuyPeasant();
        Assert.True(player.IsArmedForBuyPeasant);

        player.DisarmIfAny();

        Assert.False(player.IsArmedForBuyPeasant);
        Assert.Null(player.ArmedBeat);

        player.DisarmIfAny();
        Assert.False(player.IsArmedForBuyPeasant);
    }
}
