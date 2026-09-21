// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;

/// <summary>
/// The authoritative local achievement record, persisted to
/// <c>user://achievements.json</c>. Pure model — Godot-free, serialized by
/// <see cref="AchievementSerializer"/>.
///
/// Three properties carry the design:
/// <list type="bullet">
/// <item>Unlocks are <b>append-only</b>. Nothing revokes one — platform
/// achievement APIs cannot revoke either, so the local record matches.</item>
/// <item>Progress only ever <b>rises</b>, so a counter can be reported from
/// any order of events without going backwards.</item>
/// <item>Ids are <b>never filtered against the catalog</b>. Whatever was
/// read is re-emitted on save, so a build that knows fewer achievements
/// can never destroy a record written by one that knows more. The catalog
/// is consulted for display only.</item>
/// <item>Per-level <b>credit</b> only rises. Each entry remembers the best
/// amount every campaign level has contributed to it, so a level replayed
/// to the same or a worse result contributes nothing more.</item>
/// </list>
/// </summary>
public sealed class AchievementRecord
{
    private sealed class Entry
    {
        public required string Id { get; init; }

        /// <summary>1-based unlock sequence; 0 means "not unlocked".</summary>
        public int Order { get; set; }

        public int Progress { get; set; }

        /// <summary>Campaign level → best amount that level has contributed.
        /// Sorted so the on-disk order is deterministic.</summary>
        public SortedDictionary<int, int> Credits { get; } = new();
    }

    /// <summary>Insertion order, which is also the on-disk order.</summary>
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
    private int _maxOrder;

    /// <summary>Fresh record: nothing unlocked, no progress.</summary>
    public AchievementRecord()
    {
    }

    /// <summary>
    /// Build a record from persisted entries (deserialize path), applying
    /// <paramref name="renames"/> as ids are read.
    ///
    /// Tolerant by design — a damaged file costs at worst some re-earnable
    /// progress, never a crash: a null list is an empty record, blank ids
    /// are skipped, negative values clamp to zero, and duplicate ids
    /// collapse (progress takes the max, order the lower non-zero, credit
    /// the max per level). A credit with a negative level or a non-positive
    /// amount is dropped. Unlock order is renumbered from 1 so a
    /// hand-edited or half-written file recovers a sane sequence.
    /// </summary>
    public static AchievementRecord FromEntries(
        IReadOnlyList<AchievementEntryData>? entries,
        IReadOnlyDictionary<string, string> renames)
    {
        var record = new AchievementRecord();
        if (entries == null) return record;

        foreach (AchievementEntryData data in entries)
        {
            string? id = data.Id;
            if (string.IsNullOrWhiteSpace(id)) continue;
            // Renames are applied once, at depth 1 — chains are not
            // followed. AchievementRenames pins that with a cycle guard.
            if (renames.TryGetValue(id, out string? renamed)) id = renamed;

            int order = Math.Max(0, data.Order);
            int progress = Math.Max(0, data.Progress);

            if (!record._byId.TryGetValue(id, out Entry? entry))
            {
                entry = new Entry { Id = id, Order = order, Progress = progress };
                record._entries.Add(entry);
                record._byId[id] = entry;
            }
            else
            {
                entry.Progress = Math.Max(entry.Progress, progress);
                entry.Order = LowerNonZero(entry.Order, order);
            }
            MergeCredits(entry, data.Credits);
        }

        record.RenumberUnlocks();
        return record;
    }

    /// <summary>True iff <paramref name="id"/> has been unlocked.</summary>
    public bool IsUnlocked(string id) =>
        _byId.TryGetValue(id, out Entry? entry) && entry.Order > 0;

    /// <summary>Best progress ever recorded toward <paramref name="id"/>, or 0.</summary>
    public int ProgressFor(string id) =>
        _byId.TryGetValue(id, out Entry? entry) ? entry.Progress : 0;

    /// <summary>Unlocked ids in the sequence they were earned. A future
    /// first-party sync replays this list to backfill on first sign-in.</summary>
    public IReadOnlyList<string> UnlockedInOrder
    {
        get
        {
            var unlocked = new List<Entry>();
            foreach (Entry entry in _entries)
            {
                if (entry.Order > 0) unlocked.Add(entry);
            }
            unlocked.Sort(static (a, b) => a.Order.CompareTo(b.Order));

            var ids = new string[unlocked.Count];
            for (int i = 0; i < unlocked.Count; i++) ids[i] = unlocked[i].Id;
            return ids;
        }
    }

    /// <summary>Unlock <paramref name="id"/>, assigning the next sequence
    /// number. Returns true iff this changed anything (caller saves);
    /// unlocking twice is a no-op, never an error.</summary>
    public bool Unlock(string id)
    {
        Entry entry = GetOrAdd(id);
        if (entry.Order > 0) return false;
        entry.Order = ++_maxOrder;
        return true;
    }

    /// <summary>Raise the recorded progress toward <paramref name="id"/>.
    /// Returns true iff this changed anything (caller saves); a value at or
    /// below the stored best is ignored so progress never regresses.</summary>
    public bool SetProgress(string id, int current)
    {
        if (current <= ProgressFor(id)) return false;
        GetOrAdd(id).Progress = current;
        return true;
    }

    /// <summary>Every entry, in on-disk order — including ids this build
    /// does not recognize (serialize path).</summary>
    public AchievementEntryData[] ToEntries()
    {
        var data = new AchievementEntryData[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
        {
            data[i] = new AchievementEntryData
            {
                Id = _entries[i].Id,
                Order = _entries[i].Order,
                Progress = _entries[i].Progress,
                Credits = CreditsToData(_entries[i].Credits),
            };
        }
        return data;
    }

    /// <summary>Null when the entry has no credit, so a credit-free entry
    /// keeps the compact three-field shape on disk.</summary>
    private static AchievementLevelCreditData[]? CreditsToData(SortedDictionary<int, int> credits)
    {
        if (credits.Count == 0) return null;
        var data = new AchievementLevelCreditData[credits.Count];
        int i = 0;
        foreach (KeyValuePair<int, int> pair in credits)
        {
            data[i++] = new AchievementLevelCreditData { Level = pair.Key, Amount = pair.Value };
        }
        return data;
    }

    /// <summary>Fold persisted credits into <paramref name="entry"/>, max
    /// per level; a null list, a null slot, a negative level, or a
    /// non-positive amount is readable damage and is skipped.</summary>
    private static void MergeCredits(Entry entry, AchievementLevelCreditData[]? credits)
    {
        if (credits == null) return;
        foreach (AchievementLevelCreditData? credit in credits)
        {
            if (credit == null || credit.Level < 0 || credit.Amount <= 0) continue;
            if (entry.Credits.TryGetValue(credit.Level, out int existing) && existing >= credit.Amount)
            {
                continue;
            }
            entry.Credits[credit.Level] = credit.Amount;
        }
    }

    /// <summary>Best amount campaign <paramref name="level"/> has ever
    /// contributed toward <paramref name="id"/>, or 0.</summary>
    public int CreditFor(string id, int level) =>
        _byId.TryGetValue(id, out Entry? entry)
            && entry.Credits.TryGetValue(level, out int amount)
            ? amount
            : 0;

    /// <summary>Raise the amount campaign <paramref name="level"/> has
    /// contributed toward <paramref name="id"/>. Returns true iff this
    /// changed anything (caller saves); a value at or below the stored best
    /// is ignored so credit never regresses.</summary>
    public bool SetCredit(string id, int level, int amount)
    {
        if (amount <= CreditFor(id, level)) return false;
        GetOrAdd(id).Credits[level] = amount;
        return true;
    }

    private Entry GetOrAdd(string id)
    {
        if (_byId.TryGetValue(id, out Entry? existing)) return existing;
        var entry = new Entry { Id = id };
        _entries.Add(entry);
        _byId[id] = entry;
        return entry;
    }

    /// <summary>Compact unlock order to 1..N, preserving relative sequence
    /// (ties broken by on-disk position).</summary>
    private void RenumberUnlocks()
    {
        var unlocked = new List<Entry>();
        foreach (Entry entry in _entries)
        {
            if (entry.Order > 0) unlocked.Add(entry);
        }
        unlocked.Sort(static (a, b) => a.Order.CompareTo(b.Order));

        for (int i = 0; i < unlocked.Count; i++) unlocked[i].Order = i + 1;
        _maxOrder = unlocked.Count;
    }

    private static int LowerNonZero(int a, int b)
    {
        if (a == 0) return b;
        if (b == 0) return a;
        return Math.Min(a, b);
    }
}
