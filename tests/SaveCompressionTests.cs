// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// <see cref="SaveCompression"/> is the gzip layer under the per-level
/// campaign attempt files. It must round-trip any save text exactly,
/// recognise its own output, and tolerate a plain-JSON file dropped in
/// by hand (reads it as-is instead of failing).
/// </summary>
public class SaveCompressionTests
{
    private static string RichSaveJson()
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(12, 9, blue.Id);
        for (int c = 0; c < 4; c++)
            for (int r = 0; r < 4; r++)
                grid.Get(HexCoord.FromOffset(c, r))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(0, 0))!.Occupant = new Unit(red.Id, UnitLevel.Captain);
        grid.Get(HexCoord.FromOffset(6, 5))!.Occupant = new Tower();
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 9), new Treasury());
        return SaveSerializer.Serialize(state, 7, players, "level_0A", 100, campaignLevel: 10);
    }

    [Fact]
    public void RoundTrip_RealSave_IsExact()
    {
        string json = RichSaveJson();

        byte[] packed = SaveCompression.Compress(json);
        string unpacked = SaveCompression.Decompress(packed);

        Assert.Equal(json, unpacked);
        // The whole point: a stored attempt is a small fraction of its JSON.
        Assert.True(packed.Length < Encoding.UTF8.GetByteCount(json) / 4,
            $"gzip {packed.Length} bytes vs raw {json.Length} — compression not effective");
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("héllo wörld — 日本語 🎉")]
    public void RoundTrip_EdgeText_IsExact(string text)
    {
        Assert.Equal(text, SaveCompression.Decompress(SaveCompression.Compress(text)));
    }

    [Fact]
    public void IsGzip_RecognisesOwnOutputOnly()
    {
        Assert.True(SaveCompression.IsGzip(SaveCompression.Compress("{}")));
        Assert.False(SaveCompression.IsGzip(Encoding.UTF8.GetBytes("{}")));
        Assert.False(SaveCompression.IsGzip(new byte[0]));
        Assert.False(SaveCompression.IsGzip(new byte[] { 0x1f }));
    }

    [Fact]
    public void Decompress_PlainUtf8_ReadsAsIs()
    {
        // A hand-dropped uncompressed .json still loads.
        string json = "{\"FormatVersion\": 21}";

        Assert.Equal(json, SaveCompression.Decompress(Encoding.UTF8.GetBytes(json)));
    }
}
