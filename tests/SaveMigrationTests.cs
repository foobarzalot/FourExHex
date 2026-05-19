using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Stage-5 format bump: v5 stores claim-victory by player index and
/// owner-index -1 = PlayerId.None. v2/3/4 still load via the legacy
/// color-hex path. These tests pin the migration behavior.
/// </summary>
public class SaveMigrationTests
{
    private static (GameState, IReadOnlyList<Player>) BuildState()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), AiKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), AiKind.Heuristic);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(4, 3, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        IReadOnlyList<Territory> terr = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turn = new TurnState(players, currentPlayerIndex: 0, turnNumber: 3);
        var treasury = new Treasury();
        foreach (Territory t in terr)
            if (t.HasCapital) treasury.SetGold(t.Capital!.Value, 10);
        return (new GameState(grid, terr, players, turn, treasury), players);
    }

    [Fact]
    public void CurrentFormatVersion_IsFive()
    {
        Assert.Equal(5, SaveSerializer.CurrentFormatVersion);
    }

    [Fact]
    public void Deserialize_RejectsUnsupportedVersions()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildState();
        string json = SaveSerializer.Serialize(s, 1, p, "slot", 100);

        foreach (int bad in new[] { 1, 6, 99 })
        {
            string mutated = json.Replace(
                $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}",
                $"\"FormatVersion\": {bad}");
            Assert.ThrowsAny<System.Exception>(() => SaveSerializer.Deserialize(mutated));
        }

        foreach (int ok in new[] { 2, 3, 4, 5 })
        {
            string mutated = json.Replace(
                $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}",
                $"\"FormatVersion\": {ok}");
            SaveSerializer.Deserialize(mutated); // must not throw
        }
    }

    [Fact]
    public void V5_RoundTrips_OwnersAndClaimVictory()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildState();
        var claim = new Dictionary<PlayerId, int>
        {
            [PlayerId.FromIndex(0)] = 75,
            [PlayerId.FromIndex(1)] = 50,
        };
        string json = SaveSerializer.Serialize(s, 7, p, "slot", 100,
            claimVictoryPromptedHighestThreshold: claim);

        // v5 writes the by-player-index field, not the legacy hex map.
        Assert.Contains("ClaimVictoryPromptedHighestByPlayerIndex", json);

        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.Equal(GameStateChecksum.Compute(s),
                     GameStateChecksum.Compute(loaded.State));
        Assert.Equal(75, loaded.ClaimVictoryPromptedHighestThreshold[PlayerId.FromIndex(0)]);
        Assert.Equal(50, loaded.ClaimVictoryPromptedHighestThreshold[PlayerId.FromIndex(1)]);
    }

    [Fact]
    public void V5_ByPlayerIndex_TakesPrecedenceOverLegacyByColorHex()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildState();
        string baseJson = SaveSerializer.Serialize(s, 1, p, "slot", 100,
            claimVictoryPromptedHighestThreshold:
                new Dictionary<PlayerId, int> { [PlayerId.FromIndex(1)] = 90 });

        // Inject a contradictory legacy hex map for P0. The v5 by-index
        // field must win; the legacy field is ignored when present.
        string p0Hex = GameSettings.PlayerConfig[0].Hex;
        string injected = baseJson.Replace(
            "\"ClaimVictoryPromptedHighestByPlayerIndex\"",
            $"\"ClaimVictoryPromptedHighestByColorHex\": {{\"{p0Hex}\": 50}},\n  \"ClaimVictoryPromptedHighestByPlayerIndex\"");

        LoadedSave loaded = SaveSerializer.Deserialize(injected);
        Assert.Single(loaded.ClaimVictoryPromptedHighestThreshold);
        Assert.Equal(90, loaded.ClaimVictoryPromptedHighestThreshold[PlayerId.FromIndex(1)]);
    }

    [Fact]
    public void V4_ByColorHex_MigratesToPlayerId()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildState();
        string json = SaveSerializer.Serialize(s, 1, p, "slot", 100);

        string cur = $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}";
        string p0Hex = GameSettings.PlayerConfig[0].Hex;
        string v4 = json
            .Replace(cur, "\"FormatVersion\": 4")
            .Replace("\"FormatVersion\": 4,",
                "\"FormatVersion\": 4,\n  \"ClaimVictoryPromptedHighestByColorHex\": {\"" + p0Hex + "\": 75},");

        LoadedSave loaded = SaveSerializer.Deserialize(v4);
        Assert.Equal(75, loaded.ClaimVictoryPromptedHighestThreshold[PlayerId.FromIndex(0)]);
    }

    [Fact]
    public void V3_FlatColorHexes_MapToFiftyPercent()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildState();
        string json = SaveSerializer.Serialize(s, 1, p, "slot", 100);

        string cur = $"\"FormatVersion\": {SaveSerializer.CurrentFormatVersion}";
        string p1Hex = GameSettings.PlayerConfig[1].Hex;
        string v3 = json
            .Replace(cur, "\"FormatVersion\": 3")
            .Replace("\"FormatVersion\": 3,",
                "\"FormatVersion\": 3,\n  \"ClaimVictoryPromptedColorHexes\": [\"" + p1Hex + "\"],");

        LoadedSave loaded = SaveSerializer.Deserialize(v3);
        Assert.Equal(50, loaded.ClaimVictoryPromptedHighestThreshold[PlayerId.FromIndex(1)]);
    }

    [Fact]
    public void V5_NoneOwnedTile_RoundTripsViaMinusOne()
    {
        (GameState s, IReadOnlyList<Player> p) = BuildState();
        // Force an unowned tile (defensive: real games never produce one,
        // but the format must encode None as -1 and decode it back).
        s.Grid.Get(HexCoord.FromOffset(3, 2))!.Owner = PlayerId.None;
        // Drop it from any territory so reconciliation doesn't matter.
        string json = SaveSerializer.Serialize(s, 1, p, "slot", 100);
        Assert.Contains("\"OwnerIndex\": -1", json);

        LoadedSave loaded = SaveSerializer.Deserialize(json);
        Assert.True(loaded.State.Grid.Get(HexCoord.FromOffset(3, 2))!.Owner.IsNone);
    }
}
