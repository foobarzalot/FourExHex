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
    public void CurrentFormatVersion_IsFour()
    {
        // Bumped to 4 when the replay-recording feature added the
        // optional `Replay` block. Older saves (v2/v3) still load.
        Assert.Equal(4, SaveSerializer.CurrentFormatVersion);
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
        string v2Json = json.Replace("\"FormatVersion\": 4", "\"FormatVersion\": 2");

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
        string v2Json = json.Replace("\"FormatVersion\": 4", "\"FormatVersion\": 2");

        LoadedSave loaded = SaveSerializer.Deserialize(v2Json);

        Assert.Null(loaded.Tutorial);
    }

    [Fact]
    public void SerializeMap_RoundTripsTutorialWithSingleEndTurnBeat()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        var tutorial = new Tutorial
        {
            Title = "EndTurn smoke",
            Beats = new List<Beat>
            {
                new EndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            },
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Tutorial);
        Assert.Single(loaded.Tutorial!.Beats);
        Beat beat = loaded.Tutorial.Beats[0];
        Assert.IsType<EndTurnBeat>(beat);
        Assert.Equal(0, beat.Index);
        Assert.Equal(1, beat.Turn);
        Assert.Equal(0, beat.Actor);
        Assert.Equal(BeatKind.EndTurn, beat.Kind);
    }

    [Fact]
    public void SerializeMap_RoundTripsMultipleEndTurnBeats()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        var tutorial = new Tutorial
        {
            Beats = new List<Beat>
            {
                new EndTurnBeat { Index = 0, Turn = 1, Actor = 0, Narration = "first" },
                new EndTurnBeat { Index = 1, Turn = 1, Actor = 1 },
                new EndTurnBeat { Index = 2, Turn = 2, Actor = 0 },
            },
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Tutorial);
        Assert.Equal(3, loaded.Tutorial!.Beats.Count);
        Assert.Equal("first", loaded.Tutorial.Beats[0].Narration);
        Assert.Null(loaded.Tutorial.Beats[1].Narration);
        Assert.Equal(2, loaded.Tutorial.Beats[2].Turn);
    }

    [Fact]
    public void Deserialize_TutorialWithoutBeatsField_GivesEmptyBeats()
    {
        // 3a-shape JSON: TutorialDto without a "Beats" field at all.
        // Synthesized by serializing an empty-Beats Tutorial — which the
        // serializer omits via WhenWritingNull — so the JSON has no
        // "Beats" property under "Tutorial".
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", new Tutorial());

        Assert.DoesNotContain("\"Beats\"", json);

        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.NotNull(loaded.Tutorial);
        Assert.Empty(loaded.Tutorial!.Beats);
    }

    [Fact]
    public void SerializeMap_RoundTripsTutorialWithBuyPeasantBeat()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        var tutorial = new Tutorial
        {
            Title = "BuyPeasant smoke",
            Beats = new List<Beat>
            {
                new BuyPeasantBeat
                {
                    Index = 0,
                    Turn = 1,
                    Actor = 0,
                    At = new HexCoord(3, 5),
                },
            },
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Tutorial);
        Assert.Single(loaded.Tutorial!.Beats);
        Beat beat = loaded.Tutorial.Beats[0];
        BuyPeasantBeat bpb = Assert.IsType<BuyPeasantBeat>(beat);
        Assert.Equal(0, bpb.Index);
        Assert.Equal(1, bpb.Turn);
        Assert.Equal(0, bpb.Actor);
        Assert.Equal(BeatKind.BuyPeasant, bpb.Kind);
        Assert.Equal(new HexCoord(3, 5), bpb.At);
    }
}
