using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for the End-Turn-time "claim victory" prompt. Triggers when a
/// human player presses End Turn while owning strictly more than 50% of
/// all land tiles. Fires at most once per human per game.
/// </summary>
public class ClaimVictoryTests
{
    private static readonly Color Red = new Color(1f, 0f, 0f);
    private static readonly Color Blue = new Color(0f, 0f, 1f);

    /// <summary>
    /// Build a 5x2 grid (10 tiles) with the given Red ownership count.
    /// Red occupies the first <paramref name="redCount"/> tiles in row-
    /// major order; Blue owns the rest. Both colors retain at least one
    /// capital-bearing territory so neither is auto-eliminated.
    /// </summary>
    private static (GameState State, SessionState Session, MockHexMapView Map,
                    MockHudView Hud, GameController Controller, Player RedP,
                    Player BlueP)
        BuildGame(int redCount, AiKind redKind = AiKind.Human, AiKind blueKind = AiKind.Human)
    {
        var redP = new Player("Red", Red, redKind);
        var blueP = new Player("Blue", Blue, blueKind);
        var players = new List<Player> { redP, blueP };

        // Start with everything Blue, then flip the first redCount in
        // row-major lex order to Red.
        var grid = TestHelpers.BuildRectGrid(5, 2, Blue);
        int flipped = 0;
        for (int row = 0; row < 2 && flipped < redCount; row++)
        {
            for (int col = 0; col < 5 && flipped < redCount; col++)
            {
                grid.Get(HexCoord.FromOffset(col, row))!.Color = Red;
                flipped++;
            }
        }

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
        {
            map.TileIndex[kvp.Key] = kvp.Value;
        }
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();
        return (state, session, map, hud, controller, redP, blueP);
    }

    [Fact]
    public void EndTurn_HumanAtExactlyHalf_DoesNotPrompt()
    {
        // 5 of 10 tiles = exactly 50%, NOT > 50%.
        var g = BuildGame(redCount: 5);
        Assert.Equal(0, g.State.Turns.CurrentPlayerIndex); // Red's turn
        int turnBefore = g.State.Turns.TurnNumber;

        g.Hud.ClickEndTurn();

        Assert.False(g.Session.PendingClaimVictory.HasValue);
        // Turn advanced normally (Red → Blue, Blue (AI) plays, back to Red T2).
        Assert.True(g.State.Turns.TurnNumber > turnBefore
            || g.State.Turns.CurrentPlayerIndex != 0);
    }

    [Fact]
    public void EndTurn_HumanAboveHalf_FirstTime_TriggersPrompt()
    {
        // 6 of 10 = 60%.
        var g = BuildGame(redCount: 6);
        int turnBefore = g.State.Turns.TurnNumber;
        int currentBefore = g.State.Turns.CurrentPlayerIndex;

        g.Hud.ClickEndTurn();

        Assert.True(g.Session.PendingClaimVictory.HasValue);
        Assert.Equal(Red, g.Session.PendingClaimVictory!.Value);
        // Turn did NOT advance; AI did NOT run; no end-of-turn processing.
        Assert.Equal(turnBefore, g.State.Turns.TurnNumber);
        Assert.Equal(currentBefore, g.State.Turns.CurrentPlayerIndex);
        Assert.False(g.Session.IsGameOver);
        // Color is NOT yet recorded — only on dismissal.
        Assert.DoesNotContain(Red, g.Session.ClaimVictoryPromptedColors);
    }

    [Fact]
    public void EndTurn_AiAboveHalf_DoesNotPrompt()
    {
        // Red is AI here. Even at 60% it should not see a prompt.
        var g = BuildGame(redCount: 6, redKind: AiKind.Random);
        // Red's AI turn already ran via StartGame. Whatever state it left
        // us in, no prompt should ever have been raised.
        Assert.False(g.Session.PendingClaimVictory.HasValue);
    }

    [Fact]
    public void ClaimVictoryWinNow_DeclaresHumanAsWinnerAndFiresGameEnded()
    {
        var g = BuildGame(redCount: 6);
        bool gameEnded = false;
        g.Controller.GameEnded += () => gameEnded = true;

        g.Hud.ClickEndTurn();
        Assert.True(g.Session.PendingClaimVictory.HasValue);

        g.Hud.ClickClaimVictoryWinNow();

        Assert.False(g.Session.PendingClaimVictory.HasValue);
        Assert.True(g.Session.IsGameOver);
        Assert.Equal(Red, g.Session.Winner);
        Assert.True(gameEnded);
        Assert.Contains(Red, g.Session.ClaimVictoryPromptedColors);
    }

    [Fact]
    public void ClaimVictoryContinue_ProceedsWithEndTurnNormally()
    {
        var g = BuildGame(redCount: 6);
        int turnBefore = g.State.Turns.TurnNumber;

        g.Hud.ClickEndTurn();
        Assert.True(g.Session.PendingClaimVictory.HasValue);
        // Turn paused — has not advanced.
        Assert.Equal(turnBefore, g.State.Turns.TurnNumber);
        Assert.Equal(0, g.State.Turns.CurrentPlayerIndex); // still Red

        g.Hud.ClickClaimVictoryContinue();

        Assert.False(g.Session.PendingClaimVictory.HasValue);
        Assert.Contains(Red, g.Session.ClaimVictoryPromptedColors);
        // Turn advanced (Red → Blue, Blue (AI) plays, back to Red T2 OR
        // sole-capital end-of-turn win for Red).
        bool advanced = g.State.Turns.TurnNumber > turnBefore
            || g.State.Turns.CurrentPlayerIndex != 0
            || g.Session.IsGameOver;
        Assert.True(advanced);
    }

    [Fact]
    public void EndTurn_AfterContinueDismissal_DoesNotRePrompt()
    {
        // 6 of 10 → prompt → Continue → record Red. On a subsequent
        // human End Turn at >50%, no re-prompt.
        var g = BuildGame(redCount: 6);

        g.Hud.ClickEndTurn();
        g.Hud.ClickClaimVictoryContinue();
        Assert.Contains(Red, g.Session.ClaimVictoryPromptedColors);

        // Cycle back to Red somehow. Easiest: if Red is current after
        // the previous cycle, just End Turn again. Otherwise advance
        // by ending turns until we're back to Red.
        int safety = 10;
        while (!g.Session.IsGameOver
               && g.State.Turns.CurrentPlayer.Color != Red
               && safety-- > 0)
        {
            g.Hud.ClickEndTurn();
        }

        if (g.Session.IsGameOver) return; // Red already won outright; vacuous

        Assert.False(g.Session.PendingClaimVictory.HasValue);
        int turnBefore = g.State.Turns.TurnNumber;
        g.Hud.ClickEndTurn();
        // No prompt; turn moves on (advance, AI, game-over, etc.).
        Assert.False(g.Session.PendingClaimVictory.HasValue);
    }

    [Fact]
    public void ClaimVictoryWinNow_NoOp_WhenNotPrompted()
    {
        var g = BuildGame(redCount: 4); // 40%, no prompt would fire
        Assert.False(g.Session.PendingClaimVictory.HasValue);

        g.Hud.ClickClaimVictoryWinNow();

        Assert.False(g.Session.IsGameOver);
        Assert.Null(g.Session.Winner);
    }

    [Fact]
    public void ClaimVictoryContinue_NoOp_WhenNotPrompted()
    {
        var g = BuildGame(redCount: 4);
        int turnBefore = g.State.Turns.TurnNumber;
        int currentBefore = g.State.Turns.CurrentPlayerIndex;

        g.Hud.ClickClaimVictoryContinue();

        // Should not have advanced the turn.
        Assert.Equal(turnBefore, g.State.Turns.TurnNumber);
        Assert.Equal(currentBefore, g.State.Turns.CurrentPlayerIndex);
        Assert.Empty(g.Session.ClaimVictoryPromptedColors);
    }
}
