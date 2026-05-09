using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for the v2 → v3 format bump and the optional <c>Tutorial</c>
/// block that JSON v3 introduced (TutorialBuilder Phase 3a).
/// Existing v2-format save round-tripping is covered by
/// <see cref="SaveSerializerTests"/>; this class focuses on the v3
/// additions and the dual-version read path.
/// </summary>
public class TutorialSerializerTests
{
    private static (GameState, IReadOnlyList<Player>) BuildMinimalState()
    {
        var red = new Player("Red", new Color("e53935"), AiKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Color);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turnState = new TurnState(players, currentPlayerIndex: 0, turnNumber: 0);
        var state = new GameState(grid, territories, players, turnState, new Treasury());
        return (state, players);
    }

    [Fact]
    public void CurrentFormatVersion_IsThree()
    {
        Assert.Equal(3, SaveSerializer.CurrentFormatVersion);
    }

    [Fact]
    public void Deserialize_AcceptsLegacyV2Json()
    {
        // Synthesize a "v2" JSON by serializing at v3 and rewriting the
        // version field. v2 → v3 is purely additive (the only difference
        // is the optional Tutorial block, which v2 files lack); the v3
        // deserializer must accept v2 input unchanged so existing
        // user://saves/ autosaves keep loading after the bump.
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m");
        string v2Json = json.Replace("\"FormatVersion\": 3", "\"FormatVersion\": 2");

        LoadedSave loaded = SaveSerializer.Deserialize(v2Json);

        Assert.Equal(7, loaded.MasterSeed);
    }

    [Fact]
    public void SerializeMap_RoundTripsEmptyTutorial()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        var tutorial = new Tutorial
        {
            Title = "Intro · The Basics",
            StartTurn = 1,
            StartPlayer = 0,
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Tutorial);
        Assert.Equal("Intro · The Basics", loaded.Tutorial!.Title);
        Assert.Equal(1, loaded.Tutorial.StartTurn);
        Assert.Equal(0, loaded.Tutorial.StartPlayer);
    }

    [Fact]
    public void SerializeMap_WithoutTutorial_DeserializesToNull()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m");
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Null(loaded.Tutorial);
    }

    [Fact]
    public void Deserialize_LegacyV2Json_HasNullTutorial()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m");
        string v2Json = json.Replace("\"FormatVersion\": 3", "\"FormatVersion\": 2");

        LoadedSave loaded = SaveSerializer.Deserialize(v2Json);

        Assert.Null(loaded.Tutorial);
    }
}
