// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// End-user "Play Tutorial" scene, reached from the main menu. A
/// chrome-free host that plays the bundled <c>full_tutorial</c> by reusing
/// the TutorialBuilder's playback machinery: it instantiates a
/// <see cref="MapEditorPanel"/> (the map view) plus a <see cref="PreviewPane"/>
/// (the scripted-playback controller + HUD), loads the bundled tutorial
/// from <c>res://tutorials/</c>, and calls <see cref="PreviewPane.Start"/>.
///
/// <para>
/// No editor chrome (no palette HUD / Record pane / undo). ESC opens a
/// minimal pause modal (Resume / Main Menu) via the shared
/// <see cref="EscMenu"/>. The victory overlay's buttons (Replay / Play
/// Again / Main Menu) are handled inside <see cref="PreviewPane"/> itself.
/// </para>
/// </summary>
public partial class PlayTutorialScene : Node2D
{
    /// <summary>The bundled tutorial this scene plays. Lives in
    /// <c>res://tutorials/</c> (committed to the repo, shipped with the
    /// game) — see <see cref="SaveStore.LoadBundledTutorial"/>.</summary>
    private const string TutorialName = "full_tutorial";

    private MapEditorPanel _panel = null!;
    private PreviewPane _preview = null!;
    private EscMenu _escMenu = null!;

    public override void _Ready()
    {
        // Reuse the map view + scripted-playback pane the TutorialBuilder
        // uses; just skip every editor surface. Mirrors
        // TutorialBuilderScene._Ready's panel/pane/escmenu construction.
        // Players must be set BEFORE AddChild (MapEditorPanel._Ready
        // asserts it). Start with the all-human 6-slot roster the builder
        // uses; after LoadFromMap we trim it to only the colors that own
        // land so landless slots don't show as players.
        // PreviewPane.Start then overrides kinds (player 0 Human, rest Computer).
        _panel = new MapEditorPanel { Players = Player.BuildAllHumanRoster() };
        AddChild(_panel);
        _panel.PaintingEnabled = false;

        _preview = new PreviewPane();
        _preview.SetPanel(_panel);
        _preview.EscRequested += OpenPauseMenu;
        AddChild(_preview);

        _escMenu = new EscMenu();
        AddChild(_escMenu);

        LoadedSave loaded;
        try
        {
            loaded = new SaveStore().LoadBundledTutorial(TutorialName);
        }
        catch (System.Exception ex)
        {
            Log.Error(Log.LogCategory.Tutorial,
                $"[PlayTutorial] could not load bundled tutorial '{TutorialName}': {ex.Message}");
            ReturnToMainMenu();
            return;
        }

        if (loaded.Tutorial?.Replay == null)
        {
            Log.Error(Log.LogCategory.Tutorial,
                $"[PlayTutorial] bundled save '{TutorialName}' has no tutorial/replay payload");
            ReturnToMainMenu();
            return;
        }

        // Load the tutorial's map into the panel, then reset the grid to
        // the recording's starting frame (mirrors
        // TutorialBuilderScene.OnLoadSlotPressed) and start playback.
        _panel.LoadFromMap(loaded);

        // Trim the roster to only the colors that actually own land on the
        // painted board. The bundled map declares 6 colors but
        // only 3 hold territory; the unowned ones would otherwise render as
        // dead swatches. PreviewPane.Start reads _panel.Players and assigns
        // kinds by position (index 0 Human, rest Computer), so the surviving
        // slots keep their colors and the HUD shows one swatch per owner.
        _panel.Players = MapRosterRules.ActivePlayersForTerritories(
            _panel.Players, loaded.State.Territories);
        Log.Info(Log.LogCategory.Tutorial,
            $"[PlayTutorial] roster trimmed to {_panel.Players.Count} owning players: " +
            string.Join(",", _panel.Players.Select(p => p.Id.Index)));

        _panel.ResetToTutorialStart(loaded.Tutorial.Replay.InitialSnapshot);

        Log.Info(Log.LogCategory.Tutorial,
            $"[PlayTutorial] starting '{TutorialName}' ({loaded.Tutorial.Replay.Beats.Count} beats)");
        _preview.Start(loaded.Tutorial);

#if DEBUG
        CheatMenu.Attach(this);
#endif
    }

    private void OpenPauseMenu()
    {
        Log.Info(Log.LogCategory.Tutorial, "[PlayTutorial] pause menu opened");
        _escMenu.Show(Strings.Get(StringKeys.PauseTitle), new List<EscMenu.Option>
        {
            new(Strings.Get(StringKeys.MenuResume), () => { }),
            new(Strings.Get(StringKeys.HudButtonMainMenu), ReturnToMainMenu),
        });
    }

    private void ReturnToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
