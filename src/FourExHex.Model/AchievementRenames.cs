// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Old-id → current-id mapping applied when an achievement record is read.
///
/// Achievement ids stay changeable through design and playtesting: the
/// persisted file is ours, so renaming one costs an entry here rather than
/// a lost record. That stops the day the ids are registered in a platform
/// console against live users — after that the platform holds a record we
/// cannot migrate, and renames are off the table.
///
/// The map is applied <b>once</b>, at depth 1 — chains are not followed.
/// Adding a second hop for an id means collapsing it into a single
/// old → current entry; <c>AchievementRecordTests.RenameMap_NoValueIsAlsoAKey</c>
/// fails the build otherwise.
/// </summary>
public static class AchievementRenames
{
    /// <summary>The shipped mapping. Empty until an id is renamed.</summary>
    public static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>();
}
