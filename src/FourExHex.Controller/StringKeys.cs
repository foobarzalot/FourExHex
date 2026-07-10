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

    // HUD verb-carrying copy (tour bodies, prompts, action hints).
    public const string HudTourBuyBodyCollapsed = "hud.tour.buy.body.collapsed";
    public const string HudTourHelpBody = "hud.tour.help.body";
    public const string HudContinueHint = "hud.continue_hint";
    public const string HudHintTowerPickTile = "hud.hint.tower_pick_tile";
    public const string HudHintPlaceUnit = "hud.hint.place_unit";
    public const string HudHintMoveUnit = "hud.hint.move_unit";

    // Map editor.
    public const string EditorTooltipPaintLandCycle = "editor.tooltip.paint_land_cycle";
}
