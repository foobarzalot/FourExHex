// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

// No namespace — matches Log and the rest of the Model library, so scene
// scripts reach these types unqualified.

/// <summary>
/// The boot-time decision of how loud <see cref="Log"/> starts, kept
/// Godot-free so it is unit-testable (the caller, <c>LogBootstrap</c>, is a
/// scene script and is not).
/// </summary>
public static class LogDefaults
{
    /// <summary>
    /// True when the session should start with every category fully verbose.
    ///
    /// An exported build has no way to set <c>FOUREXHEX_LOG</c>, and the log it
    /// writes is the only diagnostic a player's bug report can carry — so it
    /// starts loud. Editor and CLI runs stay silent unless the env var asks
    /// otherwise: that keeps normal dev play quiet and keeps the seeded
    /// <c>FOUREXHEX_6AI*</c> determinism diff free of output nobody asked for.
    /// An explicit <paramref name="spec"/> always wins, in either build.
    /// </summary>
    public static bool ShouldDefaultVerbose(string? spec, bool isExportedBuild)
        => isExportedBuild && string.IsNullOrWhiteSpace(spec);
}
