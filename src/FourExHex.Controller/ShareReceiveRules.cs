// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FourExHex
using System;
using System.Collections.Generic;

namespace FourExHex.Controller;

/// <summary>
/// Pure decision rules for files handed to the app by the OS share/open
/// surfaces (godot-share receive payloads, ACTION_VIEW opens). The view-side
/// inbox feeds every incoming path through here; only what survives is
/// imported.
/// </summary>
public static class ShareReceiveRules
{
    /// <summary>The map transport extension the OS routes to us.</summary>
    public const string MapFileExtension = ".fxhmap";

    /// <summary>Filter an incoming path list down to the importable map
    /// files: case-insensitive <see cref="MapFileExtension"/> match,
    /// original order preserved, exact duplicates and null/blank entries
    /// dropped.</summary>
    public static IReadOnlyList<string> FxhmapPaths(IEnumerable<string?> paths)
    {
        List<string> accepted = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string? path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (!path.EndsWith(MapFileExtension, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(path)) continue;
            accepted.Add(path);
        }
        return accepted;
    }
}
