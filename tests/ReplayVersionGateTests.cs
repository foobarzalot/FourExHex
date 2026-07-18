// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The replay-version gate: replays are only replayed by the RNG/rules
/// generation that recorded them. A save carries
/// <see cref="SaveSerializer.CurrentReplayVersion"/> next to its Replay
/// block; on load, a replay whose version doesn't match is dropped
/// (the game state itself loads normally — the board is baked into the
/// save, so nothing else is lost). Tutorial saves are the exception:
/// their replay IS the content, so a stale-version tutorial fails
/// loudly instead of silently degrading.
/// </summary>
public class ReplayVersionGateTests
{
    private static string SerializeGameWithReplay(string? tutorialTitle = null)
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(4, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var treasury = new Treasury();
        var state = new GameState(grid, territories, players, new TurnState(players), treasury);

        GameStateSnapshot snapshot = GameStateSnapshot.Capture(grid, treasury, territories);
        var replay = new Replay(snapshot, initialTurnNumber: 1,
            initialCurrentPlayerIndex: 0,
            beats: new List<ReplayBeat> { new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 } });
        Tutorial? tutorial = tutorialTitle == null
            ? null
            : new Tutorial { Title = tutorialTitle, Replay = replay };
        return SaveSerializer.Serialize(state, masterSeed: 42, players,
            "gate-test", maxTurnNumber: 100,
            tutorial: tutorial,
            replay: tutorial == null ? replay : null);
    }

    private static string StripReplayVersion(string json)
    {
        string stripped = Regex.Replace(json, "\"ReplayVersion\"\\s*:\\s*\\d+\\s*,?", "");
        Assert.NotEqual(json, stripped); // the field must have been present
        return stripped;
    }

    [Fact]
    public void Serialize_StampsCurrentReplayVersion_AndRoundTripsReplay()
    {
        string json = SerializeGameWithReplay();
        Assert.Contains($"\"ReplayVersion\": {SaveSerializer.CurrentReplayVersion}", json);
        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.NotNull(loaded.Replay);
        Assert.Single(loaded.Replay!.Beats);
    }

    [Fact]
    public void Load_ReplayWithoutVersionMarker_DropsReplayButKeepsState()
    {
        string stale = StripReplayVersion(SerializeGameWithReplay());
        LoadedSave loaded = SaveSerializer.Deserialize(stale);
        Assert.Null(loaded.Replay);
        // The game state itself is untouched by the drop.
        Assert.Equal(8, System.Linq.Enumerable.Count(loaded.State.Grid.Tiles));
        Assert.Equal(42, loaded.MasterSeed);
    }

    [Fact]
    public void Load_ReplayWithWrongVersion_DropsReplay()
    {
        string json = SerializeGameWithReplay();
        string wrong = json.Replace(
            $"\"ReplayVersion\": {SaveSerializer.CurrentReplayVersion}",
            $"\"ReplayVersion\": {SaveSerializer.CurrentReplayVersion + 1}");
        Assert.NotEqual(json, wrong);
        LoadedSave loaded = SaveSerializer.Deserialize(wrong);
        Assert.Null(loaded.Replay);
    }

    [Fact]
    public void Load_StaleTutorial_FailsLoudly()
    {
        string stale = StripReplayVersion(SerializeGameWithReplay("Stale"));
        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => SaveSerializer.Deserialize(stale));
        Assert.Contains("replay version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
