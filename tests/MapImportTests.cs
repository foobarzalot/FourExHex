// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Text.Json;
using FourExHex.Model;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Import validation for shared <c>.fxhmap</c> files. These files come
/// from other people, so <see cref="MapImport.Validate"/> must reject
/// malformed, hostile, or non-map input with a clean typed error rather
/// than crashing downstream (the view layer allocates from inferred
/// board bounds), and must never clobber an existing local map on a
/// name collision.
/// </summary>
public class MapImportTests
{
    /// <summary>
    /// A valid exported starting map: 4x3 board, Red (Human) owns a 2x2
    /// block, Blue (Computer) owns the rest, both with capitals, turn 0,
    /// baked kinds — exactly what the editor's export path produces.
    /// </summary>
    private static string BuildValidMapJson(
        string slotName = "shared", string? author = null)
    {
        (GameState state, IReadOnlyList<Player> players) = BuildStartingState();
        return SaveSerializer.SerializeMap(state, 42, players, slotName, author: author);
    }

    private static (GameState, IReadOnlyList<Player>) BuildStartingState()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);

        // Turn 0 = the on-disk "starting map" marker.
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury());
        return (state, players);
    }

    private static readonly IReadOnlyCollection<string> NoExisting =
        Array.Empty<string>();

    // --- well-formedness ------------------------------------------------

    [Theory]
    [InlineData("{garbage")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    public void Validate_NonMapInput_Malformed(string input)
    {
        MapImportResult result = MapImport.Validate(input, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.Malformed, result.Error);
        Assert.Null(result.Loaded);
        Assert.Null(result.NormalizedJson);
    }

    // --- version gate ---------------------------------------------------

    [Fact]
    public void Validate_FutureVersion_TooNew()
    {
        // A file from a newer build must produce the distinct "needs a
        // newer version" error, not generic malformed-ness.
        string json = BuildValidMapJson().Replace(
            $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}",
            $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion + 1}");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.TooNew, result.Error);
    }

    [Fact]
    public void Validate_PreV2Version_Malformed()
    {
        string json = BuildValidMapJson().Replace(
            $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}",
            "\"FormatVersion\": 1");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.Malformed, result.Error);
    }

    // --- starting-map discriminator -------------------------------------

    [Fact]
    public void Validate_InProgressSave_NotStartingMap()
    {
        // A real in-progress save (turn 3, finite max-turn cap) is not a
        // starting map and must be rejected as such.
        (GameState state, IReadOnlyList<Player> players) = BuildStartingState();
        var midGame = new GameState(
            state.Grid, state.Territories, players,
            new TurnState(players, currentPlayerIndex: 1, turnNumber: 3),
            new Treasury());
        string json = SaveSerializer.Serialize(midGame, 42, players, "game", 100);

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.NotStartingMap, result.Error);
    }

    // --- bounds ---------------------------------------------------------

    [Fact]
    public void Validate_AbsurdCoord_TooLarge()
    {
        // A hostile coord would make MapBounds.Infer report a gigantic
        // board and blow up view allocations — reject before load.
        string json = BuildValidMapJson().Replace("\"Q\": 3,", "\"Q\": 2000000000,");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.TooLarge, result.Error);
    }

    [Fact]
    public void Validate_NegativeOffsetCoord_TooLarge()
    {
        // MapBounds.Infer silently ignores negative offsets (only grows
        // the max), so the validator must reject them explicitly.
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Add(new HexTile(HexCoord.FromOffset(-1, 0), red.Id));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury());
        string json = SaveSerializer.SerializeMap(state, 42, players, "neg");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.TooLarge, result.Error);
    }

    [Fact]
    public void Validate_CoordBeyondMaxCols_TooLarge()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Add(new HexTile(HexCoord.FromOffset(MapImport.MaxCols, 0), blue.Id));
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury());
        string json = SaveSerializer.SerializeMap(state, 42, players, "wide");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.TooLarge, result.Error);
    }

    [Fact]
    public void Validate_CellCountAboveCap_TooLarge()
    {
        // Duplicate coords could smuggle an arbitrarily long tile list
        // through the per-coord bounds check — cap the raw cell count
        // before attempting the full deserialize.
        var data = new SaveData
        {
            FormatVersion = SaveSerializer.CurrentFormatVersion,
            SlotName = "flood",
            TurnNumber = 0,
            MaxTurnNumber = int.MaxValue,
        };
        for (int i = 0; i < MapImport.MaxCells + 1; i++)
        {
            data.Tiles.Add(new TileDto { Q = 0, R = 0, OwnerIndex = -1 });
        }
        string json = JsonSerializer.Serialize(data, FourExHexJsonContext.Default.SaveData);

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.TooLarge, result.Error);
    }

    // --- roster ---------------------------------------------------------

    [Fact]
    public void Validate_BakedPlayerWithoutLand_Invalid()
    {
        // A baked roster that disagrees with the painted territory (an
        // active color owning no land) fails the same rule the editor's
        // save path enforces.
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, red.Id); // Blue owns nothing
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury());
        string json = SaveSerializer.SerializeMap(state, 42, players, "landless");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.Invalid, result.Error);
        Assert.NotNull(result.ErrorDetail);
        Assert.Contains("Blue", result.ErrorDetail);
    }

    [Fact]
    public void Validate_LegacyMapWithoutBakedKinds_TwoOwners_Ok()
    {
        // Maps without baked kinds (pre-kind files) load via the legacy
        // default roster; with two landed owners they are importable.
        string json = System.Text.RegularExpressions.Regex.Replace(
            BuildValidMapJson(),
            ",\\s*\"Kind\": \"[A-Za-z]+\"", string.Empty);
        Assert.DoesNotContain("\"Kind\"", json); // surgery took effect

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.True(result.Ok);
    }

    [Fact]
    public void Validate_LegacyMapWithoutBakedKinds_OneOwner_Invalid()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 3, red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury());
        string json = System.Text.RegularExpressions.Regex.Replace(
            SaveSerializer.SerializeMap(state, 42, players, "solo"),
            ",\\s*\"Kind\": \"[A-Za-z]+\"", string.Empty);

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.False(result.Ok);
        Assert.Equal(MapImportError.Invalid, result.Error);
    }

    // --- success + collision --------------------------------------------

    [Fact]
    public void Validate_ValidMap_OkWithLoadedAndNormalizedJson()
    {
        string json = BuildValidMapJson(slotName: "shared", author: "Nathan");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.True(result.Ok);
        Assert.Null(result.Error);
        Assert.Equal("shared", result.FinalName);
        Assert.False(result.Renamed);
        Assert.NotNull(result.Loaded);
        Assert.NotNull(result.NormalizedJson);
        // The normalized JSON is a loadable map that kept its author.
        LoadedSave reloaded = SaveSerializer.Deserialize(result.NormalizedJson!);
        Assert.Equal("Nathan", reloaded.Author);
        Assert.Equal("shared", reloaded.SlotName);
    }

    [Fact]
    public void Validate_NameCollision_AutoSuffixesAndRewritesSlotName()
    {
        // Never overwrite a local map: collide → suffixed name, and the
        // JSON's SlotName is rewritten to match (the list UI labels rows
        // from the header's SlotName, not the filename).
        string json = BuildValidMapJson(slotName: "shared");

        MapImportResult result = MapImport.Validate(json, new[] { "shared" });

        Assert.True(result.Ok);
        Assert.True(result.Renamed);
        Assert.Equal("shared-2", result.FinalName);
        Assert.Equal("shared-2", SaveSerializer.Deserialize(result.NormalizedJson!).SlotName);
    }

    [Fact]
    public void Validate_HostileSlotName_SanitizedBeforeCollisionCheck()
    {
        // Slot names travel inside the file and become the destination
        // basename — path-traversal characters must be sanitized away.
        string json = BuildValidMapJson(slotName: "../../evil map");

        MapImportResult result = MapImport.Validate(json, NoExisting);

        Assert.True(result.Ok);
        Assert.Equal(SaveNames.Sanitize("../../evil map"), result.FinalName);
        Assert.DoesNotContain("/", result.FinalName);
        Assert.DoesNotContain("..", result.FinalName);
    }

    // --- ResolveName ----------------------------------------------------

    [Fact]
    public void ResolveName_NoCollision_Passthrough()
    {
        Assert.Equal("map", MapImport.ResolveName("map", new[] { "other" }));
    }

    [Fact]
    public void ResolveName_Collision_AppendsSuffix()
    {
        Assert.Equal("map-2", MapImport.ResolveName("map", new[] { "map" }));
    }

    [Fact]
    public void ResolveName_ChainedCollisions_IncrementSuffix()
    {
        Assert.Equal("map-3", MapImport.ResolveName("map", new[] { "map", "map-2" }));
    }

    [Fact]
    public void ResolveName_MaxLengthName_SuffixStaysWithinSanitizeCap()
    {
        // Appending "-2" to a 64-char name must not exceed the sanitize
        // cap (a later Sanitize call would truncate the suffix back off
        // and re-collide).
        string longName = new string('a', 64);

        string resolved = MapImport.ResolveName(longName, new[] { longName });

        Assert.True(resolved.Length <= 64);
        Assert.EndsWith("-2", resolved);
        Assert.NotEqual(longName, resolved);
    }
}
