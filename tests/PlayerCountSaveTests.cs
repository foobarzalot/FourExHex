using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Save/load for variable-player-count games and starting maps that bake
/// per-color kinds, including the <see cref="PlayerKind.None"/> slot (issue #70).
/// </summary>
public class PlayerCountSaveTests
{
    // A 3-player in-progress game whose roster occupies slots 0, 2, 4
    // (1, 3, 5 are absent). Each present slot owns at least one tile.
    private static (GameState, IReadOnlyList<Player>) BuildSparseRosterState()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human, Difficulty.Captain);
        var green = new Player("Green", PlayerId.FromIndex(2), PlayerKind.Computer);
        var purple = new Player("Purple", PlayerId.FromIndex(4), PlayerKind.Computer);
        var players = new List<Player> { red, green, purple };

        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, green.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(3, 2))!.Owner = purple.Id;
        IReadOnlyList<Territory> terr = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turn = new TurnState(players, currentPlayerIndex: 0, turnNumber: 5);
        return (new GameState(grid, terr, players, turn, new Treasury()), players);
    }

    [Fact]
    public void SparseRoster_RoundTripsOwnersAndSlotIndices()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildSparseRosterState();
        string json = SaveSerializer.Serialize(s, 7, p, "slot", 100);

        LoadedSave loaded = SaveSerializer.Deserialize(json);

        // Owners resolve back through the gap in the roster (slot != list pos).
        Assert.Equal(GameStateChecksum.Compute(s), GameStateChecksum.Compute(loaded.State));
        Assert.Equal(new[] { 0, 2, 4 }, loaded.Players.Select(pl => pl.Id.Index).ToArray());
        Assert.Equal(Difficulty.Captain, loaded.Players[0].Difficulty);
        // The serialized color hex tracks the slot, not the list position.
        Assert.Contains($"\"ColorHex\": \"{GameSettings.PlayerConfig[4].Hex}\"", json);
    }

    // A starting map painted with colors 0, 2, 4; slots 1, 3, 5 are None.
    private static (GameState, IReadOnlyList<Player>) BuildBakedMapState()
    {
        var players = new List<Player>
        {
            new("Red", PlayerId.FromIndex(0), PlayerKind.Human, Difficulty.Captain),
            new("Blue", PlayerId.FromIndex(1), PlayerKind.None),
            new("Green", PlayerId.FromIndex(2), PlayerKind.Computer),
            new("Brown", PlayerId.FromIndex(3), PlayerKind.None),
            new("Purple", PlayerId.FromIndex(4), PlayerKind.Computer),
            new("Orange", PlayerId.FromIndex(5), PlayerKind.None),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, PlayerId.FromIndex(2));
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = PlayerId.FromIndex(0);
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = PlayerId.FromIndex(0);
        grid.Get(HexCoord.FromOffset(3, 2))!.Owner = PlayerId.FromIndex(4);
        IReadOnlyList<Territory> terr = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turn = new TurnState(players, currentPlayerIndex: 0, turnNumber: 0);
        return (new GameState(grid, terr, players, turn, new Treasury()), players);
    }

    [Fact]
    public void Map_BakesKindsAndDifficulty_InclNone_AndLoadExcludesNone()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildBakedMapState();
        string json = SaveSerializer.SerializeMap(s, 42, p, "m");

        // The map records every color's kind, including None.
        Assert.Contains("\"Kind\": \"None\"", json);
        Assert.Contains("\"Kind\": \"Human\"", json);

        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.True(loaded.MapHasBakedKinds);
        Assert.Equal(new[] { 0, 2, 4 }, loaded.Players.Select(pl => pl.Id.Index).ToArray());
        Assert.DoesNotContain(loaded.Players, pl => pl.Kind == PlayerKind.None);
        Assert.Equal(PlayerKind.Human, loaded.Players[0].Kind);
        Assert.Equal(Difficulty.Captain, loaded.Players[0].Difficulty);
    }

    [Fact]
    public void OldMap_NoBakedKinds_IsDetected()
    {
        // Simulate a pre-#70 starting map by stripping the baked Kind/Difficulty
        // fields. MapHasBakedKinds must read false (the load path then applies
        // the legacy default roster — exercised in the scene layer).
        (GameState s, IReadOnlyList<Player> p) = BuildBakedMapState();
        string json = SaveSerializer.SerializeMap(s, 42, p, "m");
        string legacy = System.Text.RegularExpressions.Regex.Replace(
            json, ",\\s*\"Kind\": \"[A-Za-z]+\"", string.Empty);
        legacy = System.Text.RegularExpressions.Regex.Replace(
            legacy, ",\\s*\"Difficulty\": \"[A-Za-z]+\"", string.Empty);

        LoadedSave loaded = SaveSerializer.Deserialize(legacy);

        Assert.False(loaded.MapHasBakedKinds);
    }
}
