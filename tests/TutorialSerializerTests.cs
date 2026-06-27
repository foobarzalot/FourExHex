using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Format-bump regression tests + tutorial round-trip for the v4
/// schema. The v3 Tutorial-Beats system was torn down; tutorials are
/// now <c>{ Title, Replay }</c> with the Replay block carrying every
/// recorded action. v2/v3 saves still load (both Tutorial and Replay
/// null on those).
/// </summary>
public class TutorialSerializerTests
{
    private static (GameState, IReadOnlyList<Player>) BuildMinimalState()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turnState = new TurnState(players, currentPlayerIndex: 0, turnNumber: 0);
        var state = new GameState(grid, territories, players, turnState, new Treasury());
        return (state, players);
    }

    [Fact]
    public void CurrentFormatVersion_IsFourteen()
    {
        Assert.Equal(14, SaveSerializer.CurrentFormatVersion);
    }

    [Fact]
    public void Deserialize_AcceptsLegacyV2Json()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m");
        string v2Json = json.Replace(
            $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}",
            "\"FormatVersion\": 2");

        LoadedSave loaded = SaveSerializer.Deserialize(v2Json);

        Assert.Equal(7, loaded.MasterSeed);
        Assert.Null(loaded.Tutorial);
        Assert.Null(loaded.Replay);
    }

    [Fact]
    public void SerializeMap_WithoutTutorial_DeserializesToNullTutorial()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m");
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Null(loaded.Tutorial);
    }

    [Fact]
    public void SerializeMap_WithTutorialAndReplay_RoundTripsTitleAndReplay()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);
        var beats = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var replay = new Replay(snapshot, 1, 0, beats);
        var tutorial = new Tutorial { Title = "Intro", Replay = replay };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Tutorial);
        Assert.Equal("Intro", loaded.Tutorial!.Title);
        Assert.NotNull(loaded.Tutorial.Replay);
        Assert.Single(loaded.Tutorial.Replay.Beats);
        Assert.Equal(1, loaded.Tutorial.Replay.InitialTurnNumber);
        Assert.Equal(0, loaded.Tutorial.Replay.InitialCurrentPlayerIndex);
    }

    [Fact]
    public void Deserialize_TutorialBlockWithoutReplayBlock_Throws()
    {
        // Hand-craft a malformed JSON: Tutorial { Title = "X" } but no
        // Replay block. Deserialize must reject it — a tutorial without
        // a replay is meaningless under the new schema.
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m",
            new Tutorial { Title = "X", Replay = new Replay(
                GameStateSnapshot.Capture(state.Grid, state.Treasury, state.Territories),
                0, 0, new List<ReplayBeat>()) });

        // Strip the Replay block to simulate a malformed file.
        int replayStart = json.IndexOf("\"Replay\":");
        int replayBlockStart = json.LastIndexOf(',', replayStart);
        int replayBlockEnd = json.IndexOf('}', replayStart);
        // Walk forward through nested braces to find the matching close.
        int depth = 0;
        for (int i = replayStart; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0) { replayBlockEnd = i; break; }
            }
        }
        string malformed = json.Substring(0, replayBlockStart) + json.Substring(replayBlockEnd + 1);

        Assert.Throws<System.InvalidOperationException>(() =>
            SaveSerializer.Deserialize(malformed));
    }
}
