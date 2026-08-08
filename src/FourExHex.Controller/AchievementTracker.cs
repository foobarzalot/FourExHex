// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// Turns an observable <see cref="AchievementEvent"/> into progress and
/// unlocks against the local record. Pure arithmetic over
/// <see cref="AchievementCatalog"/> — it does not decide <em>whether</em>
/// an event should be raised, which is the controller's guard (a replay
/// re-runs real beats and must award nothing).
///
/// Already-unlocked achievements are skipped entirely, so a store that has
/// earned everything sees no writes at all.
/// </summary>
public sealed class AchievementTracker
{
    private readonly IAchievementStore _store;

    public AchievementTracker(IAchievementStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Apply one event. Returns the ids unlocked by it, in catalog order —
    /// empty when nothing crossed its target.
    /// </summary>
    public IReadOnlyList<string> OnEvent(AchievementEvent evt)
    {
        List<string>? unlocked = null;

        foreach (AchievementDefinition def in AchievementCatalog.All)
        {
            if (_store.IsUnlocked(def.Id)) continue;

            int delta = def.Advance(evt);
            if (delta <= 0) continue;

            int current = Math.Min(_store.ProgressFor(def.Id) + delta, def.Target);
            _store.ReportProgress(def.Id, current, def.Target);
            Log.Debug(Log.LogCategory.Achieve,
                $"[award] progress {def.Id} {current}/{def.Target}");

            if (current < def.Target) continue;

            _store.Unlock(def.Id);
            Log.Info(Log.LogCategory.Achieve, $"[award] unlocked {def.Id}");
            (unlocked ??= new List<string>()).Add(def.Id);
        }

        Log.Trace(Log.LogCategory.Achieve,
            $"[eval] {AchievementCatalog.All.Count} definitions evaluated for {evt}");
        return (IReadOnlyList<string>?)unlocked ?? Array.Empty<string>();
    }
}
