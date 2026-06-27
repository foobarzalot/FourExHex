using System.Collections.Generic;
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
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, blue.Id);
        // Red owns a 2x2 block in the top-left.
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;

        // Place every occupant type so the serializer has to handle
        // each branch.
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(red.Id, UnitLevel.Captain) { HasMovedThisTurn = true };
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(red.Id, UnitLevel.Recruit) { HasMovedThisTurn = false };
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(3, 0))!.Occupant = new Grave();
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Tower();
        grid.Get(HexCoord.FromOffset(3, 1))!.Occupant = new Unit(blue.Id, UnitLevel.Commander);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        // Advance turn state: it's Blue's turn 5.
        var turnState = new TurnState(players, currentPlayerIndex: 1, turnNumber: 5);

        var treasury = new Treasury();
        foreach (Territory t in territories)
        {
            if (t.HasCapital) treasury.SetGold(t.Capital!.Value, t.Owner == red.Id ? 17 : 42);
        }

        var state = new GameState(grid, territories, players, turnState, treasury);
        return (state, players);
    }

    [Fact]
    public void SerializeMap_BakesKindFieldIntoJson()
    {
        // Since #70 starting maps record each color's role (Human/Computer/None)
        // so a loaded map restores its exact roster.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.SerializeMap(state, 42, players, "m");

        Assert.Contains("\"Kind\": \"Human\"", json);
        Assert.Contains("\"Kind\": \"Computer\"", json);
    }

    [Fact]
    public void SerializeMap_RoundTripPreservesNamesAndColors()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.SerializeMap(state, 42, players, "m");
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(players.Count, loaded.Players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            Assert.Equal(players[i].Name, loaded.Players[i].Name);
            Assert.Equal(players[i].Id, loaded.Players[i].Id);
        }
    }

    [Fact]
    public void SerializeMap_RoundTripsRisingTidesMode()
    {
        // The serialization layer carries a starting map's game mode end to
        // end: SerializeMap → Deserialize preserves it. The editor authoring
        // path (MapEditorPanel.BuildSaveState ← MapEditorScene._mapMode) and
        // Main's starting-map load both thread the mode through (issue #56), so
        // this guards the format they rely on.
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        // Turn 0 = the on-disk "starting map" marker (see BuildSaveState).
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury(), waterCoords: null, mode: GameMode.RisingTides);

        string json = SaveSerializer.SerializeMap(state, 42, players, "m");
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(GameMode.RisingTides, loaded.State.Mode);
    }

    [Fact]
    public void Deserialize_PlayerWithMissingKind_DefaultsToHuman()
    {
        // A pre-#70 starting map's JSON has no "Kind" field per player. The
        // loader must not throw — it treats missing kind as Human (the legacy
        // placeholder; the play-scene load path then applies the default roster).
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.SerializeMap(state, 42, players, "m");
        // Strip the baked Kind fields to simulate the pre-#70 file shape.
        string legacy = System.Text.RegularExpressions.Regex.Replace(
            json, ",\\s*\"Kind\": \"[A-Za-z]+\"", string.Empty);

        LoadedSave loaded = SaveSerializer.Deserialize(legacy);

        Assert.False(loaded.MapHasBakedKinds);
        foreach (Player p in loaded.Players)
        {
            Assert.Equal(PlayerKind.Human, p.Kind);
        }
    }

    [Fact]
    public void Serialize_StillIncludesKindForInProgressSaves()
    {
        // Regression guard: regular Serialize (used for play-scene saves)
        // must keep recording each player's role so resume works.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);

        Assert.Contains("\"Kind\"", json);
    }

    [Fact]
    public void CurrentFormatVersion_IsFourteen()
    {
        Assert.Equal(14, SaveSerializer.CurrentFormatVersion);
    }

    [Fact]
    public void Serialize_RoundTripsPendingTideForecast()
    {
        // Issue #85: a mid-turn save must preserve the locked tide forecast so the
        // reloaded game telegraphs the same doomed tile and submerges exactly it.
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.RisingTides);
        state.PendingTide = new List<TideStep>
        {
            new TideStep(HexCoord.FromOffset(0, 0), DemoteOnly: false),
            new TideStep(HexCoord.FromOffset(2, 1), DemoteOnly: true),
        };

        string json = SaveSerializer.Serialize(state, 42, players, "tide", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(state.PendingTide, loaded.State.PendingTide);
    }

    [Fact]
    public void Deserialize_PreV14Save_DefaultsPendingTideToEmpty()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury());

        // A save with no PendingTide field (pre-#85) must load as an empty forecast.
        string json = SaveSerializer.Serialize(state, 42, players, "f", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Empty(loaded.State.PendingTide);
    }

    [Fact]
    public void Serialize_RoundTripsRisingTidesModeAndGrownWater()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(),
            waterCoords: null, mode: GameMode.RisingTides);
        // Simulate flood progress: a shore tile has submerged mid-game.
        HexCoord drowned = HexCoord.FromOffset(0, 0);
        state.Grid.Remove(drowned);
        state.AddWater(drowned);

        string json = SaveSerializer.Serialize(state, 42, players, "tide", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(GameMode.RisingTides, loaded.State.Mode);
        Assert.Contains(drowned, loaded.State.WaterCoords);
        Assert.False(loaded.State.Grid.Contains(drowned));
    }

    [Fact]
    public void Deserialize_PreV12Save_DefaultsModeToFreeform()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury());

        // A plain freeform save omits the Mode field; rewind the version to
        // simulate a pre-#56 file.
        string json = SaveSerializer.Serialize(state, 42, players, "f", 100)
            .Replace("\"FormatVersion\": 12", "\"FormatVersion\": 11");
        Assert.DoesNotContain("\"Mode\"", json); // Freeform omits the field

        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(GameMode.Freeform, loaded.State.Mode);
    }

    [Fact]
    public void Serialize_RoundTripPreservesGoldTiles()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        // Mark a couple of tiles gold (issue #45). One empty, one occupied,
        // to prove gold is orthogonal to the occupant.
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.IsGold = true;   // empty Red tile
        state.Grid.Get(HexCoord.FromOffset(2, 1))!.IsGold = true;   // Tower tile

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        foreach (HexTile orig in state.Grid.Tiles)
        {
            HexTile? loadedTile = loaded.State.Grid.Get(orig.Coord);
            Assert.NotNull(loadedTile);
            Assert.Equal(orig.IsGold, loadedTile!.IsGold);
        }
        Assert.True(loaded.State.Grid.Get(HexCoord.FromOffset(1, 1))!.IsGold);
        Assert.True(loaded.State.Grid.Get(HexCoord.FromOffset(2, 1))!.IsGold);
    }

    [Fact]
    public void Deserialize_PreV9Save_DefaultsGoldToFalse()
    {
        // A pre-gold save (v8) has no IsGold field on any tile. The loader
        // must accept it and default every tile to non-gold.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.IsGold = true;
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);

        // Strip the new field (always the last tile property, so consume the
        // preceding comma to keep the JSON valid) and rewind the version to
        // simulate an old file.
        string legacy = System.Text.RegularExpressions.Regex
            .Replace(json, ",\\s*\"IsGold\": (true|false)", string.Empty)
            .Replace("\"FormatVersion\": 9", "\"FormatVersion\": 8");

        LoadedSave loaded = SaveSerializer.Deserialize(legacy);

        foreach (HexTile loadedTile in loaded.State.Grid.Tiles)
        {
            Assert.False(loadedTile.IsGold);
        }
    }

    [Fact]
    public void Serialize_RoundTripPreservesMountainTiles()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        // Mark tiles mountain (issue #37). Gold and mountain are now mutually
        // exclusive (issue #81), so a mountain tile is never also gold.
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.IsMountain = true;
        HexTile mountain = state.Grid.Get(HexCoord.FromOffset(2, 1))!;
        mountain.IsMountain = true;

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        foreach (HexTile orig in state.Grid.Tiles)
        {
            HexTile? loadedTile = loaded.State.Grid.Get(orig.Coord);
            Assert.NotNull(loadedTile);
            Assert.Equal(orig.IsMountain, loadedTile!.IsMountain);
            Assert.Equal(orig.IsGold, loadedTile.IsGold);
        }
        HexTile loadedMountain = loaded.State.Grid.Get(HexCoord.FromOffset(2, 1))!;
        Assert.True(loadedMountain.IsMountain);
        Assert.False(loadedMountain.IsGold);
    }

    [Fact]
    public void Deserialize_LegacyGoldAndMountainTile_NormalizesToMountain()
    {
        // A pre-#81 save could legally encode a tile with both IsGold and
        // IsMountain set (the flags were independent). Under the new mutual
        // exclusion the loader must normalize such a tile to mountain-only
        // (mountain wins, issue #81).
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        // Author a single mountain tile, then forge the now-illegal combo
        // straight into the JSON (the model can no longer represent both).
        var coord = HexCoord.FromOffset(2, 1);
        state.Grid.Get(coord)!.IsMountain = true;
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);

        // The mountain tile serializes as `"IsGold": false, ... "IsMountain": true`.
        // Flip that IsGold to true to simulate the legacy both-flags encoding
        // (the only tile with IsMountain:true here).
        string forged = System.Text.RegularExpressions.Regex.Replace(
            json,
            "\"IsGold\": false,(\\s*)\"IsMountain\": true",
            "\"IsGold\": true,$1\"IsMountain\": true");
        Assert.NotEqual(json, forged); // sanity: the substitution actually fired

        LoadedSave loaded = SaveSerializer.Deserialize(forged);

        HexTile loadedTile = loaded.State.Grid.Get(coord)!;
        Assert.True(loadedTile.IsMountain);
        Assert.False(loadedTile.IsGold);   // gold dropped — mountain wins
        Assert.Equal(TerrainFeature.Mountain, loadedTile.Feature);
    }

    [Fact]
    public void Deserialize_PreV10Save_DefaultsMountainToFalse()
    {
        // A pre-mountain save (v9) has no IsMountain field on any tile. The
        // loader must accept it and default every tile to non-mountain.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        state.Grid.Get(HexCoord.FromOffset(1, 1))!.IsMountain = true;
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);

        // Strip the new field (always the last tile property, so consume the
        // preceding comma to keep the JSON valid) and rewind the version.
        string legacy = System.Text.RegularExpressions.Regex
            .Replace(json, ",\\s*\"IsMountain\": (true|false)", string.Empty)
            .Replace("\"FormatVersion\": 10", "\"FormatVersion\": 9");

        LoadedSave loaded = SaveSerializer.Deserialize(legacy);

        foreach (HexTile loadedTile in loaded.State.Grid.Tiles)
        {
            Assert.False(loadedTile.IsMountain);
        }
    }

    [Fact]
    public void Serialize_RoundTripsPlayerDifficulty()
    {
        // Per-AI difficulty must survive an in-progress save so a reloaded
        // game keeps each opponent earning at its configured rate.
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human, Difficulty.Soldier);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer, Difficulty.Commander);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, blue.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());

        string json = SaveSerializer.Serialize(state, 42, players, "d", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(Difficulty.Soldier, loaded.Players[0].Difficulty);
        Assert.Equal(Difficulty.Commander, loaded.Players[1].Difficulty);
    }

    [Fact]
    public void SerializeMap_BakesDifficultyFieldIntoJson()
    {
        // Since #70 starting maps bake per-color difficulty alongside kind, so
        // a loaded map restores its exact roster.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.SerializeMap(state, 42, players, "m");

        Assert.Contains("\"Difficulty\": \"Soldier\"", json);
    }

    [Fact]
    public void Deserialize_PlayerWithMissingDifficulty_DefaultsToSoldier()
    {
        // A starting map (and any pre-v7 save) has no "Difficulty" field;
        // the loader must default each player to Soldier.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.SerializeMap(state, 42, players, "m");

        LoadedSave loaded = SaveSerializer.Deserialize(json);

        foreach (Player p in loaded.Players)
        {
            Assert.Equal(Difficulty.Soldier, p.Difficulty);
        }
    }

    [Fact]
    public void Serialize_RoundTripsWaterCoords()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var water = new HashSet<HexCoord>
        {
            HexCoord.FromOffset(5, 5),
            HexCoord.FromOffset(7, 8),
            HexCoord.FromOffset(0, 9),
        };
        var state = new GameState(
            grid, territories, players, new TurnState(players), new Treasury(), water);

        string json = SaveSerializer.Serialize(state, 42, players, "w", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(water.Count, loaded.State.WaterCoords.Count);
        foreach (HexCoord c in water)
        {
            Assert.Contains(c, loaded.State.WaterCoords);
        }
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
            Assert.Equal(originalPlayers[i].Id, loaded.Players[i].Id);
            Assert.Equal(originalPlayers[i].Kind, loaded.Players[i].Kind);
        }

        // Tiles: every coord present, color and occupant preserved.
        foreach (HexTile origTile in original.Grid.Tiles)
        {
            HexTile? loadedTile = loaded.State.Grid.Get(origTile.Coord);
            Assert.NotNull(loadedTile);
            Assert.Equal(origTile.Owner, loadedTile!.Owner);
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
    public void Deserialize_LegacyUnitLevelNames_MapToRenamedLevels()
    {
        // Saves written before the unit rename stored the old level
        // names (Peasant/Spearman/Knight/Baron). They must still load,
        // mapping onto the current names (Recruit/Soldier/Captain/
        // Commander) — units keep their level, just under the new name.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.Serialize(state, 42, players, "legacy", 100);

        // Forge a pre-rename file: old level strings, old format version.
        // Strip the per-player Difficulty fields first — they're a v7
        // addition a genuine v5 file wouldn't have, and their values share
        // names with unit levels so the blanket replaces below would
        // otherwise corrupt them.
        string legacy = System.Text.RegularExpressions.Regex
            .Replace(json, ",\\s*\"Difficulty\": \"[A-Za-z]+\"", string.Empty)
            .Replace("\"Recruit\"", "\"Peasant\"")
            .Replace("\"Soldier\"", "\"Spearman\"")
            .Replace("\"Captain\"", "\"Knight\"")
            .Replace("\"Commander\"", "\"Baron\"")
            .Replace($"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}", "\"FormatVersion\": 5");

        LoadedSave loaded = SaveSerializer.Deserialize(legacy);

        Unit captain = (Unit)loaded.State.Grid.Get(HexCoord.FromOffset(0, 0))!.Occupant!;
        Unit recruit = (Unit)loaded.State.Grid.Get(HexCoord.FromOffset(1, 0))!.Occupant!;
        Unit commander = (Unit)loaded.State.Grid.Get(HexCoord.FromOffset(3, 1))!.Occupant!;
        Assert.Equal(UnitLevel.Captain, captain.Level);
        Assert.Equal(UnitLevel.Recruit, recruit.Level);
        Assert.Equal(UnitLevel.Commander, commander.Level);
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

    [Fact]
    public void Serialize_RoundTripsOriginMapName()
    {
        // Games launched from a starting map record the map's name so
        // resumed saves can keep showing "Map: foo" in the bottom-left
        // label instead of falling back to the seed.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.Serialize(
            state, 42, players, slotName: "s", maxTurnNumber: 100, originMapName: "alpha");
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal("alpha", loaded.OriginMapName);
    }

    [Fact]
    public void Serialize_NullOriginMapName_RoundTripsAsNull()
    {
        // Procedural (Random Map) games leave the field unset so the
        // bottom-left label falls back to the seed.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.Serialize(
            state, 42, players, slotName: "s", maxTurnNumber: 100, originMapName: null);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Null(loaded.OriginMapName);
    }

    [Fact]
    public void Deserialize_OldSaveMissingOriginMapName_LoadsAsNull()
    {
        // Backward compat: saves written before the OriginMapName field
        // was added must still load (with the field defaulting to null),
        // since users have v2 autosaves on disk.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);
        // Sanity: the freshly serialized form does include the field.
        // To simulate an older save, strip it out before deserializing.
        string legacyJson = System.Text.RegularExpressions.Regex.Replace(
            json, "\\s*\"OriginMapName\":\\s*(\"[^\"]*\"|null),?", "");

        LoadedSave loaded = SaveSerializer.Deserialize(legacyJson);

        Assert.Null(loaded.OriginMapName);
    }

    [Fact]
    public void SerializeMap_DoesNotRecordOriginMapName()
    {
        // Editor-saved maps ARE the origin — they shouldn't carry an
        // OriginMapName field of their own.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.SerializeMap(state, 42, players, "m");
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Null(loaded.OriginMapName);
    }

    [Fact]
    public void Serialize_RoundTripsClaimVictoryPromptedTiers()
    {
        // The per-color highest-tier dictionary is per-game, persists
        // across save/load, and survives reload so each tier's prompt
        // won't re-fire.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        var prompted = new Dictionary<PlayerId, int>
        {
            [players[0].Id] = 75,
            [players[1].Id] = 90,
        };

        string json = SaveSerializer.Serialize(
            state, 42, players, slotName: "s", maxTurnNumber: 100,
            originMapName: null,
            claimVictoryPromptedHighestThreshold: prompted);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(2, loaded.ClaimVictoryPromptedHighestThreshold.Count);
        Assert.Equal(75, loaded.ClaimVictoryPromptedHighestThreshold[players[0].Id]);
        Assert.Equal(90, loaded.ClaimVictoryPromptedHighestThreshold[players[1].Id]);
    }

    [Fact]
    public void Serialize_EmptyClaimVictoryPromptedTiers_RoundTripsAsEmpty()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.Serialize(
            state, 42, players, slotName: "s", maxTurnNumber: 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Empty(loaded.ClaimVictoryPromptedHighestThreshold);
    }

    [Fact]
    public void Deserialize_LegacyColorListField_LoadsEachColorAtFiftyPercent()
    {
        // Backward compat: saves written by the single-tier (50%-only)
        // version of the feature stored a flat color hex list. On load,
        // each color is treated as "prompted at 50%" so the new 75% and
        // 90% prompts can still fire after reload.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string baseJson = SaveSerializer.Serialize(state, 42, players, "s", 100);

        // Inject the legacy field into otherwise-current JSON. Track
        // CurrentFormatVersion so the test survives format bumps.
        string legacyHex = GameSettings.PlayerConfig[players[0].Id.Index].Hex;
        string versionLine = $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion},";
        string injected = baseJson.Replace(
            versionLine,
            $"{versionLine}\n  \"ClaimVictoryPromptedColorHexes\": [\"{legacyHex}\"],");

        LoadedSave loaded = SaveSerializer.Deserialize(injected);

        Assert.Single(loaded.ClaimVictoryPromptedHighestThreshold);
        Assert.Equal(50, loaded.ClaimVictoryPromptedHighestThreshold[players[0].Id]);
    }

    [Fact]
    public void Serialize_NewSaves_OmitLegacyColorListField()
    {
        // The multi-tier serializer should write only the new dict
        // field — never the legacy flat list — even when a color is
        // recorded only at 50%. Keeps new saves clean.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        var prompted = new Dictionary<PlayerId, int>
        {
            [players[0].Id] = 50,
        };

        string json = SaveSerializer.Serialize(
            state, 42, players, slotName: "s", maxTurnNumber: 100,
            originMapName: null,
            claimVictoryPromptedHighestThreshold: prompted);

        // v5 writes only the palette-independent by-index field; neither
        // legacy color-hex field is written. Keeps new saves clean.
        Assert.DoesNotContain("ClaimVictoryPromptedColorHexes", json);
        Assert.DoesNotContain("ClaimVictoryPromptedHighestByColorHex", json);
        Assert.Contains("ClaimVictoryPromptedHighestByPlayerIndex", json);
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

    // --- Replay round-trip -------------------------------------------------

    [Fact]
    public void Serialize_WithoutReplay_DoesNotEmitReplayField()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);
        Assert.DoesNotContain("\"Replay\"", json);
    }

    [Fact]
    public void SaveRoundTrip_PreservesAllReplayBeatKinds()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);

        var beats = new List<ReplayBeat>
        {
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = new HexCoord(1, 0), To = new HexCoord(2, 0),
            },
            new ReplayBuyBeat
            {
                Index = 1, Turn = 1, Actor = 0,
                Capital = new HexCoord(0, 0), To = new HexCoord(0, 1),
                Level = UnitLevel.Soldier,
            },
            new ReplayBuildTowerBeat
            {
                Index = 2, Turn = 1, Actor = 0,
                Capital = new HexCoord(0, 0), To = new HexCoord(1, 1),
            },
            new ReplayEndTurnBeat { Index = 3, Turn = 1, Actor = 0 },
            new ReplayLongPressRallyBeat
            {
                Index = 4, Turn = 2, Actor = 1,
                Target = new HexCoord(3, 1),
            },
            new ReplayClaimVictoryBeat
            {
                Index = 5, Turn = 3, Actor = 0, ThresholdPercent = 75,
            },
            new ReplayDismissClaimBeat
            {
                Index = 6, Turn = 3, Actor = 0, ThresholdPercent = 50,
            },
            new ReplayDismissDefeatBeat { Index = 7, Turn = 4, Actor = 1 },
        };
        var replay = new Replay(snapshot, initialTurnNumber: 1,
            initialCurrentPlayerIndex: 0, beats: beats);

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100,
            replay: replay);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Replay);
        Assert.Equal(beats.Count, loaded.Replay!.Beats.Count);
        for (int i = 0; i < beats.Count; i++)
        {
            Assert.Equal(beats[i], loaded.Replay.Beats[i]);
        }
        Assert.Equal(1, loaded.Replay.InitialTurnNumber);
        Assert.Equal(0, loaded.Replay.InitialCurrentPlayerIndex);
    }

    [Fact]
    public void SaveRoundTrip_PreservesInitialSnapshotTilesAndGold()
    {
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);
        var replay = new Replay(snapshot, 1, 0, new List<ReplayBeat>());

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100,
            replay: replay);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.NotNull(loaded.Replay);
        // Apply both snapshots to fresh grids and compare tile-by-tile.
        HexGrid liveGrid = state.Grid;
        var freshGrid = new HexGrid();
        foreach (HexTile t in liveGrid.Tiles)
        {
            freshGrid.Add(new HexTile(t.Coord, t.Owner));
        }
        var freshTreasury = new Treasury();
        loaded.Replay!.InitialSnapshot.ApplyTo(freshGrid, freshTreasury);

        foreach (HexTile original in liveGrid.Tiles)
        {
            HexTile? restored = freshGrid.Get(original.Coord);
            Assert.NotNull(restored);
            Assert.Equal(original.Owner, restored!.Owner);
            AssertOccupantsEqual(original.Occupant, restored.Occupant);
        }
        foreach (Territory t in state.Territories)
        {
            if (!t.HasCapital) continue;
            Assert.Equal(state.Treasury.GetGold(t.Capital!.Value),
                freshTreasury.GetGold(t.Capital!.Value));
        }
    }

    [Fact]
    public void Deserialize_V3SaveWithoutReplayField_LeavesReplayNull()
    {
        // Write a v3 save (current pre-feature format) via the public
        // API without passing a replay argument.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);

        // The serializer bumps to v4 once the replay feature ships;
        // tweak the JSON to look like a v3 save (FormatVersion = 3,
        // no Replay block) and verify it still loads with Replay=null.
        string v3Json = json.Replace(
            $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}",
            "\"FormatVersion\": 3");
        LoadedSave loaded = SaveSerializer.Deserialize(v3Json);

        Assert.Null(loaded.Replay);
    }

    [Fact]
    public void Deserialize_V4SaveWithoutReplayField_LeavesReplayNull()
    {
        // A v4 save written without replay data (player chose not to
        // include it / fresh game with no actions yet) must load with
        // Replay = null, not throw.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);

        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Null(loaded.Replay);
    }

    [Fact]
    public void Serialize_RoundTripPreservesCampaignLevel()
    {
        // v8: a campaign game's level index rides along in the save so a
        // resumed autosave still knows which campaign hex it is (issue #2).
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100,
            campaignLevel: 79);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Equal(79, loaded.CampaignLevel);
    }

    [Fact]
    public void Serialize_FreeformGame_OmitsCampaignLevelAndLoadsNull()
    {
        // Non-campaign saves must not carry the field at all (skip-nulls
        // keeps the JSON clean) and must load with CampaignLevel = null.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();

        string json = SaveSerializer.Serialize(state, 42, players, "s", 100);
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.DoesNotContain("CampaignLevel", json);
        Assert.Null(loaded.CampaignLevel);
    }

    [Fact]
    public void Deserialize_V7SaveWithoutCampaignField_LoadsNullCampaignLevel()
    {
        // Saves written before the v8 CampaignLevel addition must keep
        // loading, with the field defaulting to null.
        (GameState state, IReadOnlyList<Player> players) = BuildRichState();
        string json = SaveSerializer.Serialize(state, 42, players, "s", 100)
            .Replace($"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}",
                "\"FormatVersion\": 7");

        LoadedSave loaded = SaveSerializer.Deserialize(json);

        Assert.Null(loaded.CampaignLevel);
    }
}
