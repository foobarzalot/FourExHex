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
        string title = Strings.Get(StringKeys.CampaignLevelTitle,
            ("level", CampaignProgress.LabelFor(level)));
        string statusText = CampaignStore.Progress.StatusOf(level) switch
        {
            CampaignLevelStatus.Won => Strings.Get(StringKeys.CampaignStatusWon),
            CampaignLevelStatus.Lost => Strings.Get(StringKeys.CampaignStatusLost),
            _ => Strings.Get(StringKeys.CampaignStatusUnattempted),
        };
        string status = Strings.Get(StringKeys.CampaignTierStatus,
            ("tier", Strings.Get(StringKeys.ForDifficulty(CampaignProgress.DifficultyForLevel(level)))),
            ("status", statusText));

        // Tell the player which game mode the level runs. Complication modes
        // are the Soldier+ minority, so they get emphasized descriptions;
        // freeform levels get a one-liner for parity.
        GameMode mode = CampaignProgress.ModeForLevel(level);
        string gameMode = Strings.Get(mode switch
        {
            GameMode.RisingTides => StringKeys.CampaignBlurbRisingTides,
            GameMode.FogOfWar => StringKeys.CampaignBlurbFogOfWar,
            GameMode.VikingRaiders => StringKeys.CampaignBlurbVikingRaiders,
            _ => StringKeys.CampaignBlurbFreeform,
        });

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
                Player.BuildCampaignRoster(level),
                mode),
            gameMode: gameMode,
            gameModeEmphasis: mode != GameMode.Freeform);
    }
}
