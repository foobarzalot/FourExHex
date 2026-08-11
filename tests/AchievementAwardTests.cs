// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The controller-side guard around achievement awards: which game
/// endings raise the <see cref="GameEndEvent"/> facts record and which
/// must stay silent. The award arithmetic itself lives in
/// <see cref="AchievementTrackerTests"/>.
/// </summary>
public class AchievementAwardTests
{
    private const string Veteran = AchievementCatalog.Veteran;

    /// <summary>
    /// A one-row board where every tile is the first player's except the
    /// last, with a friendly unit poised next to it. Moving onto that tile
    /// captures the opponent's only territory and wins by domination.
    /// </summary>
    private static ControllerHarness OneMoveFromWinning(
        IAchievementStore store,
        bool firstPlayerIsAi = false,
        bool previewMode = false,
        bool recordingMode = false)
    {
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: firstPlayerIsAi);
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);

        return TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            cols: 5, rows: 1,
            defaultOwner: red.Id,
            ownerOverrides: new[] { (4, 0, blue.Id) },
            previewMode: previewMode,
            recordingMode: recordingMode,
            beforeTerritories: grid =>
                grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Unit(red.Id),
            achievementStore: store);
    }

    private static void PlayWinningMove(ControllerHarness h)
    {
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0)));
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(4, 0)));
    }

    // --- The award happens ---

    [Fact]
    public void HumanWin_ReportsProgress()
    {
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store);

        PlayWinningMove(h);

        Assert.Equal(h.Players[0].Id, h.Session.Winner);
        Assert.Contains((Veteran, 1, 3), store.ProgressReports);
    }

    [Fact]
    public void HumanWin_AwardsExactlyOnce_EvenIfGameEndChecksRunAgain()
    {
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store);

        PlayWinningMove(h);
        // A trailing end-turn press after game over must not re-award.
        h.Hud.ClickEndTurn();

        Assert.Single(store.ProgressReports, r => r.Id == Veteran);
    }

    [Fact]
    public void UnlockingWin_ShowsTheAchievementBanner()
    {
        // Two wins already banked, so this one crosses the target.
        var store = new FakeAchievementStore();
        store.ReportProgress(Veteran, 2, 3);
        ControllerHarness h = OneMoveFromWinning(store);

        PlayWinningMove(h);

        Assert.Contains(Veteran, store.Unlocks);
        Assert.Contains(AchievementBannerContent.For(Veteran), h.Hud.AchievementBanners);
    }

    [Fact]
    public void NonUnlockingWin_ShowsNoBanner()
    {
        // Pre-unlock every row except the Veteran counter so this win can
        // only produce progress — a progress-only report must stay silent.
        var store = new FakeAchievementStore();
        foreach (AchievementDefinition def in AchievementCatalog.All)
        {
            if (def.Id != Veteran) store.Unlock(def.Id);
        }
        store.ClearCallLog();
        ControllerHarness h = OneMoveFromWinning(store);

        PlayWinningMove(h);

        Assert.Equal((Veteran, 1, 3), Assert.Single(store.ProgressReports));
        Assert.Empty(h.Hud.AchievementBanners);
    }

    [Fact]
    public void StasisEnd_StillRaisesFacts_SoChainOfCommandAdvancesOnALoss()
    {
        // Nobody wins (turn cap), but the human fielded a Commander via a
        // combine — the mechanic milestone must still unlock.
        var store = new FakeAchievementStore();
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue },
            cols: 5, rows: 1,
            defaultOwner: blue.Id,
            ownerOverrides: new[] { (0, 0, red.Id), (1, 0, red.Id), (2, 0, red.Id) },
            maxTurnNumber: 1,
            beforeTerritories: grid =>
            {
                grid.Get(HexCoord.FromOffset(1, 0))!.Occupant =
                    new Unit(red.Id, UnitLevel.Captain);
                grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(red.Id);
            },
            achievementStore: store);

        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(2, 0)));
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(1, 0))); // Commander
        h.Hud.ClickEndTurn();  // Red
        h.Hud.ClickEndTurn();  // Blue — rotation completes, turn cap ends the game

        Assert.Null(h.Session.Winner);
        Assert.Contains(AchievementCatalog.ChainOfCommand, store.Unlocks);
        Assert.DoesNotContain(AchievementCatalog.FirstWin, store.Unlocks);
    }

    // --- The award is suppressed ---

    [Fact]
    public void AiWin_AwardsNothing()
    {
        var store = new FakeAchievementStore();
        // Red is the AI here; the same capture makes an AI seat the winner.
        ControllerHarness h = OneMoveFromWinning(store, firstPlayerIsAi: true);

        PlayWinningMove(h);

        Assert.Equal(0, store.TotalCalls);
    }

    [Fact]
    public void PreviewMode_AwardsNothingAndShowsNoBanner()
    {
        var store = new FakeAchievementStore();
        store.ReportProgress(Veteran, 2, 3);
        ControllerHarness h = OneMoveFromWinning(store, previewMode: true);
        store.ClearCallLog();

        PlayWinningMove(h);

        Assert.Equal(0, store.TotalCalls);
        Assert.Empty(h.Hud.AchievementBanners);
    }

    [Fact]
    public void RecordingMode_AwardsNothing()
    {
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store, recordingMode: true);

        PlayWinningMove(h);

        Assert.Equal(0, store.TotalCalls);
    }

    [Fact]
    public void ReplayOfAWonGame_AwardsNothing()
    {
        // The trap this guard exists for: BeginReplay clears the
        // game-ended latch and the winner, then drives the recorded beats
        // to the same ending — so the game-end funnel fires a second time.
        // An append-only unlock would survive that; a counter would not.
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store);
        PlayWinningMove(h);
        Assert.NotEmpty(store.ProgressReports);
        store.ClearCallLog();

        h.Controller.BeginReplay();

        Assert.Equal(0, store.TotalCalls);
    }

    // --- The campaign seam ---

    [Fact]
    public void RaiseCampaignLevelWon_AwardsAndShowsTheBanner()
    {
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store);

        h.Controller.RaiseCampaignLevelWon(
            new CampaignLevelWonEvent(Level: 0, WonCount: 1, TierIndex: 0, TierWonCount: 1));

        Assert.Contains(AchievementCatalog.CampaignFirst, store.Unlocks);
        Assert.Contains(
            AchievementBannerContent.For(AchievementCatalog.CampaignFirst),
            h.Hud.AchievementBanners);
    }

    [Fact]
    public void RaiseCampaignLevelWon_IsSuppressedWithTheSameGuardsAsGameEnd()
    {
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store, previewMode: true);
        store.ClearCallLog();

        h.Controller.RaiseCampaignLevelWon(
            new CampaignLevelWonEvent(Level: 0, WonCount: 1, TierIndex: 0, TierWonCount: 1));

        Assert.Equal(0, store.TotalCalls);
        Assert.Empty(h.Hud.AchievementBanners);
    }

    // --- The default wiring is inert ---

    [Fact]
    public void ControllerWithoutAStore_UsesTheNullStoreAndNeverThrows()
    {
        ControllerHarness h = TestHelpers.BuildControllerGame(
            cols: 5, rows: 1,
            defaultOwner: PlayerId.FromIndex(0),
            ownerOverrides: new[] { (4, 0, PlayerId.FromIndex(1)) },
            beforeTerritories: grid =>
                grid.Get(HexCoord.FromOffset(3, 0))!.Occupant =
                    new Unit(PlayerId.FromIndex(0)));

        PlayWinningMove(h);

        Assert.Equal(PlayerId.FromIndex(0), h.Session.Winner);
    }
}
