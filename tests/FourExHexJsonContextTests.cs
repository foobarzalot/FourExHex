using System.Collections.Generic;
using System.Text.Json;
using FourExHex.Model;
using Xunit;

namespace FourExHex.Tests;

// AOT compatibility contract for save (de)serialization. iOS forbids JIT, so
// .NET on iOS is AOT-compiled and reflection-based JSON throws "Reflection-
// based serialization has been disabled for this application." To survive
// that, every root type SaveSerializer / SaveStore / UserSettings push through System.Text.Json
// must be reachable via FourExHexJsonContext's source-generated JsonTypeInfo
// tables — no reflection fallback.
//
// These tests assert each root type round-trips through the context's
// typed JsonTypeInfo. A missing [JsonSerializable] attribute is a compile-
// time failure (the matching property is missing) — that's the same error
// mode that catches iOS save bugs, just one cycle earlier.
public class FourExHexJsonContextTests
{
    [Fact]
    public void Context_RoundTripsSaveData_ViaSourceGenJsonTypeInfo()
    {
        // Minimal SaveData — every required field set. We're testing the
        // context's reachability of the SaveData type, not the SaveSerializer
        // builder logic (which has its own tests).
        var data = new SaveData
        {
            FormatVersion = SaveSerializer.CurrentFormatVersion,
            SavedAtUnix = 1_700_000_000,
            SlotName = "test-slot",
            MasterSeed = 42,
            TurnNumber = 1,
            CurrentPlayerIndex = 0,
            MaxTurnNumber = 100,
            Players = new List<PlayerDto>
            {
                new PlayerDto { Index = 0, Name = "Red", ColorHex = "#ff0000", Kind = "Human" },
            },
        };

        // FourExHexJsonContext.Default.SaveData is the source-gen
        // JsonTypeInfo<SaveData>; the production-path overload bakes in the
        // context's [JsonSourceGenerationOptions] attribute settings. If the
        // SaveData entry is removed from the context, this line stops
        // compiling — that's the AOT regression net.
        string json = JsonSerializer.Serialize(data, FourExHexJsonContext.Default.SaveData);
        SaveData? roundtrip = JsonSerializer.Deserialize(json, FourExHexJsonContext.Default.SaveData);

        Assert.NotNull(roundtrip);
        Assert.Equal(SaveSerializer.CurrentFormatVersion, roundtrip!.FormatVersion);
        Assert.Equal("test-slot", roundtrip.SlotName);
        Assert.Equal(42, roundtrip.MasterSeed);
        Assert.Single(roundtrip.Players);
        Assert.Equal("Red", roundtrip.Players[0].Name);
    }

    [Fact]
    public void Context_AppliesWriteIndentedAndIgnoreNullsFromAttribute()
    {
        // The [JsonSourceGenerationOptions] attribute on the context sets
        // WriteIndented = true + DefaultIgnoreCondition = WhenWritingNull, so
        // a SaveData with null optional fields produces a multi-line JSON that
        // omits the null entries from the indented output.
        var data = new SaveData
        {
            FormatVersion = SaveSerializer.CurrentFormatVersion,
            SlotName = "indent-test",
            // OriginMapName left null on purpose — must be absent from JSON.
        };

        string json = JsonSerializer.Serialize(data, FourExHexJsonContext.Default.SaveData);

        Assert.Contains("\n", json); // indented = multi-line
        Assert.DoesNotContain("OriginMapName", json); // null field omitted
    }
}
