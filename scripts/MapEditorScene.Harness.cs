// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// View-matrix harness seam (issue #63) for the map editor. Its HUD is the one
/// surface currently known to break below ~1300px wide (#226), so the cells
/// that reach it are the harness's first soft-fail customers.
/// </summary>
public partial class MapEditorScene : IHarnessNavigable
{
    private static readonly string[] HarnessScreens =
    {
        "palette",
        "esc-menu",
        "save-dialog",
        "export-dialog",
        "load-dialog",
    };

    IReadOnlyList<string> IHarnessNavigable.HarnessScreenIds => HarnessScreens;

    bool IHarnessNavigable.ShowHarnessScreen(string id)
    {
        switch (id)
        {
            case "palette":
                return true;   // the editor HUD is the resting state

            case "esc-menu":
                OpenEscMenu();
                return true;

            case "save-dialog":
                OpenSaveDialog();
                return true;

            case "export-dialog":
                OpenExportDialog();
                return true;

            case "load-dialog":
                OpenLoadDialog();
                return _loadDialog != null && _loadDialog.Visible;

            default:
                return false;
        }
    }

    void IHarnessNavigable.ResetHarnessScreen()
    {
        if (_escMenu.IsOpen) _escMenu.CloseAsEscape();
        _saveModal?.Close();
        _loadDialog?.Hide();
    }
}
