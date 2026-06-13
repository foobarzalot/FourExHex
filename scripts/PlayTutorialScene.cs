using System.Collections.Generic;
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
        // asserts it). Use the all-human roster the builder uses;
        // PreviewPane.Start overrides kinds (player 0 Human, rest Computer).
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
        // TutorialBuilderScene.OnLoadSlotPressed) and start playback. The
        // roster stays the all-human one set above — the builder likewise
        // keeps it across LoadFromMap rather than adopting loaded.Players.
        _panel.LoadFromMap(loaded);
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
        _escMenu.Show("Paused", new List<EscMenu.Option>
        {
            new("Resume", () => { }),
            new("Main Menu", ReturnToMainMenu),
        });
    }

    private void ReturnToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
