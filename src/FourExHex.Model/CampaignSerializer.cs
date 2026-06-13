using System;
using System.Text.Json;
using FourExHex.Model;

/// <summary>
/// JSON (de)serialization for the campaign sidecar file
/// <c>user://campaign.json</c> (issue #2). Pure model — the Godot-side
/// <c>CampaignStore</c> only does file I/O around these two methods.
/// Deserialize throws on anything unreadable (corrupt JSON, unsupported
/// version); the store catches and falls back to fresh progress, and the
/// next status change overwrites the file cleanly. Readable damage
/// (short / long / out-of-range status arrays) degrades gracefully via
/// <see cref="CampaignProgress.FromStatuses"/> instead of throwing.
/// </summary>
public static class CampaignSerializer
{
    /// <summary>Bump on any breaking schema change. Unknown (future)
    /// versions are rejected rather than guessed at.</summary>
    public const int CurrentFormatVersion = 1;

    public static string Serialize(CampaignProgress progress)
    {
        var data = new CampaignData
        {
            FormatVersion = CurrentFormatVersion,
            Statuses = progress.ToStatusArray(),
        };
        return JsonSerializer.Serialize(data, FourExHexJsonContext.Default.CampaignData);
    }

    public static CampaignProgress Deserialize(string json)
    {
        CampaignData? data = JsonSerializer.Deserialize(json, FourExHexJsonContext.Default.CampaignData);
        if (data == null)
        {
            throw new InvalidOperationException("Campaign file is empty or malformed.");
        }
        if (data.FormatVersion is < 1 or > CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported campaign format version {data.FormatVersion} " +
                $"(expected 1..{CurrentFormatVersion}).");
        }
        return CampaignProgress.FromStatuses(data.Statuses ?? Array.Empty<int>());
    }
}

/// <summary>
/// Wire DTO for <see cref="CampaignSerializer"/>: a version stamp plus
/// 256 numeric <see cref="CampaignLevelStatus"/> values (0=Untried,
/// 1=Lost, 2=Won — enum member order is load-bearing).
/// </summary>
public sealed class CampaignData
{
    public int FormatVersion { get; set; }
    public int[]? Statuses { get; set; }
}
