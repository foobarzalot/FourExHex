// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Names the demo-replay tutorials among the bundled tutorial files.
/// Demos are the <c>demo_*</c> subset of <c>res://tutorials/*.json</c>,
/// played hands-free from the cheat menu's Demo Replays picker.
/// Pure string logic so the picker's selection rule is unit-testable;
/// the Godot-side directory scan lives in <c>SaveStore</c>.
/// </summary>
public static class DemoCatalog
{
    /// <summary>
    /// Filter a directory listing down to demo slot names: keeps files
    /// matching <c>demo_*.json</c> (case-insensitive), returns their
    /// slot names (extension stripped, original casing kept), sorted
    /// ordinally for a stable picker order.
    /// </summary>
    public static List<string> FilterDemoNames(IEnumerable<string> fileNames)
    {
        const string prefix = "demo_";
        const string extension = ".json";
        var names = new List<string>();
        foreach (string fileName in fileNames)
        {
            if (fileName.Length < prefix.Length + extension.Length) continue;
            if (!fileName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!fileName.EndsWith(extension, System.StringComparison.OrdinalIgnoreCase)) continue;
            names.Add(fileName[..^extension.Length]);
        }
        names.Sort(System.StringComparer.Ordinal);
        return names;
    }
}
