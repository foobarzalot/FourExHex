// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

namespace FourExHex.Tests;

/// <summary>
/// Recording <see cref="IAchievementStore"/> for controller tests. Backed
/// by a real <see cref="AchievementRecord"/> so the read-back semantics
/// (append-only unlocks, progress that only rises) match production, while
/// every call is also logged so tests can assert what the tracker asked
/// for — including asserting that it asked for <em>nothing</em>.
/// </summary>
public sealed class FakeAchievementStore : IAchievementStore
{
    private readonly AchievementRecord _record = new();

    /// <summary>Every progress report, in order.</summary>
    public List<(string Id, int Current, int Target)> ProgressReports { get; } = new();

    /// <summary>Every unlock, in order.</summary>
    public List<string> Unlocks { get; } = new();

    /// <summary>Total calls of any kind — the "did anything happen?" probe.</summary>
    public int TotalCalls => ProgressReports.Count + Unlocks.Count;

    /// <summary>Forget the call log, keeping the earned record. Used to
    /// assert that a replay of an already-awarded game adds nothing.</summary>
    public void ClearCallLog()
    {
        ProgressReports.Clear();
        Unlocks.Clear();
    }

    public bool IsUnlocked(string id) => _record.IsUnlocked(id);

    public int ProgressFor(string id) => _record.ProgressFor(id);

    public void ReportProgress(string id, int current, int target)
    {
        ProgressReports.Add((id, current, target));
        _record.SetProgress(id, current);
    }

    public void Unlock(string id)
    {
        Unlocks.Add(id);
        _record.Unlock(id);
    }
}
