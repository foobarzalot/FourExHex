using System.Collections.Generic;
using Godot;

/// <summary>
/// Builds the campaign level "Play?" confirm sheet as a configured
/// <see cref="MapInfoSheet"/>: serif title, tier/status line, the single human
/// color for the level, and a live preview of the level's exact
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

        // Tell the player which game mode the level runs. Rising
        // Tides is the rare Soldier+ complication, so it gets an emphasized
        // description; freeform levels get a one-liner for parity.
        GameMode mode = CampaignProgress.ModeForLevel(level);
        string gameMode = mode == GameMode.RisingTides
            ? "Rising Tides — your coastline sinks a little each turn and the map shrinks; outlast the sea to be the last player standing."
            : "Freeform — expand your territory and outlast your rivals.";

        // The single color the human plays this level (deterministic per level).
        int humanSlot = CampaignProgress.HumanColorSlotForLevel(level);
        (string colorName, string colorHex) = GameSettings.PlayerConfig[humanSlot];
        var humans = new List<MapInfoSheet.HumanIdentity>
        {
            new(colorName, new Color(colorHex)),
        };

        return new MapInfoSheet(
            title,
            status,
            humans,
            // Preview the exact board the level launches: its per-level roster
            // (2–6 colors) and fixed terrain features — not the freeform
            // roster/toggles — so the thumbnail matches the game.
            thumb => thumb.RequestRandom(
                seed,
                CampaignProgress.MapGenOptionsForLevel(level),
                Player.BuildCampaignRoster(level)),
            gameMode: gameMode,
            gameModeEmphasis: mode == GameMode.RisingTides);
    }
}
