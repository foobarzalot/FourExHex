// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// View-matrix harness seam (issue #63) for the in-game scene. HudView is the
/// largest layout surface in the game, so this is the scene whose coverage
/// matters most.
/// </summary>
public partial class Main : IHarnessNavigable
{
    private static readonly string[] HarnessScreens =
    {
        "hud-idle",
        "pause-menu",
        "save-dialog",
        "load-dialog",
        "settings-from-pause",
        "restart-confirm",
        "overlay-victory",
        "overlay-defeat",
        "overlay-ai-won",
        "overlay-campaign-victory",
        "overlay-vikings-conquered",
        "overlay-claim-victory",
    };

    IReadOnlyList<string> IHarnessNavigable.HarnessScreenIds => HarnessScreens;

    bool IHarnessNavigable.ShowHarnessScreen(string id)
    {
        // Every screen here needs the real HudView; a diagnostic run swaps in
        // HeadlessHudView and would report a vacuous pass.
        if (_visibleHud == null) return false;

        switch (id)
        {
            case "hud-idle":
                return true;   // the board + HUD resting state

            case "pause-menu":
                EnterPause();
                return true;

            case "save-dialog":
                OpenSaveDialogFromPause();
                return true;

            case "load-dialog":
                OpenLoadDialogFromPause();
                return _loadDialog != null && _loadDialog.Visible;

            case "settings-from-pause":
                OpenSettingsFromPause();
                return true;

            case "restart-confirm":
                OpenRestartConfirmFromPause();
                return true;

            case "overlay-victory":
                return ShowOverlay(EndgameOverlayId.Victory);
            case "overlay-defeat":
                return ShowOverlay(EndgameOverlayId.Defeat);
            case "overlay-ai-won":
                return ShowOverlay(EndgameOverlayId.AiWon);
            case "overlay-campaign-victory":
                return ShowOverlay(EndgameOverlayId.CampaignVictory);
            case "overlay-vikings-conquered":
                return ShowOverlay(EndgameOverlayId.VikingsConquered);
            case "overlay-claim-victory":
                return ShowOverlay(EndgameOverlayId.ClaimVictory);

            default:
                return false;
        }
    }

    private bool ShowOverlay(EndgameOverlayId id)
    {
        _visibleHud!.ShowEndgameOverlayForHarness(id);
        return true;
    }

    void IHarnessNavigable.ResetHarnessScreen()
    {
        _visibleHud?.HideEndgameOverlaysForHarness();
        _settingsPanel.Close();
        _saveModal?.Close();
        _loadDialog?.Hide();
        _restartConfirmModal?.Close();
        if (_escMenu.IsOpen) _escMenu.CloseAsEscape();
    }
}
