// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// The authored starting maps shipped inside the build
/// (<c>res://maps/*.json</c>). A hardcoded catalog, not a directory
/// scan — <c>res://</c> listing is unreliable inside exported PCKs, so
/// like the tutorial/instruction catalogs the shipped names live in
/// code. Shipping a new map = commit the JSON under <c>maps/</c> and
/// add its name here (see LEVEL_DESIGN.md).
/// </summary>
public static class StartingMapCatalog
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "atoll-6p",
    };

    /// <summary>
    /// The Load Starting Map listing: user rows first (order kept),
    /// then catalog-order bundled rows — except names shadowed by a
    /// user map (matching <c>LoadStartingMap</c>'s user-wins
    /// resolution) and names whose header read fails (returns null).
    /// </summary>
    public static IReadOnlyList<SaveSlotInfo> MergeWithUser(
        IReadOnlyList<SaveSlotInfo> userMaps,
        Func<string, SaveSlotInfo?> bundledHeaderFor)
    {
        var merged = new List<SaveSlotInfo>(userMaps);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (SaveSlotInfo info in userMaps) taken.Add(info.SlotName);

        foreach (string name in Names)
        {
            if (taken.Contains(name)) continue;
            SaveSlotInfo? bundled = bundledHeaderFor(name);
            if (bundled != null) merged.Add(bundled);
        }
        return merged;
    }
}
