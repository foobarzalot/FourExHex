using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pure tests for <see cref="StringTable"/> (parse / lookup / {token}
/// replacement / fallback + warn semantics / data-driven Tap-Click verb
/// variants) and a thin delegation check on the <see cref="Strings"/>
/// facade. Behavioral tests run on instances so the process-wide store —
/// configured from the shipped en.json by <see cref="TestStrings"/> —
/// is never disturbed for other test classes.
/// </summary>
public class StringStoreTests
{
    private const string SampleJson = """
    {
      "greeting": "Hello",
      "buy": "Buy {level} ({cost}g)",
      "repeat": "{name} and {name}",
      "prompt.continue": "{Verb} anywhere to continue, or {verb} a tile",
      "verb.capitalized.desktop": "Click",
      "verb.capitalized.mobile": "Tap",
      "verb.lowercase.desktop": "click",
      "verb.lowercase.mobile": "tap"
    }
    """;

    private static StringTable Desktop() => StringTable.Parse(SampleJson, isMobile: false);

    /// <summary>Run <paramref name="body"/> with Hud warnings captured;
    /// returns the emitted lines. Restores the sink and levels.</summary>
    private static List<string> CaptureHudLog(Action body)
    {
        var lines = new List<string>();
        Action<string>? oldSink = Log.Sink;
        Log.Sink = lines.Add;
        Log.SetLevel(Log.LogCategory.Hud, Log.LogLevel.Warn);
        try
        {
            body();
        }
        finally
        {
            Log.Sink = oldSink;
            Log.ResetLevels();
        }
        return lines;
    }

    [Fact]
    public void Get_KnownKey_ReturnsValue()
    {
        Assert.Equal("Hello", Desktop().Get("greeting"));
    }

    [Fact]
    public void Parse_CountsEntries()
    {
        Assert.Equal(8, Desktop().Count);
    }

    [Fact]
    public void Get_ReplacesNamedTokens()
    {
        Assert.Equal("Buy Soldier (20g)",
            Desktop().Get("buy", ("level", "Soldier"), ("cost", "20")));
    }

    [Fact]
    public void Get_ReplacesRepeatedToken()
    {
        Assert.Equal("Ada and Ada", Desktop().Get("repeat", ("name", "Ada")));
    }

    [Fact]
    public void Get_MissingKey_ReturnsKey_AndWarnsOncePerKey()
    {
        StringTable table = Desktop();
        string first = "", second = "";
        List<string> log = CaptureHudLog(() =>
        {
            first = table.Get("no.such.key");
            second = table.Get("no.such.key");
        });
        Assert.Equal("no.such.key", first);
        Assert.Equal("no.such.key", second);
        Assert.Single(log, line => line.Contains("missing key 'no.such.key'"));
    }

    [Fact]
    public void Get_MissingToken_LeavesLiteral_AndWarnsOncePerKeyToken()
    {
        StringTable table = Desktop();
        string first = "", second = "";
        List<string> log = CaptureHudLog(() =>
        {
            first = table.Get("buy", ("cost", "20"));
            second = table.Get("buy", ("cost", "20"));
        });
        Assert.Equal("Buy {level} (20g)", first);
        Assert.Equal("Buy {level} (20g)", second);
        Assert.Single(log, line => line.Contains("missing token '{level}' in 'buy'"));
    }

    [Fact]
    public void Get_DesktopVerbs_SubstituteClick()
    {
        Assert.Equal("Click anywhere to continue, or click a tile",
            Desktop().Get("prompt.continue"));
    }

    [Fact]
    public void Get_MobileVerbs_SubstituteTap()
    {
        StringTable mobile = StringTable.Parse(SampleJson, isMobile: true);
        Assert.Equal("Tap anywhere to continue, or tap a tile",
            mobile.Get("prompt.continue"));
    }

    [Fact]
    public void Parse_MalformedJson_Throws()
    {
        Assert.ThrowsAny<JsonException>(() => StringTable.Parse("{ not json", isMobile: false));
    }

    [Fact]
    public void Empty_RendersKeys()
    {
        List<string> log = CaptureHudLog(() =>
            Assert.Equal("anything", StringTable.Empty.Get("anything")));
        Assert.Single(log, line => line.Contains("missing key 'anything'"));
    }

    [Fact]
    public void Facade_Configure_ReplacesActiveTable()
    {
        try
        {
            Strings.Configure(SampleJson, isMobile: false);
            Assert.Equal("Hello", Strings.Get("greeting"));
            Assert.Equal(8, Strings.Count);
        }
        finally
        {
            TestStrings.ConfigureFromFixture();
        }
    }
}
