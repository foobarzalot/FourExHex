using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FourExHex.Model;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins exact two-way agreement between the <see cref="StringKeys"/> consts
/// and the shipped <c>assets/strings/en.json</c> (copied beside the test
/// binaries — see <see cref="TestStrings.FixturePath"/>). A key added,
/// renamed, or removed on one side only fails <c>dotnet test</c>, so
/// stringly-typed drift between code and the store is impossible.
/// </summary>
public class StringKeysParityTests
{
    private static HashSet<string> JsonKeys()
    {
        string json = File.ReadAllText(TestStrings.FixturePath);
        Dictionary<string, string> table = JsonSerializer.Deserialize(
            json, FourExHexJsonContext.Default.DictionaryStringString)!;
        return table.Keys.ToHashSet();
    }

    private static List<string> ConstKeys()
        => typeof(StringKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    [Fact]
    public void EveryConstKeyHasAJsonEntry()
    {
        List<string> missing = ConstKeys().Except(JsonKeys()).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0,
            $"StringKeys consts with no en.json entry: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryJsonEntryHasAConstKey()
    {
        List<string> extra = JsonKeys().Except(ConstKeys()).OrderBy(k => k).ToList();
        Assert.True(extra.Count == 0,
            $"en.json entries with no StringKeys const: {string.Join(", ", extra)}");
    }

    [Fact]
    public void ConstKeysAreUnique()
    {
        List<string> dupes = ConstKeys()
            .GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key)
            .OrderBy(k => k).ToList();
        Assert.True(dupes.Count == 0,
            $"Duplicate StringKeys const values: {string.Join(", ", dupes)}");
    }
}
