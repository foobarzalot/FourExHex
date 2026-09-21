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
    private readonly AchievementRecord _record;

    public FakeAchievementStore() : this(new AchievementRecord())
    {
    }

    /// <summary>Wrap an existing record — e.g. one round-tripped through
    /// <see cref="AchievementSerializer"/> to model a process restart.</summary>
    public FakeAchievementStore(AchievementRecord record)
    {
        _record = record;
    }

    /// <summary>The backing record, for serializing "what would be on disk".</summary>
    public AchievementRecord Record => _record;

    /// <summary>Every progress report, in order.</summary>
    public List<(string Id, int Current, int Target)> ProgressReports { get; } = new();

    /// <summary>Every unlock, in order.</summary>
    public List<string> Unlocks { get; } = new();

    /// <summary>Every per-level credit report, in order.</summary>
    public List<(string Id, int Level, int Amount)> Credits { get; } = new();

    /// <summary>Total calls of any kind — the "did anything happen?" probe.</summary>
    public int TotalCalls => ProgressReports.Count + Unlocks.Count + Credits.Count;

    /// <summary>Forget the call log, keeping the earned record. Used to
    /// assert that a replay of an already-awarded game adds nothing.</summary>
    public void ClearCallLog()
    {
        ProgressReports.Clear();
        Unlocks.Clear();
        Credits.Clear();
    }

    public bool IsUnlocked(string id) => _record.IsUnlocked(id);

    public int ProgressFor(string id) => _record.ProgressFor(id);

    public int CreditFor(string id, int level) => _record.CreditFor(id, level);

    public void ReportCredit(string id, int level, int amount)
    {
        Credits.Add((id, level, amount));
        _record.SetCredit(id, level, amount);
    }

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
