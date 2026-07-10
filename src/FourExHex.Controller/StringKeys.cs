/// <summary>
/// The canonical list of user-facing string keys — one <c>const</c> per
/// entry in <c>assets/strings/en.json</c>. Call sites pass these to
/// <see cref="Strings.Get"/> instead of raw string literals so a typo is a
/// compile error, and <c>StringKeysParityTests</c> pins exact two-way
/// agreement between these consts and the JSON (an entry added on one side
/// only fails <c>dotnet test</c>).
///
/// Naming: dotted lowercase, area-prefixed (<c>hud.tooltip.*</c>,
/// <c>menu.*</c>, <c>endgame.*</c>, <c>editor.*</c>, <c>tutorial.*</c>).
/// Const identifiers PascalCase the key path.
/// </summary>
public static class StringKeys
{
    // Platform-aware interaction verb (built-in {Verb}/{verb} tokens —
    // see StringTable). Data replaces the old code branch.
    public const string VerbCapitalizedDesktop = "verb.capitalized.desktop";
    public const string VerbCapitalizedMobile = "verb.capitalized.mobile";
    public const string VerbLowercaseDesktop = "verb.lowercase.desktop";
    public const string VerbLowercaseMobile = "verb.lowercase.mobile";

    // Unit display names (see also Strings.UnitName / ForUnit below).
    public const string UnitRecruit = "unit.recruit";
    public const string UnitSoldier = "unit.soldier";
    public const string UnitCaptain = "unit.captain";
    public const string UnitCommander = "unit.commander";

    /// <summary>The display-name key for a unit level.</summary>
    public static string ForUnit(UnitLevel level) => level switch
    {
        UnitLevel.Recruit => UnitRecruit,
        UnitLevel.Soldier => UnitSoldier,
        UnitLevel.Captain => UnitCaptain,
        _ => UnitCommander,
    };

    // Game-over overlay (EndgameOverlayContent).
    public const string EndgameDefeatEyebrow = "endgame.defeat.eyebrow";
    public const string EndgameVictoryEyebrow = "endgame.victory.eyebrow";
    public const string EndgameVikingConquestTitle = "endgame.viking_conquest.title";
    public const string EndgameDefeatedTitle = "endgame.defeated.title";
    public const string EndgameVictoryTitle = "endgame.victory.title";

    // Viking Raiders wave banner (VikingWaveBannerContent).
    public const string VikingWaveFinalSpawned = "viking.wave.final_spawned";
    public const string VikingWaveSpawned = "viking.wave.spawned";
    public const string VikingWaveFinalIncoming = "viking.wave.final_incoming";
    public const string VikingWaveIncoming = "viking.wave.incoming";
    public const string VikingTurnsOne = "viking.turns.one";
    public const string VikingTurnsMany = "viking.turns.many";

    // Tutorial Preview instructions (TutorialInstructionText).
    public const string TutorialEndTurn = "tutorial.end_turn";
    public const string TutorialTowerPlace = "tutorial.tower.place";
    public const string TutorialTowerPressButton = "tutorial.tower.press_button";
    public const string TutorialRally = "tutorial.rally";
    public const string TutorialClaimWinNow = "tutorial.claim.win_now";
    public const string TutorialClaimContinue = "tutorial.claim.continue";
    public const string TutorialDefeatContinue = "tutorial.defeat.continue";
    public const string TutorialMovePickUp = "tutorial.move.pick_up";
    public const string TutorialMoveCombine = "tutorial.move.combine";
    public const string TutorialMoveClearTree = "tutorial.move.clear_tree";
    public const string TutorialMoveRemoveGrave = "tutorial.move.remove_grave";
    public const string TutorialMoveCaptureTower = "tutorial.move.capture_tower";
    public const string TutorialMoveCaptureTree = "tutorial.move.capture_tree";
    public const string TutorialMoveCapture = "tutorial.move.capture";
    public const string TutorialMoveGeneric = "tutorial.move.generic";
    public const string TutorialBuyPressRecruit = "tutorial.buy.press_recruit";
    public const string TutorialBuyUpgrade = "tutorial.buy.upgrade";
    public const string TutorialBuyCycle = "tutorial.buy.cycle";
    public const string TutorialPlaceCombine = "tutorial.place.combine";
    public const string TutorialPlaceGeneric = "tutorial.place.generic";
    public const string TutorialPlaceCaptureTower = "tutorial.place.capture_tower";
    public const string TutorialPlaceCaptureTree = "tutorial.place.capture_tree";
    public const string TutorialPlaceCaptureGrave = "tutorial.place.capture_grave";
    public const string TutorialPlaceCapture = "tutorial.place.capture";
    public const string TutorialPlaceClearTree = "tutorial.place.clear_tree";
    public const string TutorialPlaceRemoveGrave = "tutorial.place.remove_grave";

    // Roster fallback when a player id has no roster entry.
    public const string PlayerUnknown = "player.unknown";

    // HUD chrome.
    public const string HudEyebrowTurn = "hud.eyebrow.turn";
    public const string HudChipGold = "hud.chip.gold";

    // HUD button tooltips (HudIconButton.DefaultTooltip + HudView state
    // overrides).
    public const string HudTooltipBuyRecruit = "hud.tooltip.buy_recruit";
    public const string HudTooltipBuildTower = "hud.tooltip.build_tower";
    public const string HudTooltipUndoLast = "hud.tooltip.undo_last";
    public const string HudTooltipUndoAll = "hud.tooltip.undo_all";
    public const string HudTooltipRedoLast = "hud.tooltip.redo_last";
    public const string HudTooltipRedoAll = "hud.tooltip.redo_all";
    public const string HudTooltipEndTurn = "hud.tooltip.end_turn";
    public const string HudTooltipNextUnit = "hud.tooltip.next_unit";
    public const string HudTooltipNextTerritory = "hud.tooltip.next_territory";
    public const string HudTooltipOptions = "hud.tooltip.options";
    public const string HudTooltipGenerateMap = "hud.tooltip.generate_map";
    public const string HudTooltipAddNarration = "hud.tooltip.add_narration";
    public const string HudTooltipAutomate = "hud.tooltip.automate";
    public const string HudTooltipAutomateStop = "hud.tooltip.automate_stop";
    public const string HudTooltipBuyCycle = "hud.tooltip.buy_cycle";
    public const string HudTooltipUndoHold = "hud.tooltip.undo_hold";
    public const string HudTooltipRedoHold = "hud.tooltip.redo_hold";
    public const string HudTooltipNextUnitHold = "hud.tooltip.next_unit_hold";
    public const string HudTooltipHelp = "hud.tooltip.help";
    public const string HudTooltipNoUnmovedUnits = "hud.tooltip.no_unmoved_units";
    public const string HudTooltipBuyUnit = "hud.tooltip.buy_unit";

    // Disabled-reason tooltips + their {action} fragments.
    public const string HudDisabledNoSelection = "hud.disabled.no_selection";
    public const string HudDisabledNoCapital = "hud.disabled.no_capital";
    public const string HudDisabledCantAfford = "hud.disabled.cant_afford";
    public const string HudActionARecruit = "hud.action.a_recruit";
    public const string HudActionASoldier = "hud.action.a_soldier";
    public const string HudActionACaptain = "hud.action.a_captain";
    public const string HudActionACommander = "hud.action.a_commander";
    public const string HudActionAUnit = "hud.action.a_unit";
    public const string HudActionATower = "hud.action.a_tower";

    /// <summary>The "a recruit"-style {action} fragment key for buying a
    /// unit of <paramref name="level"/>.</summary>
    public static string ForBuyAction(UnitLevel level) => level switch
    {
        UnitLevel.Recruit => HudActionARecruit,
        UnitLevel.Soldier => HudActionASoldier,
        UnitLevel.Captain => HudActionACaptain,
        _ => HudActionACommander,
    };

    // Economy toast.
    public const string HudBankruptTitle = "hud.bankrupt.title";
    public const string HudBankruptBody = "hud.bankrupt.body";
    public const string HudLosingGoldTitle = "hud.losing_gold.title";
    public const string HudLosingGoldBody = "hud.losing_gold.body";

    // Endgame / claim overlays (HudView-built chrome; the winner-dependent
    // eyebrow/title come from EndgameOverlayContent's endgame.* keys).
    public const string HudOverlayCheckpointEyebrow = "hud.overlay.checkpoint.eyebrow";
    public const string HudOverlayClaimVictoryTitle = "hud.overlay.claim_victory.title";
    public const string HudOverlayCampaignEyebrow = "hud.overlay.campaign.eyebrow";
    public const string HudCampaignLevelWon = "hud.campaign.level_won";
    public const string HudCampaignProgress = "hud.campaign.progress";
    public const string HudButtonPlayAgain = "hud.button.play_again";
    public const string HudButtonReplay = "hud.button.replay";
    public const string HudButtonMainMenu = "hud.button.main_menu";
    public const string HudButtonContinue = "hud.button.continue";
    public const string HudButtonWinNow = "hud.button.win_now";
    public const string HudButtonContinuePlaying = "hud.button.continue_playing";
    public const string HudButtonNextUnbeaten = "hud.button.next_unbeaten";
    public const string HudButtonBackToCampaign = "hud.button.back_to_campaign";

    // Guided UI tour (HudView.BuildTourSteps).
    public const string HudTourTurnTitle = "hud.tour.turn.title";
    public const string HudTourTurnBody = "hud.tour.turn.body";
    public const string HudTourTreasuryTitle = "hud.tour.treasury.title";
    public const string HudTourTreasuryBody = "hud.tour.treasury.body";
    public const string HudTourBuyTitle = "hud.tour.buy.title";
    public const string HudTourBuyBody = "hud.tour.buy.body";
    public const string HudTourBuyBodyCollapsed = "hud.tour.buy.body.collapsed";
    public const string HudTourTowerTitle = "hud.tour.tower.title";
    public const string HudTourTowerBody = "hud.tour.tower.body";
    public const string HudTourUndoTitle = "hud.tour.undo.title";
    public const string HudTourUndoBody = "hud.tour.undo.body";
    public const string HudTourNextUnitTitle = "hud.tour.next_unit.title";
    public const string HudTourNextUnitBody = "hud.tour.next_unit.body";
    public const string HudTourNextTerritoryTitle = "hud.tour.next_territory.title";
    public const string HudTourNextTerritoryBody = "hud.tour.next_territory.body";
    public const string HudTourEndTurnTitle = "hud.tour.end_turn.title";
    public const string HudTourEndTurnBody = "hud.tour.end_turn.body";
    public const string HudTourAutomateTitle = "hud.tour.automate.title";
    public const string HudTourAutomateBody = "hud.tour.automate.body";
    public const string HudTourOptionsTitle = "hud.tour.options.title";
    public const string HudTourOptionsBody = "hud.tour.options.body";
    public const string HudTourHelpTitle = "hud.tour.help.title";
    public const string HudTourHelpBody = "hud.tour.help.body";

    // Bottom-panel prompts / action hints.
    public const string HudContinueHint = "hud.continue_hint";
    public const string HudHintTowerPickTile = "hud.hint.tower_pick_tile";
    public const string HudHintPlaceUnit = "hud.hint.place_unit";
    public const string HudHintMoveUnit = "hud.hint.move_unit";
    public const string HudHintNoCaptureTargets = "hud.hint.no_capture_targets";

    // Map editor.
    public const string EditorTooltipPaintLandCycle = "editor.tooltip.paint_land_cycle";

    // Main menu (landing, New Game setup, source choosers, dialogs).
    public const string MenuWordmark = "menu.wordmark";
    public const string MenuResume = "menu.resume";
    public const string MenuPlayGame = "menu.play_game";
    public const string MenuCampaign = "menu.campaign";
    public const string MenuPlayTutorial = "menu.play_tutorial";
    public const string MenuLoadGame = "menu.load_game";
    public const string MenuMapEditor = "menu.map_editor";
    public const string MenuSettings = "menu.settings";
    public const string MenuExit = "menu.exit";
    public const string MenuNewGame = "menu.new_game";
    public const string MenuBack = "menu.back";
    public const string MenuNext = "menu.next";
    public const string MenuStartGame = "menu.start_game";
    public const string MenuCreateMap = "menu.create_map";
    public const string MenuGameMode = "menu.game_mode";
    public const string MenuType = "menu.type";
    public const string MenuDifficulty = "menu.difficulty";
    public const string MenuConfigureGame = "menu.configure_game";
    public const string MenuLoadStartingMap = "menu.load_starting_map";
    public const string MenuQuickPlay = "menu.quick_play";
    public const string MenuNewMap = "menu.new_map";
    public const string MenuLoadMap = "menu.load_map";
    public const string MenuNoMapsFound = "menu.no_maps_found";
    public const string MenuNoSavesFound = "menu.no_saves_found";
    public const string MenuLoadFailed = "menu.load_failed";
    public const string MenuCouldNotLoadMap = "menu.could_not_load_map";
    public const string MenuCouldNotLoad = "menu.could_not_load";
    public const string MenuExitTitle = "menu.exit_title";
    public const string MenuExitBody = "menu.exit_body";

    // Game-mode display names (setup dropdown).
    public const string ModeFreeform = "mode.freeform";
    public const string ModeRisingTides = "mode.rising_tides";
    public const string ModeFogOfWar = "mode.fog_of_war";
    public const string ModeVikingRaiders = "mode.viking_raiders";

    /// <summary>The display-name key for a game mode.</summary>
    public static string ForMode(GameMode mode) => mode switch
    {
        GameMode.RisingTides => ModeRisingTides,
        GameMode.FogOfWar => ModeFogOfWar,
        GameMode.VikingRaiders => ModeVikingRaiders,
        _ => ModeFreeform,
    };

    // Player-kind display names (setup dropdown).
    public const string PlayerKindHuman = "player_kind.human";
    public const string PlayerKindComputer = "player_kind.computer";
    public const string PlayerKindNone = "player_kind.none";

    /// <summary>The display-name key for a difficulty tier (tiers share
    /// the unit-level names).</summary>
    public static string ForDifficulty(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Recruit => UnitRecruit,
        Difficulty.Soldier => UnitSoldier,
        Difficulty.Captain => UnitCaptain,
        _ => UnitCommander,
    };

    // Settings panel.
    public const string SettingsSoundEffects = "settings.sound_effects";
    public const string SettingsVisualEffects = "settings.visual_effects";
    public const string SettingsAiSpeed = "settings.ai_speed";
    public const string SettingsAutomateSpeed = "settings.automate_speed";
    public const string SettingsReplaySpeed = "settings.replay_speed";
    public const string SettingsCredits = "settings.credits";
    public const string SpeedSlow = "speed.slow";
    public const string SpeedNormal = "speed.normal";
    public const string SpeedFast = "speed.fast";
    public const string SpeedInstant = "speed.instant";

    // Shared dialog buttons.
    public const string ButtonCancel = "button.cancel";
    public const string ButtonSave = "button.save";
    public const string ButtonLoad = "button.load";
    public const string ButtonPlay = "button.play";

    // Save/load dialogs.
    public const string SaveTitleGame = "save.title_game";
    public const string SaveSlotName = "save.slot_name";
    public const string SaveFailed = "save.failed";
    public const string SaveCouldNotSave = "save.could_not_save";
    public const string SaveSlotRow = "save.slot_row";
    public const string SaveAutosaveRow = "save.autosave_row";

    // Credits panel ({url} = repo link).
    public const string CreditsBody = "credits.body";

    // Map-info / play-confirm sheet.
    public const string MapInfoPlayingAsHeading = "mapinfo.playing_as_heading";
    public const string MapInfoPlayingAs = "mapinfo.playing_as";
    public const string MapInfoAllComputer = "mapinfo.all_computer";

    // Campaign confirm sheet.
    public const string CampaignLevelTitle = "campaign.level_title";
    public const string CampaignTierStatus = "campaign.tier_status";
    public const string CampaignStatusWon = "campaign.status.won";
    public const string CampaignStatusLost = "campaign.status.lost";
    public const string CampaignStatusUnattempted = "campaign.status.unattempted";
    public const string CampaignBlurbFreeform = "campaign.blurb.freeform";
    public const string CampaignBlurbRisingTides = "campaign.blurb.rising_tides";
    public const string CampaignBlurbFogOfWar = "campaign.blurb.fog_of_war";
    public const string CampaignBlurbVikingRaiders = "campaign.blurb.viking_raiders";

    // First-encounter intros (game modes + terrain features).
    public const string IntroRisingTides = "intro.rising_tides";
    public const string IntroFogOfWar = "intro.fog_of_war";
    public const string IntroVikingRaiders = "intro.viking_raiders";
    public const string IntroGoldHex = "intro.gold_hex";
    public const string IntroMountainHex = "intro.mountain_hex";

    // In-game chrome owned by Main.
    public const string MainMapLabel = "main.map_label";
    public const string MainSeedLabel = "main.seed_label";
    public const string PauseTitle = "pause.title";
    public const string PauseExitGame = "pause.exit_game";
}
