// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The controller-side guard around achievement awards: which game
/// endings raise <see cref="AchievementEvent.GameWonByHuman"/> and which
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
        Assert.Equal((Veteran, 1, 3), Assert.Single(store.ProgressReports));
    }

    [Fact]
    public void HumanWin_AwardsExactlyOnce_EvenIfGameEndChecksRunAgain()
    {
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store);

        PlayWinningMove(h);
        // A trailing end-turn press after game over must not re-award.
        h.Hud.ClickEndTurn();

        Assert.Single(store.ProgressReports);
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
    public void PreviewMode_AwardsNothing()
    {
        var store = new FakeAchievementStore();
        ControllerHarness h = OneMoveFromWinning(store, previewMode: true);

        PlayWinningMove(h);

        Assert.Equal(0, store.TotalCalls);
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
        Assert.Single(store.ProgressReports);
        store.ClearCallLog();

        h.Controller.BeginReplay();

        Assert.Equal(0, store.TotalCalls);
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
