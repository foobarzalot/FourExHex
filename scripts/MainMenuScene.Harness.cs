// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// View-matrix harness seam (issue #63) for the main menu. Kept in its own
/// partial so the screen list sits next to nothing else and the main file is
/// untouched; the navigation methods it calls stay private.
/// </summary>
public partial class MainMenuScene : IHarnessNavigable
{
    private static readonly string[] HarnessScreens =
    {
        "landing",
        "play-config-players",
        "play-config-map",
        "campaign",
        "load-slots",
        "settings",
        "credits",
        "bug-report",
    };

    IReadOnlyList<string> IHarnessNavigable.HarnessScreenIds => HarnessScreens;

    bool IHarnessNavigable.ShowHarnessScreen(string id)
    {
        switch (id)
        {
            case "landing":
                ShowLanding();
                return true;

            case "play-config-players":
                ShowPlayConfig(PlayConfigPurpose.NewGame);
                return true;

            case "play-config-map":
                ShowPlayConfig(PlayConfigPurpose.NewGame);
                GoToMapPage();
                return true;

            case "campaign":
                ShowCampaign();
                return true;

            case "load-slots":
                OnLoadPressed();
                // The picker only materializes when there are saves to pick.
                return _loadDialog != null && _loadDialog.Visible;

            case "settings":
                if (_settingsPanel == null) return false;
                _settingsPanel.Open();
                return true;

            case "credits":
                if (_settingsPanel == null) return false;
                _settingsPanel.Open();
                _settingsPanel.OpenCreditsForHarness();
                return true;

            case "bug-report":
                if (_settingsPanel == null) return false;
                _settingsPanel.Open();
                _settingsPanel.OpenBugReportForHarness();
                return true;

            default:
                return false;
        }
    }

    void IHarnessNavigable.ResetHarnessScreen()
    {
        _settingsPanel?.CloseChildPanelsForHarness();
        _settingsPanel?.Close();
        _loadDialog?.Hide();
        ShowLanding();
    }
}
