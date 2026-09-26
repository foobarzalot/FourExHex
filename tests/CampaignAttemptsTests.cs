// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The Godot-free half of per-level campaign attempt storage: how a stored
/// save classifies (none / unfinished / finished), the recency index sidecar
/// (<c>user://campaign_attempts.json</c>) and its tolerant read, and the
/// landing Resume pick across the freeform autosave and campaign attempts.
/// </summary>
public class CampaignAttemptsTests
{
    private static LoadedSave Save(int? winnerIndex, bool withReplay)
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players, 0, 4), new Treasury());
        Replay? replay = withReplay
            ? new Replay(GameStateSnapshot.Capture(grid, state.Treasury, territories), 1, 0,
                new List<ReplayBeat>())
            : null;
        return new LoadedSave(state, players, 1, 100, "level_00",
            replay: replay, campaignLevel: 0, winnerIndex: winnerIndex);
    }

    // --- Classification ---------------------------------------------------

    [Fact]
    public void Classify_Null_IsNone() =>
        Assert.Equal(CampaignAttemptKind.None, CampaignAttempts.Classify(null));

    [Fact]
    public void Classify_InProgress_IsUnfinished() =>
        Assert.Equal(CampaignAttemptKind.Unfinished, CampaignAttempts.Classify(Save(null, true)));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Classify_WithWinner_IsFinished(int winner) =>
        Assert.Equal(CampaignAttemptKind.Finished, CampaignAttempts.Classify(Save(winner, true)));

    [Fact]
    public void CanReplay_TracksReplayPayload()
    {
        Assert.True(CampaignAttempts.CanReplay(Save(0, withReplay: true)));
        // The ReplayVersion gate drops a stale replay on load; nothing to watch.
        Assert.False(CampaignAttempts.CanReplay(Save(0, withReplay: false)));
    }

    [Theory]
    [InlineData(0, "level_00")]
    [InlineData(10, "level_0A")]
    [InlineData(255, "level_FF")]
    public void FileNameFor_UsesTwoDigitHexLabel(int level, string expected) =>
        Assert.Equal(expected, CampaignAttempts.FileNameFor(level));

    // --- Index ------------------------------------------------------------

    [Fact]
    public void Index_SetRemoveEntries()
    {
        var index = new CampaignAttemptIndex();
        index.Set(new CampaignAttemptEntry(7, 1000, 3, null));
        index.Set(new CampaignAttemptEntry(9, 2000, 40, 0));
        index.Set(new CampaignAttemptEntry(7, 1500, 5, null)); // replaces

        Assert.Equal(2, index.Entries.Count);
        Assert.Equal(1500, index.Entries.Single(e => e.Level == 7).SavedAtUnix);
        Assert.True(index.Entries.Single(e => e.Level == 9).IsFinished);
        Assert.False(index.Entries.Single(e => e.Level == 7).IsFinished);

        Assert.True(index.Remove(9));
        Assert.False(index.Remove(9));
        Assert.Single(index.Entries);
    }

    [Fact]
    public void Index_RoundTrip_PreservesEntries()
    {
        var index = new CampaignAttemptIndex();
        index.Set(new CampaignAttemptEntry(7, 1000, 3, null));
        index.Set(new CampaignAttemptEntry(255, 2000, 40, -1));

        string json = CampaignAttemptIndexSerializer.Serialize(index);
        CampaignAttemptIndex loaded = CampaignAttemptIndexSerializer.Deserialize(json);

        Assert.Contains("\"FormatVersion\": 1", json);
        Assert.Equal(2, loaded.Entries.Count);
        CampaignAttemptEntry a = loaded.Entries.Single(e => e.Level == 7);
        Assert.Equal((1000L, 3, (int?)null), (a.SavedAtUnix, a.TurnNumber, a.WinnerIndex));
        CampaignAttemptEntry b = loaded.Entries.Single(e => e.Level == 255);
        Assert.Equal((2000L, 40, (int?)-1), (b.SavedAtUnix, b.TurnNumber, b.WinnerIndex));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all {")]
    [InlineData("null")]
    public void Index_Deserialize_CorruptOrEmpty_Throws(string json) =>
        Assert.ThrowsAny<Exception>(() => CampaignAttemptIndexSerializer.Deserialize(json));

    [Fact]
    public void Index_Deserialize_UnsupportedFutureVersion_Throws()
    {
        string json = CampaignAttemptIndexSerializer.Serialize(new CampaignAttemptIndex())
            .Replace("\"FormatVersion\": 1", "\"FormatVersion\": 99");
        Assert.ThrowsAny<Exception>(() => CampaignAttemptIndexSerializer.Deserialize(json));
    }

    [Fact]
    public void Index_FromEntries_ToleratesDamage()
    {
        CampaignAttemptIndex index = CampaignAttemptIndex.FromEntries(new[]
        {
            new CampaignAttemptEntry(3, 100, 1, null),
            new CampaignAttemptEntry(3, 300, 2, null),   // duplicate: newer wins
            new CampaignAttemptEntry(3, 200, 9, 0),      // duplicate, older: dropped
            new CampaignAttemptEntry(-1, 100, 1, null),  // out of range: dropped
            new CampaignAttemptEntry(256, 100, 1, null), // out of range: dropped
            new CampaignAttemptEntry(5, -50, -2, null),  // negatives clamp to 0
        });

        Assert.Equal(2, index.Entries.Count);
        CampaignAttemptEntry three = index.Entries.Single(e => e.Level == 3);
        Assert.Equal((300L, 2, (int?)null), (three.SavedAtUnix, three.TurnNumber, three.WinnerIndex));
        CampaignAttemptEntry five = index.Entries.Single(e => e.Level == 5);
        Assert.Equal((0L, 0), (five.SavedAtUnix, five.TurnNumber));
    }

    [Fact]
    public void Index_FromEntries_NullIsEmpty() =>
        Assert.Empty(CampaignAttemptIndex.FromEntries(null).Entries);

    // --- Resume pick ------------------------------------------------------

    [Fact]
    public void ResumePick_NothingStored_IsNull() =>
        Assert.Null(ResumeTarget.Pick(null, Array.Empty<CampaignAttemptEntry>()));

    [Fact]
    public void ResumePick_AutosaveOnly()
    {
        ResumeTarget? pick = ResumeTarget.Pick(500, Array.Empty<CampaignAttemptEntry>());
        Assert.True(pick!.Value.IsAutosave);
    }

    [Fact]
    public void ResumePick_UnfinishedAttemptOnly()
    {
        ResumeTarget? pick = ResumeTarget.Pick(null,
            new[] { new CampaignAttemptEntry(12, 500, 3, null) });
        Assert.Equal(12, pick!.Value.CampaignLevel);
    }

    [Fact]
    public void ResumePick_FinishedAttemptsAreNotResumable()
    {
        Assert.Null(ResumeTarget.Pick(null,
            new[] { new CampaignAttemptEntry(12, 500, 3, WinnerIndex: 0) }));
        // ...and never outrank an older autosave.
        ResumeTarget? pick = ResumeTarget.Pick(100,
            new[] { new CampaignAttemptEntry(12, 500, 3, WinnerIndex: 0) });
        Assert.True(pick!.Value.IsAutosave);
    }

    [Fact]
    public void ResumePick_NewestWins()
    {
        var entries = new[]
        {
            new CampaignAttemptEntry(1, 300, 3, null),
            new CampaignAttemptEntry(2, 900, 3, null),
            new CampaignAttemptEntry(3, 600, 3, null),
        };
        Assert.Equal(2, ResumeTarget.Pick(700, entries)!.Value.CampaignLevel);
        Assert.True(ResumeTarget.Pick(1000, entries)!.Value.IsAutosave);
    }

    [Fact]
    public void ResumePick_TieGoesToAutosave() =>
        Assert.True(ResumeTarget.Pick(900,
            new[] { new CampaignAttemptEntry(2, 900, 3, null) })!.Value.IsAutosave);
}
