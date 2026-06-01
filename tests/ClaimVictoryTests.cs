using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for the End-Turn-time "claim victory" prompt. Triggers when a
/// human player presses End Turn while crossing one of the
/// <see cref="WinConditionRules.ClaimVictoryThresholdsPercent"/> tiers
/// (50/75/90). Each tier fires at most once per human per game; "show
/// only highest unseen" means a single End Turn that crosses multiple
/// tiers shows just the topmost not-yet-dismissed one.
/// </summary>
public class ClaimVictoryTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>
    /// Build a <paramref name="cols"/>x<paramref name="rows"/> grid with
    /// the given Red ownership count. Red occupies the first
    /// <paramref name="redCount"/> tiles in row-major order; Blue owns
    /// the rest. Both colors retain at least one capital-bearing
    /// territory so neither is auto-eliminated (provided redCount is in
    /// the open range (0, cols*rows)).
    /// </summary>
    private static (GameState State, SessionState Session, MockHexMapView Map,
                    MockHudView Hud, GameController Controller, Player RedP,
                    Player BlueP)
        BuildGame(
            int redCount,
            int cols = 5,
            int rows = 2,
            PlayerKind redKind = PlayerKind.Human,
            PlayerKind blueKind = PlayerKind.Human)
    {
        var redP = new Player("Red", PlayerId.FromIndex(0), redKind);
        var blueP = new Player("Blue", PlayerId.FromIndex(1), blueKind);
        var players = new List<Player> { redP, blueP };

        var grid = TestHelpers.BuildRectGrid(cols, rows, Blue);
        int flipped = 0;
        for (int row = 0; row < rows && flipped < redCount; row++)
        {
            for (int col = 0; col < cols && flipped < redCount; col++)
            {
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = Red;
                flipped++;
            }
        }

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        controller.StartGame();
        return (state, session, map, hud, controller, redP, blueP);
    }

    [Fact]
    public void EndTurn_WhenAlreadySoleCapitalBearer_WinsImmediately_NoClaimPrompt()
    {
        // Red holds a capital-bearing territory; the only enemy (Blue) has
        // been reduced to an orphan singleton (no capital). Red owns 75%
        // so the claim-victory prompt WOULD fire — but this End Turn wins
        // outright (WinnerAtEndOfTurn: Red is the sole capital-bearer), so
        // the prompt must be skipped and victory declared immediately.
        var redP = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blueP = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
        var players = new List<Player> { redP, blueP };

        var grid = TestHelpers.BuildRectGrid(4, 1, Blue);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = Red;
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = Red;
        // Blue owns only (3,0) — an orphan singleton with no capital.

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud);
        bool gameEnded = false;
        controller.GameEnded += () => gameEnded = true;
        controller.StartGame();
        Assert.Equal(0, state.Turns.CurrentPlayerIndex); // Red's turn
        Assert.False(session.IsGameOver);                // not won mid-turn (Blue still owns a tile)

        hud.ClickEndTurn();

        // Straight to victory — no "Claim Victory?" prompt.
        Assert.False(session.PendingClaimVictory.HasValue);
        Assert.True(session.IsGameOver);
        Assert.Equal(Red, session.Winner);
        Assert.True(gameEnded);
    }

    [Fact]
    public void GraduateFromTutorialScripting_ResumesOrdinaryGameEndRules()
    {
        // A preview-mode controller suppresses the win overlay and the
        // claim-victory prompt while the tutorial is scripted. Once it
        // graduates (script exhausted), ordinary rules resume: the win
        // overlay un-suppresses and the claim-victory prompt fires again.
        var redP = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blueP = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
        var players = new List<Player> { redP, blueP };
        var grid = TestHelpers.BuildRectGrid(5, 2, Blue);
        for (int i = 0; i < 6; i++) // Red 60% — would prompt at the 50 tier
        {
            grid.Get(HexCoord.FromOffset(i % 5, i / 5))!.Owner = Red;
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud, previewMode: true);
        controller.StartGame();
        Assert.True(hud.VictoryOverlaySuppressed);   // suppressed during the tutorial

        // A scripted cue instruction is pinned in the message panel.
        hud.ShowTutorialMessage("Press End Turn.");

        controller.GraduateFromTutorialScripting();

        Assert.False(hud.VictoryOverlaySuppressed);   // ordinary rules resume
        Assert.Null(hud.CurrentTutorialMessage);      // stale cue text cleared
        hud.ClickEndTurn();
        Assert.True(session.PendingClaimVictory.HasValue); // prompt fires again
        Assert.Equal(50, session.PendingClaimVictory!.Value.ThresholdPercent);
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
        // Turn advanced normally (Red → Blue).
        Assert.True(g.State.Turns.TurnNumber > turnBefore
            || g.State.Turns.CurrentPlayerIndex != 0);
    }

    [Fact]
    public void EndTurn_HumanAtSixtyPercent_FirstTime_PromptsAtFiftyTier()
    {
        // 6 of 10 = 60% — meets only the 50% tier.
        var g = BuildGame(redCount: 6);
        int turnBefore = g.State.Turns.TurnNumber;
        int currentBefore = g.State.Turns.CurrentPlayerIndex;

        g.Hud.ClickEndTurn();

        Assert.True(g.Session.PendingClaimVictory.HasValue);
        Assert.Equal(Red, g.Session.PendingClaimVictory!.Value.Player);
        Assert.Equal(50, g.Session.PendingClaimVictory!.Value.ThresholdPercent);
        // Turn did NOT advance.
        Assert.Equal(turnBefore, g.State.Turns.TurnNumber);
        Assert.Equal(currentBefore, g.State.Turns.CurrentPlayerIndex);
        Assert.False(g.Session.IsGameOver);
        // Color is NOT yet recorded — only on dismissal.
        Assert.False(g.Session.ClaimVictoryPromptedHighestThreshold.ContainsKey(Red));
    }

    [Fact]
    public void EndTurn_PreviewMode_HumanCrossingTier_DoesNotPrompt()
    {
        // In Tutorial Preview the claim-victory modal would interrupt
        // the scripted flow with a prompt the tutorial author can't
        // pre-record. Building a controller with previewMode: true must
        // suppress every PendingClaimVictory assignment regardless of
        // how much of the map the human player controls.
        var redP = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blueP = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
        var players = new List<Player> { redP, blueP };

        var grid = TestHelpers.BuildRectGrid(5, 2, Blue);
        // 6/10 tiles = 60% (50 tier), 8/10 = 80% (75 tier), 10/10 = 100%
        // (90 tier — actually domination, but the 50/75/90 path would
        // fire first). 8/10 picks the 75% tier — the most likely tier a
        // mid-tutorial dev would cross.
        for (int i = 0; i < 8; i++)
        {
            grid.Get(HexCoord.FromOffset(i % 5, i / 5))!.Owner = Red;
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud, previewMode: true);
        controller.StartGame();
        int turnBefore = state.Turns.TurnNumber;
        int currentBefore = state.Turns.CurrentPlayerIndex;

        hud.ClickEndTurn();

        Assert.False(session.PendingClaimVictory.HasValue);
        // Turn advanced normally — preview mode lets End Turn through
        // the way it does for AI players.
        Assert.True(state.Turns.TurnNumber > turnBefore
            || state.Turns.CurrentPlayerIndex != currentBefore);
    }

    [Fact]
    public void EndTurn_RecordingMode_HumanCrossingTier_DoesNotPrompt()
    {
        // In Tutorial Builder's Record mode every slot is forced Human
        // so the dev plays all six. The same scripted-flow concern as
        // Preview applies: a claim-victory modal would interrupt the
        // recording session. recordingMode: true suppresses it.
        var redP = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blueP = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
        var players = new List<Player> { redP, blueP };

        var grid = TestHelpers.BuildRectGrid(5, 2, Blue);
        for (int i = 0; i < 8; i++)
        {
            grid.Get(HexCoord.FromOffset(i % 5, i / 5))!.Owner = Red;
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(state, session, map, hud, recordingMode: true);
        controller.StartGame();
        int turnBefore = state.Turns.TurnNumber;
        int currentBefore = state.Turns.CurrentPlayerIndex;

        hud.ClickEndTurn();

        Assert.False(session.PendingClaimVictory.HasValue);
        Assert.True(state.Turns.TurnNumber > turnBefore
            || state.Turns.CurrentPlayerIndex != currentBefore);
    }

    [Fact]
    public void EndTurn_HumanAtEightyPercent_NoPriors_PromptsAtSeventyFiveTier()
    {
        // 8 of 10 = 80% — meets 50 and 75; "show only highest unseen"
        // makes this a 75% prompt, not 50%.
        var g = BuildGame(redCount: 8);

        g.Hud.ClickEndTurn();

        Assert.True(g.Session.PendingClaimVictory.HasValue);
        Assert.Equal(Red, g.Session.PendingClaimVictory!.Value.Player);
        Assert.Equal(75, g.Session.PendingClaimVictory!.Value.ThresholdPercent);
    }

    [Fact]
    public void EndTurn_HumanAtNinetyFivePercent_NoPriors_PromptsAtNinetyTier()
    {
        // 95 of 100 = 95% — skipping straight to the 90% tier.
        var g = BuildGame(redCount: 95, cols: 10, rows: 10);

        g.Hud.ClickEndTurn();

        Assert.True(g.Session.PendingClaimVictory.HasValue);
        Assert.Equal(90, g.Session.PendingClaimVictory!.Value.ThresholdPercent);
    }

    [Fact]
    public void EndTurn_AfterDismissingFifty_NextPromptIsAtSeventyFiveWhenReached()
    {
        // Stage Red at 80% with prior dismissal at 50%. Next End Turn
        // should fire the 75% prompt (not skip — since 80 > 75 > 50).
        var g = BuildGame(redCount: 8);
        g.Session.ClaimVictoryPromptedHighestThreshold[Red] = 50;

        g.Hud.ClickEndTurn();

        Assert.True(g.Session.PendingClaimVictory.HasValue);
        Assert.Equal(75, g.Session.PendingClaimVictory!.Value.ThresholdPercent);
    }

    [Fact]
    public void EndTurn_AllThreeTiersDismissed_NoMorePrompts()
    {
        // Even at 95% ownership, the prompt is suppressed once the
        // highest-prompted entry is at 90.
        var g = BuildGame(redCount: 95, cols: 10, rows: 10);
        g.Session.ClaimVictoryPromptedHighestThreshold[Red] = 90;

        g.Hud.ClickEndTurn();

        Assert.False(g.Session.PendingClaimVictory.HasValue);
    }

    [Fact]
    public void EndTurn_AiAboveHalf_DoesNotPrompt()
    {
        // Red is AI here. Even at 60% it should not see a prompt.
        var g = BuildGame(redCount: 6, redKind: PlayerKind.Computer);
        // Red's AI turn already ran via StartGame.
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
        // Threshold recorded so a (vacuous) re-show couldn't fire.
        Assert.Equal(50, g.Session.ClaimVictoryPromptedHighestThreshold[Red]);
    }

    [Fact]
    public void ClaimVictoryWinNow_AtSeventyFiveTier_RecordsCorrectThreshold()
    {
        // Dismissing the 75% prompt (not 50%) should record 75.
        var g = BuildGame(redCount: 8);

        g.Hud.ClickEndTurn();
        Assert.Equal(75, g.Session.PendingClaimVictory!.Value.ThresholdPercent);

        g.Hud.ClickClaimVictoryWinNow();

        Assert.Equal(75, g.Session.ClaimVictoryPromptedHighestThreshold[Red]);
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
        Assert.Equal(50, g.Session.ClaimVictoryPromptedHighestThreshold[Red]);
        // Turn advanced (Red → Blue), or the end-of-turn win check fired.
        bool advanced = g.State.Turns.TurnNumber > turnBefore
            || g.State.Turns.CurrentPlayerIndex != 0
            || g.Session.IsGameOver;
        Assert.True(advanced);
    }

    [Fact]
    public void EndTurn_AfterContinueDismissalAtFifty_DoesNotRePromptAtFifty()
    {
        // 6 of 10 → prompt at 50% → Continue → record 50. On a
        // subsequent human End Turn at 60% (still below 75%), no prompt
        // should fire.
        var g = BuildGame(redCount: 6);

        g.Hud.ClickEndTurn();
        g.Hud.ClickClaimVictoryContinue();
        Assert.Equal(50, g.Session.ClaimVictoryPromptedHighestThreshold[Red]);

        // Cycle back to Red.
        int safety = 10;
        while (!g.Session.IsGameOver
               && g.State.Turns.CurrentPlayer.Id != Red
               && safety-- > 0)
        {
            g.Hud.ClickEndTurn();
        }

        if (g.Session.IsGameOver) return; // Red won outright; vacuous

        g.Hud.ClickEndTurn();
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

        Assert.Equal(turnBefore, g.State.Turns.TurnNumber);
        Assert.Equal(currentBefore, g.State.Turns.CurrentPlayerIndex);
        Assert.Empty(g.Session.ClaimVictoryPromptedHighestThreshold);
    }

    [Fact]
    public void ClaimVictoryWinNow_FromRepeatedMovementMode_ClearsPendingAction()
    {
        // Bug: pressing End Turn → claim-victory prompt → Win Now while
        // mid-N-cycle in repeated-movement mode used to leave Mode and
        // MoveSource set, so the HUD showed "Red Wins!" alongside the
        // stale "Click to place a Soldier" action hint. The DeclareWinner
        // path must clear pending-action state for all game-over paths.
        var g = BuildGame(redCount: 6);
        // Place a Red unit on a Red tile (BuildGame flips ownership but
        // doesn't place units). (0,0) is Red after BuildGame; pick a Red
        // tile likely to be non-capital so N has a movable unit to find.
        g.State.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(Red);
        Territory redT = g.State.Territories.First(t => t.Owner == Red);
        g.Map.SimulateClick(g.State.Grid.Get(redT.Coords.First()));
        g.Hud.PressNextUnit();
        Assert.Equal(SessionState.ActionMode.MovingUnit, g.Session.Mode);
        Assert.True(g.Session.RepeatedMovement);

        g.Hud.ClickEndTurn();
        Assert.True(g.Session.PendingClaimVictory.HasValue);

        g.Hud.ClickClaimVictoryWinNow();

        Assert.True(g.Session.IsGameOver);
        Assert.Equal(SessionState.ActionMode.None, g.Session.Mode);
        Assert.Null(g.Session.MoveSource);
        Assert.False(g.Session.RepeatedMovement);
    }
}
