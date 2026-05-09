using System.Collections.Generic;
using Godot;

/// <summary>
/// Top-level mode for the TutorialBuilder scene. The 3-mode topbar
/// switches between these; the scene's <see cref="TutorialBuilderScene"/>
/// owns the state machine.
/// </summary>
public enum TutorialMode { MapEdit, Build, Preview }

/// <summary>
/// TutorialBuilder scene root. Mirrors <see cref="MapEditorScene"/>'s
/// shape — instantiates the reusable <see cref="MapEditorPanel"/> from
/// Phase 1 plus its palette HUD — and adds a top-level mode switcher
/// (<see cref="TutorialBuilderTopBar"/>) and per-mode chrome
/// (<see cref="BuildPane"/> placeholder, <see cref="PreviewPane"/>
/// placeholder). The panel is created once and never torn down; mode
/// switching toggles <see cref="MapEditorPanel.PaintingEnabled"/> and
/// the per-mode chrome's Visible flag, so the painted draft survives
/// every mode transition.
///
/// Phase 2 ships only the scaffolding. Phase 3 wires Save Tutorial /
/// Load Tutorial and grows BuildPane / PreviewPane into real chrome.
/// </summary>
public partial class TutorialBuilderScene : Node2D
{
    private MapEditorPanel _panel = null!;
    private MapEditorHudView _mapEditHud = null!;
    private TutorialBuilderTopBar _topBar = null!;
    private BuildPane _buildPane = null!;
    private PreviewPane _previewPane = null!;
    private List<Player> _players = null!;

    private TutorialMode _currentMode = TutorialMode.MapEdit;

    public override void _Ready()
    {
        _players = BuildPlayers();

        // 1. The reusable Map Editor panel (Phase 1). Owns the map +
        //    draft state + paint stroke machine + undo. Painting starts
        //    enabled because Phase 2 lands in Map Edit mode.
        _panel = new MapEditorPanel { Players = _players };
        AddChild(_panel);

        // 2. The Map Edit palette HUD. TopOffsetPx = 60 puts it below
        //    the topbar; ShowSceneRootChrome = false hides Save Map /
        //    Load Map / Exit (those are TutorialBuilder-level concerns
        //    and live on the topbar instead). The scene wires its
        //    palette/seed/undo events to panel methods exactly the way
        //    MapEditorScene does.
        _mapEditHud = new MapEditorHudView
        {
            TopOffsetPx = (int)HudView.HudHeight,
            ShowSceneRootChrome = false,
        };
        AddChild(_mapEditHud);
        _mapEditHud.GenerateRequested += _panel.GenerateMap;
        _mapEditHud.PaletteSelectionChanged += _panel.SetSelectedPalette;
        _mapEditHud.UndoLastClicked += _panel.UndoLast;
        _mapEditHud.UndoAllClicked += _panel.UndoAll;
        _mapEditHud.RedoLastClicked += _panel.RedoLast;
        _mapEditHud.RedoAllClicked += _panel.RedoAll;
        _panel.UndoStateChanged += () =>
            _mapEditHud.SetUndoState(_panel.CanUndo, _panel.CanRedo);
        _mapEditHud.SetUndoState(canUndo: false, canRedo: false);

        // 3. The 3-mode topbar. Owns its own visual current-mode
        //    indication; Save / Load Tutorial start disabled and stay
        //    disabled until Phase 3 wires them up.
        _topBar = new TutorialBuilderTopBar();
        AddChild(_topBar);
        _topBar.ModeRequested += SetMode;
        _topBar.ExitPressed += ReturnToMainMenu;
        // SaveTutorialPressed / LoadTutorialPressed are wired in Phase 3.
        // Their buttons are Disabled = true in Phase 2 so they can't fire
        // — the events will simply have no subscriber.

        // 4. Per-mode placeholder chrome. Both start Visible = false;
        //    SetMode flips visibility on transitions.
        _buildPane = new BuildPane { Visible = false };
        _buildPane.SetPanel(_panel);
        AddChild(_buildPane);

        _previewPane = new PreviewPane { Visible = false };
        _previewPane.SetPanel(_panel);
        AddChild(_previewPane);

        // 5. Topbar starts on MapEdit; field _currentMode already matches.
        //    Sync the topbar's visual indicator without going through
        //    SetMode (which short-circuits on no-op transitions).
        _topBar.SetCurrentMode(_currentMode);
        _mapEditHud.Visible = true;
        _panel.PaintingEnabled = true;
    }

    /// <summary>
    /// Mode-switch state machine. Toggles painting on the panel,
    /// shows/hides each mode's chrome, and updates the topbar's visual
    /// indicator. Idempotent — calling SetMode(currentMode) is a no-op
    /// (returns early).
    /// </summary>
    private void SetMode(TutorialMode mode)
    {
        if (mode == _currentMode) return;
        TutorialMode previous = _currentMode;
        _currentMode = mode;

        // Painting is only enabled in Map Edit. The panel's
        // PaintingEnabled flag gates every paint event at the source.
        _panel.PaintingEnabled = mode == TutorialMode.MapEdit;

        // Show/hide each mode's chrome. The panel's HexMapView is shared
        // across modes and stays visible; the painted draft persists
        // because we never tear down the panel.
        _mapEditHud.Visible = mode == TutorialMode.MapEdit;
        _buildPane.Visible = mode == TutorialMode.Build;
        _previewPane.Visible = mode == TutorialMode.Preview;

        // Phase 3+ uses Pause() to dispose the transient controller when
        // leaving Preview. Phase 2's Pause is a no-op but the call site
        // is wired now.
        if (previous == TutorialMode.Preview)
        {
            _previewPane.Pause();
        }

        _topBar.SetCurrentMode(mode);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;

        switch (keyEvent.Keycode)
        {
            case Key.Key1:
                SetMode(TutorialMode.MapEdit);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key2:
                SetMode(TutorialMode.Build);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key3:
                SetMode(TutorialMode.Preview);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Escape:
                HandleEscape();
                GetViewport().SetInputAsHandled();
                break;
        }
        // Note: 1 / 2 / 3 reach _UnhandledInput only when no focused
        // Control consumed the keypress first — LineEdit (the seed
        // field in MapEditorHudView) consumes digit keys via its
        // GuiInput, so typing in the seed field doesn't trigger mode
        // switches. That's the desired behavior.
    }

    /// <summary>
    /// ESC ladders out:
    ///   1. If in Build or Preview → drop to Map Edit.
    ///   2. Else if a non-hand palette is selected → drop to hand.
    ///   3. Else exit to the main menu.
    /// </summary>
    private void HandleEscape()
    {
        if (_currentMode != TutorialMode.MapEdit)
        {
            SetMode(TutorialMode.MapEdit);
            return;
        }
        if (_mapEditHud.SelectedPaletteIndex != MapEditorHudView.HandPaletteIndex)
        {
            _mapEditHud.SelectHand();
            return;
        }
        ReturnToMainMenu();
    }

    private static List<Player> BuildPlayers()
    {
        var players = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            players.Add(new Player(name, new Color(hex), AiKind.Human));
        }
        return players;
    }

    private void ReturnToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
