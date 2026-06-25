using System.Collections.Generic;
using Godot;

/// <summary>
/// Builds the campaign level "Play?" confirm sheet (issue #51) as a configured
/// <see cref="MapInfoSheet"/>: serif title, tier/status line, the single human
/// color for the level (issue #74), and a live preview of the level's exact
/// procedural board (level N = seed N). The sheet UI itself is shared with the
/// New Game / Map Editor "load starting map" flows; this factory just supplies
/// the campaign-specific content.
/// </summary>
public static class CampaignConfirmSheet
{
    public static MapInfoSheet Create(int level)
    {
        int seed = CampaignProgress.SeedForLevel(level);
        string title = $"Level {CampaignProgress.LabelFor(level)}";
        string statusText = CampaignStore.Progress.StatusOf(level) switch
        {
            CampaignLevelStatus.Won => "Already won — replaying can't lose it.",
            CampaignLevelStatus.Lost => "Attempted, not yet won.",
            _ => "Not yet attempted.",
        };
        string status = $"{CampaignProgress.DifficultyForLevel(level)} tier · {statusText}";

        // The single color the human plays this level (deterministic per level).
        int humanSlot = CampaignProgress.HumanSlotForLevel(level, GameSettings.PlayerConfig.Length);
        (string colorName, string colorHex) = GameSettings.PlayerConfig[humanSlot];
        var humans = new List<MapInfoSheet.HumanIdentity>
        {
            new(colorName, new Color(colorHex)),
        };

        return new MapInfoSheet(
            title,
            status,
            humans,
            // The level's fixed terrain features (issue #48) — derived from the
            // level, not the freeform New Game toggles — so the preview matches.
            thumb => thumb.RequestRandom(seed, CampaignProgress.MapGenOptionsForLevel(level)));
    }
}
