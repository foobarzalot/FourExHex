using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Round-trip tests for the new <see cref="ReplayDisplayTextBeat"/>
/// tutorial-only beat through <see cref="SaveSerializer"/>'s tutorial
/// embedding (the same pipeline used by <c>user://tutorials/</c>).
/// </summary>
public class ReplayDisplayTextBeatSerializerTests
{
    private static (GameState, IReadOnlyList<Player>) BuildMinimalState()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), AiKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turnState = new TurnState(players, currentPlayerIndex: 0, turnNumber: 0);
        var state = new GameState(grid, territories, players, turnState, new Treasury());
        return (state, players);
    }

    [Fact]
    public void DisplayTextBeat_RoundTripsTextAndActorMinusOne()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);

        var beats = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            new ReplayDisplayTextBeat
            {
                Index = 1, Turn = 1, Actor = -1,
                Text = "Hello, narration!",
            },
            new ReplayEndTurnBeat { Index = 2, Turn = 1, Actor = 0 },
        };
        var replay = new Replay(snapshot, 1, 0, beats);
        var tutorial = new Tutorial { Title = "T", Replay = replay };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Tutorial);
        Assert.NotNull(loaded.Tutorial!.Replay);
        Assert.Equal(3, loaded.Tutorial.Replay.Beats.Count);

        ReplayBeat second = loaded.Tutorial.Replay.Beats[1];
        var displayText = Assert.IsType<ReplayDisplayTextBeat>(second);
        Assert.Equal("Hello, narration!", displayText.Text);
        Assert.Equal(-1, displayText.Actor);
        Assert.Equal(1, displayText.Index);
        Assert.Equal(1, displayText.Turn);
    }

    [Fact]
    public void DisplayTextBeat_EmptyTextRoundTrips()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);

        var beats = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "" },
        };
        var tutorial = new Tutorial
        {
            Title = "Empty",
            Replay = new Replay(snapshot, 1, 0, beats),
        };

        string json = SaveSerializer.SerializeMap(state, masterSeed: 1, players, "m", tutorial);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        var displayText = Assert.IsType<ReplayDisplayTextBeat>(
            loaded.Tutorial!.Replay!.Beats[0]);
        Assert.Equal("", displayText.Text);
    }
}
