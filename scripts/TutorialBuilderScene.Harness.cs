// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// View-matrix harness seam (issue #63) for the tutorial builder. Its three
/// modes are three visually distinct screens reached through one call, which
/// makes it the cheapest scene in the sweep to cover well.
/// </summary>
public partial class TutorialBuilderScene : IHarnessNavigable
{
    private static readonly string[] HarnessScreens =
    {
        "mode-mapedit",
        "mode-record",
        "mode-preview",
        "esc-menu",
    };

    IReadOnlyList<string> IHarnessNavigable.HarnessScreenIds => HarnessScreens;

    bool IHarnessNavigable.ShowHarnessScreen(string id)
    {
        switch (id)
        {
            case "mode-mapedit":
                SetMode(TutorialMode.MapEdit);
                return true;

            case "mode-record":
                SetMode(TutorialMode.Record);
                return true;

            case "mode-preview":
                SetMode(TutorialMode.Preview);
                return true;

            case "esc-menu":
                OpenEscMenu();
                return true;

            default:
                return false;
        }
    }

    void IHarnessNavigable.ResetHarnessScreen()
    {
        if (_escMenu.IsOpen) _escMenu.CloseAsEscape();
        _loadDialog?.Hide();
    }
}
