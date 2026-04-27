using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Round-trip tests for <see cref="SaveSerializer"/>. The save format
/// must capture every gameplay-relevant bit of state — grid colors,
/// every occupant type, treasury balances, territory partition with
/// capitals, the player roster (including AI kind), turn/player
/// indices, master seed, and max-turn cap — and reproduce them
/// exactly on deserialize.
/// </summary>
public class SaveSerializerTests
{
    /// <summary>
    /// Build a non-trivial test state: 4x3 grid with two territories
    /// (Red and Blue), every occupant type present (Unit at multiple
    /// levels, Capital, Tower, Tree, Grave), partial gold, mid-game
    /// turn state.
    /// </summary>
    private static (GameState, IReadOnlyList<Player>) BuildRichState()
    {
        var red = new Player("Red", new Color("e53935"), AiKind.Human);
        var blue = new Player("Blue", new Color("1e88e5"), AiKind.Heuristic);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, blue.Color);
        // Red owns a 2x2 block in the top-left.
        grid.Get(HexCoord.FromOffset(0, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(1, 0))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(0, 1))!.Color = red.Color;
        grid.Get(HexCoord.FromOffset(1, 1))!.Color = red.Color;

        // Place every occupant type so the serializer has to handle
        // each branch.
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(red.Color, UnitLevel.Knight) { HasMovedThisTurn = true };
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(red.Color, UnitLevel.Peasant) { HasMovedThisTurn = false };
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Grave();
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Tower();
        grid.Get(HexCoord.FromOffset(3, 1))!.Occupant = new Unit(blue.Color, UnitLevel.Baron);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        // Advance turn state: it's Blue's turn 5.
        var turnState = new TurnState(players, currentPlayerIndex: 1, turnNumber: 5);

        var treasury = new Treasury();
        foreach (Territory t in territories)
        {
            if (t.HasCapital) treasury.SetGold(t.Capital!.Value, t.Owner == red.Color ? 17 : 42);
        }

        var state = new GameState(grid, territories, players, turnState, treasury);
        return (state, players);
    }

    [Fact]
    public void Serialize_ThenDeserialize_PreservesAllGameState()
    {
        (GameState original, IReadOnlyList<Player> originalPlayers) = BuildRichState();
        const int seed = 99887766;
        const int maxTurns = 1234;

        string json = SaveSerializer.Serialize(
            original, seed, originalPlayers, slotName: "test_slot", maxTurnNumber: maxTurns);

        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal("test_slot", loaded.SlotName);
        Assert.Equal(seed, loaded.MasterSeed);
        Assert.Equal(maxTurns, loaded.MaxTurnNumber);
        Assert.Equal(original.Turns.TurnNumber, loaded.State.Turns.TurnNumber);
        Assert.Equal(original.Turns.CurrentPlayerIndex, loaded.State.Turns.CurrentPlayerIndex);

        // Players: name, color, kind preserved.
        Assert.Equal(originalPlayers.Count, loaded.Players.Count);
        for (int i = 0; i < originalPlayers.Count; i++)
        {
            Assert.Equal(originalPlayers[i].Name, loaded.Players[i].Name);
            Assert.Equal(originalPlayers[i].Color, loaded.Players[i].Color);
            Assert.Equal(originalPlayers[i].Kind, loaded.Players[i].Kind);
        }

        // Tiles: every coord present, color and occupant preserved.
        foreach (HexTile origTile in original.Grid.Tiles)
        {
            HexTile? loadedTile = loaded.State.Grid.Get(origTile.Coord);
            Assert.NotNull(loadedTile);
            Assert.Equal(origTile.Color, loadedTile!.Color);
            AssertOccupantsEqual(origTile.Occupant, loadedTile.Occupant);
        }
        // No extra tiles either.
        int origCount = 0;
        foreach (HexTile _ in original.Grid.Tiles) origCount++;
        int loadedCount = 0;
        foreach (HexTile _ in loaded.State.Grid.Tiles) loadedCount++;
        Assert.Equal(origCount, loadedCount);

        // Territories: same partition (count and owner-by-coord).
        Assert.Equal(original.Territories.Count, loaded.State.Territories.Count);
        Dictionary<HexCoord, Territory> origIndex = original.Territories.BuildTileIndex();
        Dictionary<HexCoord, Territory> loadedIndex = loaded.State.Territories.BuildTileIndex();
        Assert.Equal(origIndex.Count, loadedIndex.Count);
        foreach (KeyValuePair<HexCoord, Territory> kvp in origIndex)
        {
            Assert.True(loadedIndex.ContainsKey(kvp.Key),
                $"loaded index missing coord {kvp.Key}");
            Assert.Equal(kvp.Value.Owner, loadedIndex[kvp.Key].Owner);
            Assert.Equal(kvp.Value.Capital, loadedIndex[kvp.Key].Capital);
        }

        // Treasury gold preserved per capital.
        foreach (Territory origT in original.Territories)
        {
            if (!origT.HasCapital) continue;
            HexCoord cap = origT.Capital!.Value;
            Assert.Equal(
                original.Treasury.GetGold(cap),
                loaded.State.Treasury.GetGold(cap));
        }
    }

    [Fact]
    public void Deserialize_RejectsMismatchedFormatVersion()
    {
        // A save written by a future or past version of the game must
        // be rejected with a clear error rather than silently loading
        // partial state.
        string json = "{\"FormatVersion\":99999,\"SlotName\":\"x\"}";
        Assert.ThrowsAny<System.Exception>(() => SaveSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_PreservesMovedFlagOnUnits()
    {
        // The HasMovedThisTurn flag is gameplay-critical (a saved
        // mid-turn state with already-moved units must not let the
        // unit move again on reload). Round-trip both true and false.
        (GameState original, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.Serialize(original, 0, players, "x", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        HexTile? movedTile = loaded.State.Grid.Get(HexCoord.FromOffset(0, 0));
        Assert.NotNull(movedTile?.Unit);
        Assert.True(movedTile!.Unit!.HasMovedThisTurn);

        HexTile? unmovedTile = loaded.State.Grid.Get(HexCoord.FromOffset(1, 0));
        Assert.NotNull(unmovedTile?.Unit);
        Assert.False(unmovedTile!.Unit!.HasMovedThisTurn);
    }

    private static void AssertOccupantsEqual(HexOccupant? a, HexOccupant? b)
    {
        if (a == null && b == null) return;
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.GetType(), b!.GetType());
        if (a is Unit ua && b is Unit ub)
        {
            Assert.Equal(ua.Owner, ub.Owner);
            Assert.Equal(ua.Level, ub.Level);
            Assert.Equal(ua.HasMovedThisTurn, ub.HasMovedThisTurn);
        }
    }
}
