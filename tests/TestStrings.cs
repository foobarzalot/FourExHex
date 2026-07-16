// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace FourExHex.Tests;

/// <summary>
/// Configures the process-wide <see cref="Strings"/> store from the real
/// shipped <c>assets/strings/en.json</c> (copied beside the test binaries
/// by the csproj) once, before any test runs. Copy-helper tests therefore
/// assert the actual user-facing English with zero per-test setup, exactly
/// as the game sees it (desktop verbs). Tests that reconfigure the global
/// store must restore it via <see cref="ConfigureFromFixture"/>.
/// </summary>
internal static class TestStrings
{
    internal static string FixturePath
        => Path.Combine(AppContext.BaseDirectory, "assets", "strings", "en.json");

    [ModuleInitializer]
    internal static void Init() => ConfigureFromFixture();

    internal static void ConfigureFromFixture(bool isMobile = false)
        => Strings.Configure(File.ReadAllText(FixturePath), isMobile);
}
