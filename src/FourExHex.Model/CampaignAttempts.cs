// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FourExHex.Model;

/// <summary>What a level's stored attempt file holds.</summary>
public enum CampaignAttemptKind
{
    /// <summary>No readable attempt stored.</summary>
    None,
    /// <summary>A game in progress — Continue resumes it.</summary>
    Unfinished,
    /// <summary>A game that reached its end — Watch Replay plays it back.</summary>
    Finished,
}

/// <summary>
/// The Godot-free half of per-level campaign attempt storage. Each campaign
/// level keeps its most recent attempt as a gzipped save
/// (<c>user://campaign_saves/level_XX.json.gz</c>, see
/// <see cref="SaveCompression"/>); the Godot-side <c>CampaignStore</c> owns
/// the file I/O and calls these for naming and classification.
/// </summary>
public static class CampaignAttempts
{
    public static CampaignAttemptKind Classify(LoadedSave? save) => save switch
    {
        null => CampaignAttemptKind.None,
        { IsFinished: true } => CampaignAttemptKind.Finished,
        _ => CampaignAttemptKind.Unfinished,
    };

    /// <summary>A finished attempt is watchable only if its replay survived
    /// the <see cref="SaveSerializer.CurrentReplayVersion"/> gate on load.</summary>
    public static bool CanReplay(LoadedSave save) => save.Replay != null;

    /// <summary>Slot name of a level's attempt file (no extension).</summary>
    public static string FileNameFor(int level) => "level_" + CampaignProgress.LabelFor(level);
}

/// <summary>
/// One row of the attempt index: enough to rank attempts by recency and
/// tell finished from unfinished without opening the gzipped file.
/// </summary>
public readonly record struct CampaignAttemptEntry(
    int Level, long SavedAtUnix, int TurnNumber, int? WinnerIndex)
{
    public bool IsFinished => WinnerIndex != null;
}

/// <summary>
/// The recency index sidecar (<c>user://campaign_attempts.json</c>), one
/// entry per level with a stored attempt. It exists so the landing page's
/// Resume pick (<see cref="ResumeTarget.Pick"/>) never has to inflate up to
/// 256 attempt files; the attempt file stays authoritative when a level is
/// opened. Mutate via <see cref="Set"/> / <see cref="Remove"/>.
/// </summary>
public sealed class CampaignAttemptIndex
{
    private readonly Dictionary<int, CampaignAttemptEntry> _byLevel = new();

    public IReadOnlyCollection<CampaignAttemptEntry> Entries => _byLevel.Values;

    public void Set(CampaignAttemptEntry entry) => _byLevel[entry.Level] = entry;

    public bool Remove(int level) => _byLevel.Remove(level);

    /// <summary>
    /// Tolerant read, like <see cref="CampaignProgress.FromStatuses"/>:
    /// out-of-range levels dropped, duplicate levels keep the newest
    /// <c>SavedAtUnix</c>, negative timestamps and turns clamp to zero.
    /// </summary>
    public static CampaignAttemptIndex FromEntries(IEnumerable<CampaignAttemptEntry>? entries)
    {
        var index = new CampaignAttemptIndex();
        if (entries == null) return index;
        foreach (CampaignAttemptEntry raw in entries)
        {
            if (raw.Level < 0 || raw.Level >= CampaignProgress.LevelCount) continue;
            var entry = new CampaignAttemptEntry(
                raw.Level, Math.Max(0, raw.SavedAtUnix), Math.Max(0, raw.TurnNumber), raw.WinnerIndex);
            if (index._byLevel.TryGetValue(entry.Level, out CampaignAttemptEntry existing)
                && existing.SavedAtUnix >= entry.SavedAtUnix)
            {
                continue;
            }
            index._byLevel[entry.Level] = entry;
        }
        return index;
    }
}

/// <summary>
/// JSON (de)serialization for <see cref="CampaignAttemptIndex"/>. Same
/// posture as <see cref="CampaignSerializer"/>: unreadable input throws
/// (the store catches and rebuilds), readable damage degrades via
/// <see cref="CampaignAttemptIndex.FromEntries"/>.
/// </summary>
public static class CampaignAttemptIndexSerializer
{
    public const int CurrentFormatVersion = 1;

    public static string Serialize(CampaignAttemptIndex index)
    {
        var data = new CampaignAttemptIndexData
        {
            FormatVersion = CurrentFormatVersion,
            Entries = index.Entries
                .OrderBy(e => e.Level)
                .Select(e => new CampaignAttemptEntryData
                {
                    Level = e.Level,
                    SavedAtUnix = e.SavedAtUnix,
                    TurnNumber = e.TurnNumber,
                    WinnerIndex = e.WinnerIndex,
                })
                .ToList(),
        };
        return JsonSerializer.Serialize(data, FourExHexJsonContext.Default.CampaignAttemptIndexData);
    }

    public static CampaignAttemptIndex Deserialize(string json)
    {
        CampaignAttemptIndexData? data = JsonSerializer.Deserialize(
            json, FourExHexJsonContext.Default.CampaignAttemptIndexData);
        if (data == null)
        {
            throw new InvalidOperationException("Campaign attempt index is empty or malformed.");
        }
        if (data.FormatVersion is < 1 or > CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported campaign attempt index version {data.FormatVersion} " +
                $"(expected 1..{CurrentFormatVersion}).");
        }
        return CampaignAttemptIndex.FromEntries(
            data.Entries?.Select(d => new CampaignAttemptEntry(
                d.Level, d.SavedAtUnix, d.TurnNumber, d.WinnerIndex)));
    }
}

/// <summary>Wire DTO for <see cref="CampaignAttemptIndexSerializer"/>.</summary>
public sealed class CampaignAttemptIndexData
{
    public int FormatVersion { get; set; }
    public List<CampaignAttemptEntryData>? Entries { get; set; }
}

public sealed class CampaignAttemptEntryData
{
    public int Level { get; set; }
    public long SavedAtUnix { get; set; }
    public int TurnNumber { get; set; }
    public int? WinnerIndex { get; set; }
}

/// <summary>
/// What the landing page's Resume button opens: the most recently saved
/// <em>unfinished</em> game across the freeform autosave slot and every
/// stored campaign attempt. <see cref="CampaignLevel"/> null = the autosave.
/// </summary>
public readonly record struct ResumeTarget(int? CampaignLevel)
{
    public bool IsAutosave => CampaignLevel == null;

    /// <summary>Null when nothing is resumable. Ties go to the autosave.</summary>
    public static ResumeTarget? Pick(long? autosaveSavedAtUnix, IEnumerable<CampaignAttemptEntry> attempts)
    {
        ResumeTarget? best = autosaveSavedAtUnix != null ? new ResumeTarget(null) : null;
        long bestAt = autosaveSavedAtUnix ?? long.MinValue;
        foreach (CampaignAttemptEntry entry in attempts)
        {
            if (entry.IsFinished) continue;
            if (best != null && entry.SavedAtUnix <= bestAt) continue;
            best = new ResumeTarget(entry.Level);
            bestAt = entry.SavedAtUnix;
        }
        return best;
    }
}
