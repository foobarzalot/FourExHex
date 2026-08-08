// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Text.Json;
using FourExHex.Model;

/// <summary>
/// JSON (de)serialization for the achievement sidecar file
/// <c>user://achievements.json</c>. Pure model — the Godot-side
/// <c>AchievementStore</c> only does file I/O around these two methods.
/// Deserialize throws on anything unreadable (corrupt JSON, unsupported
/// version); the store catches and falls back to a fresh record, and the
/// next unlock overwrites the file cleanly. Readable damage (blank ids,
/// duplicates, negative values) degrades gracefully via
/// <see cref="AchievementRecord.FromEntries"/> instead of throwing.
/// </summary>
public static class AchievementSerializer
{
    /// <summary>Bump on any breaking schema change. Unknown (future)
    /// versions are rejected rather than guessed at.</summary>
    public const int CurrentFormatVersion = 1;

    public static string Serialize(AchievementRecord record)
    {
        var data = new AchievementData
        {
            FormatVersion = CurrentFormatVersion,
            Entries = record.ToEntries(),
        };
        return JsonSerializer.Serialize(data, FourExHexJsonContext.Default.AchievementData);
    }

    public static AchievementRecord Deserialize(string json)
    {
        AchievementData? data =
            JsonSerializer.Deserialize(json, FourExHexJsonContext.Default.AchievementData);
        if (data == null)
        {
            throw new InvalidOperationException("Achievement file is empty or malformed.");
        }
        if (data.FormatVersion is < 1 or > CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported achievement format version {data.FormatVersion} " +
                $"(expected 1..{CurrentFormatVersion}).");
        }
        return AchievementRecord.FromEntries(data.Entries, AchievementRenames.Map);
    }
}

/// <summary>
/// Wire DTO for <see cref="AchievementSerializer"/>: a version stamp plus
/// one entry per achievement the record has ever heard of.
/// </summary>
public sealed class AchievementData
{
    public int FormatVersion { get; set; }
    public AchievementEntryData[]? Entries { get; set; }
}

/// <summary>
/// One persisted achievement. <see cref="Order"/> is the 1-based unlock
/// sequence, 0 meaning "not unlocked"; <see cref="Progress"/> is the best
/// value ever recorded toward the definition's target.
/// </summary>
public sealed class AchievementEntryData
{
    public string? Id { get; set; }
    public int Order { get; set; }
    public int Progress { get; set; }
}
