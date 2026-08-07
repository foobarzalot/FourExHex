// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Xunit;

namespace FourExHex.Tests;

// Log holds process-wide static state (per-category levels + Sink).
// Each test resets levels and restores the Sink so tests don't leak
// into one another. NOTE: the [Conditional("DEBUG")] compile-time
// strip of Trace/Debug/Info is NOT exercised here — `dotnet test`
// builds with DEBUG defined, so those calls compile and run; these
// tests cover only the runtime level gate. The Release strip is a C#
// language guarantee, spot-verified by a Release IL check, not a unit
// test.
public class LogTests
{
    private static Log.LogCategory[] AllCategories =>
        (Log.LogCategory[])Enum.GetValues(typeof(Log.LogCategory));

    private static void RunIsolated(Action body)
    {
        Action<string>? savedSink = Log.Sink;
        try
        {
            Log.ResetLevels();
            body();
        }
        finally
        {
            Log.Sink = savedSink;
            Log.ResetLevels();
        }
    }

    [Fact]
    public void Levels_DefaultToOff_NothingEmits()
    {
        RunIsolated(() =>
        {
            var seen = new List<string>();
            Log.Sink = seen.Add;

            Log.Warn(Log.LogCategory.Ai, "x");
            Log.Error(Log.LogCategory.Turn, "y");
            Log.Info(Log.LogCategory.Capture, "z");

            Assert.Empty(seen);
        });
    }

    [Fact]
    public void SetLevel_Info_AllowsInfoAndAbove_BlocksDebug()
    {
        RunIsolated(() =>
        {
            var seen = new List<string>();
            Log.Sink = seen.Add;
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Info);

            Log.Trace(Log.LogCategory.Ai, "t");
            Log.Debug(Log.LogCategory.Ai, "d");
            Log.Info(Log.LogCategory.Ai, "i");
            Log.Warn(Log.LogCategory.Ai, "w");

            Assert.Equal(new[] { "i", "WARN  [Ai] w" }, seen);
        });
    }

    [Fact]
    public void SetLevel_IsPerCategoryIndependent()
    {
        RunIsolated(() =>
        {
            var seen = new List<string>();
            Log.Sink = seen.Add;
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Trace);
            Log.SetLevel(Log.LogCategory.Turn, Log.LogLevel.Error);

            Log.Debug(Log.LogCategory.Ai, "a");   // 1 >= Trace(0) -> emit
            Log.Debug(Log.LogCategory.Turn, "b"); // 1 <  Error(4) -> drop
            Log.Error(Log.LogCategory.Turn, "c"); // 4 >= Error(4) -> emit

            Assert.Equal(new[] { "a", "ERROR [Turn] c" }, seen);
        });
    }

    [Fact]
    public void IsEnabled_ReflectsThreshold()
    {
        RunIsolated(() =>
        {
            Log.SetLevel(Log.LogCategory.Capture, Log.LogLevel.Warn);

            Assert.True(Log.IsEnabled(Log.LogCategory.Capture, Log.LogLevel.Error));
            Assert.True(Log.IsEnabled(Log.LogCategory.Capture, Log.LogLevel.Warn));
            Assert.False(Log.IsEnabled(Log.LogCategory.Capture, Log.LogLevel.Info));
            Assert.False(Log.IsEnabled(Log.LogCategory.Capture, Log.LogLevel.Debug));
        });
    }

    [Fact]
    public void Configure_ParsesPairs_AndStarDefaultAppliesToAll()
    {
        RunIsolated(() =>
        {
            // '*' comes last and iterates every category -> all Warn.
            Log.Configure("AI:Debug,Turn:Info,*:Warn");

            foreach (Log.LogCategory c in AllCategories)
            {
                Assert.True(Log.IsEnabled(c, Log.LogLevel.Warn));
                Assert.False(Log.IsEnabled(c, Log.LogLevel.Info));
            }
        });
    }

    [Fact]
    public void Configure_StarThenSpecificOverrides()
    {
        RunIsolated(() =>
        {
            Log.Configure("*:Warn,AI:Debug");

            Assert.True(Log.IsEnabled(Log.LogCategory.Ai, Log.LogLevel.Debug));
            Assert.False(Log.IsEnabled(Log.LogCategory.Turn, Log.LogLevel.Debug));
            Assert.True(Log.IsEnabled(Log.LogCategory.Turn, Log.LogLevel.Warn));
        });
    }

    [Fact]
    public void Configure_IsCaseInsensitive()
    {
        RunIsolated(() =>
        {
            Log.Configure("ai:dEbUg,TURN:info");

            Assert.True(Log.IsEnabled(Log.LogCategory.Ai, Log.LogLevel.Debug));
            Assert.True(Log.IsEnabled(Log.LogCategory.Turn, Log.LogLevel.Info));
            Assert.False(Log.IsEnabled(Log.LogCategory.Turn, Log.LogLevel.Debug));
        });
    }

    [Fact]
    public void Configure_UnknownTokensIgnored_NeverThrows()
    {
        RunIsolated(() =>
        {
            Exception? ex = Record.Exception(() =>
                Log.Configure("Bogus:Info,Ai:Nonsense,:Info,Turn:,  ,Ai:Trace"));

            Assert.Null(ex);
            Assert.True(Log.IsEnabled(Log.LogCategory.Ai, Log.LogLevel.Trace));
            // Turn was never validly set -> still Off.
            Assert.False(Log.IsEnabled(Log.LogCategory.Turn, Log.LogLevel.Error));
        });
    }

    [Fact]
    public void Configure_NullOrEmpty_IsNoOp()
    {
        RunIsolated(() =>
        {
            Log.Configure(null);
            Log.Configure("");
            Log.Configure("   ");

            Assert.False(Log.IsEnabled(Log.LogCategory.Ai, Log.LogLevel.Error));
        });
    }

    [Fact]
    public void Emit_WhenSinkNull_DoesNotThrow()
    {
        RunIsolated(() =>
        {
            Log.Sink = null;
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Trace);

            Exception? ex = Record.Exception(() =>
            {
                Log.Trace(Log.LogCategory.Ai, "a");
                Log.Info(Log.LogCategory.Ai, "b");
                Log.Warn(Log.LogCategory.Ai, "c");
                Log.Error(Log.LogCategory.Ai, "d");
            });

            Assert.Null(ex);
        });
    }

    // Trace/Debug/Info stay byte-identical on the wire: the FOUREXHEX_6AI
    // determinism diff, tools/tree_sweep.sh's greps, and
    // AiBaselineMeasurementTests' `StartsWith("[chose] ")` all parse those
    // levels. Only Warn/Error — which nothing parses positionally — carry a tag.
    [Fact]
    public void InfoAndBelow_RouteVerbatimInOrder()
    {
        RunIsolated(() =>
        {
            var seen = new List<string>();
            Log.Sink = seen.Add;
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Trace);

            Log.Trace(Log.LogCategory.Ai, "alpha");
            Log.Debug(Log.LogCategory.Ai, "beta");
            Log.Info(Log.LogCategory.Ai, "gamma");

            Assert.Equal(new[] { "alpha", "beta", "gamma" }, seen);
        });
    }

    // The severity tag is what makes "fail the run on any Warn/Error" greppable
    // from stdout — without it a Warn and a Debug are indistinguishable, since
    // Emit passes the caller's message through untouched.
    [Fact]
    public void WarnAndError_CarrySeverityAndCategoryTag()
    {
        RunIsolated(() =>
        {
            var seen = new List<string>();
            Log.Sink = seen.Add;
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Trace);

            Log.Warn(Log.LogCategory.Ai, "alpha");
            Log.Error(Log.LogCategory.Ai, "beta");

            Assert.Equal(new[] { "WARN  [Ai] alpha", "ERROR [Ai] beta" }, seen);
        });
    }

    // The tag is fixed-width so a log column-aligns, and the harness grep
    // (^WARN |^ERROR ) anchors on it.
    [Fact]
    public void SeverityTags_AreFixedWidthAndAnchorable()
    {
        RunIsolated(() =>
        {
            var seen = new List<string>();
            Log.Sink = seen.Add;
            Log.SetLevel(Log.LogCategory.Layout, Log.LogLevel.Warn);

            Log.Warn(Log.LogCategory.Layout, "x");
            Log.Error(Log.LogCategory.Layout, "y");

            Assert.All(seen, line => Assert.Equal(' ', line[5]));
            Assert.StartsWith("WARN  [Layout] ", seen[0]);
            Assert.StartsWith("ERROR [Layout] ", seen[1]);
        });
    }

    // The audit walker's category has to be addressable by name from
    // FOUREXHEX_LOG like every other one.
    [Fact]
    public void Configure_RecognizesLayoutCategory()
    {
        RunIsolated(() =>
        {
            Log.Configure("Layout:Warn");

            Assert.True(Log.IsEnabled(Log.LogCategory.Layout, Log.LogLevel.Warn));
            Assert.False(Log.IsEnabled(Log.LogCategory.Layout, Log.LogLevel.Info));
        });
    }

    [Fact]
    public void ResetLevels_RestoresAllOff()
    {
        RunIsolated(() =>
        {
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Trace);
            Log.SetLevel(Log.LogCategory.Turn, Log.LogLevel.Info);
            Log.SetLevel(Log.LogCategory.Render, Log.LogLevel.Error);

            Log.ResetLevels();

            foreach (Log.LogCategory c in AllCategories)
            {
                Assert.False(Log.IsEnabled(c, Log.LogLevel.Error));
            }
        });
    }

    // --- Compile-time strip contract ------------------------------------
    //
    // Which constant gates Trace/Debug/Info decides whether a shipping
    // build logs anything at all: keyed to DEBUG they vanish from every
    // ExportRelease build (the bug-report log arrives empty), and DEBUG
    // cannot simply be defined there because it also gates the CheatMenu
    // via #if DEBUG. These pin the constant by reflection — the property
    // is otherwise invisible to a test suite that always builds with
    // logging compiled in.

    private static string? ConditionOf(string methodName)
    {
        MethodInfo method = typeof(Log).GetMethod(
            methodName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Log.{methodName} not found");
        ConditionalAttribute? conditional =
            method.GetCustomAttribute<ConditionalAttribute>();
        return conditional?.ConditionString;
    }

    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Info")]
    [InlineData("Since")]
    public void StrippableLevels_GateOnTheLoggingConstant_NotDebug(string methodName)
    {
        Assert.Equal("FOUREXHEX_LOGGING", ConditionOf(methodName));
    }

    [Theory]
    [InlineData("Warn")]
    [InlineData("Error")]
    public void WarnAndError_AreNeverConditional(string methodName)
    {
        Assert.Null(ConditionOf(methodName));
    }

    // --- Shipping-build verbosity default -------------------------------

    [Fact]
    public void ExportedBuild_WithNoSpec_DefaultsVerbose()
    {
        Assert.True(LogDefaults.ShouldDefaultVerbose(null, isExportedBuild: true));
        Assert.True(LogDefaults.ShouldDefaultVerbose("", isExportedBuild: true));
        Assert.True(LogDefaults.ShouldDefaultVerbose("   ", isExportedBuild: true));
    }

    [Fact]
    public void ExportedBuild_WithAnExplicitSpec_LeavesItAlone()
    {
        Assert.False(LogDefaults.ShouldDefaultVerbose("Ai:Debug", isExportedBuild: true));
    }

    [Fact]
    public void EditorRun_NeverDefaultsVerbose()
    {
        // Dev launches and every FOUREXHEX_6AI* run start from the editor
        // binary; going verbose there would make normal play noisy and
        // perturb the seeded determinism diff.
        Assert.False(LogDefaults.ShouldDefaultVerbose(null, isExportedBuild: false));
        Assert.False(LogDefaults.ShouldDefaultVerbose("Ai:Debug", isExportedBuild: false));
    }
}
