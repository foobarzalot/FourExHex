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

    private SaveStore _saveStore = null!;
    private AcceptDialog? _saveDialog;
    private LineEdit? _saveDialogLineEdit;
    private AcceptDialog? _saveErrorDialog;
    private Window? _loadDialog;
    private VBoxContainer? _loadDialogList;
    private AcceptDialog? _loadErrorDialog;

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
        //    indication. Phase 3a enables Save / Load Tutorial and wires
        //    them to the in-scene save/load dialogs (built below).
        _topBar = new TutorialBuilderTopBar();
        AddChild(_topBar);
        _topBar.ModeRequested += SetMode;
        _topBar.ExitPressed += ReturnToMainMenu;
        _topBar.SaveTutorialPressed += OpenSaveDialog;
        _topBar.LoadTutorialPressed += OpenLoadDialog;
        _topBar.SaveEnabled = true;
        _topBar.LoadEnabled = true;

        _saveStore = new SaveStore();
        BuildSaveDialog();
        BuildLoadDialog();

        // 4. Per-mode placeholder chrome. Both start Visible = false;
        //    SetMode flips visibility on transitions.
        _buildPane = new BuildPane { Visible = false };
        _buildPane.SetPanel(_panel);
        AddChild(_buildPane);

        _previewPane = new PreviewPane { Visible = false };
        _previewPane.Configure(_panel);
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

    private void BuildSaveDialog()
    {
        _saveDialog = new AcceptDialog
        {
            Title = "Save Tutorial",
            OkButtonText = "Save",
            DialogHideOnOk = true,
        };
        var content = new VBoxContainer();
        _saveDialogLineEdit = new LineEdit
        {
            CustomMinimumSize = new Vector2(320, 0),
            PlaceholderText = "tutorial name",
        };
        content.AddChild(_saveDialogLineEdit);
        _saveDialog.AddChild(content);
        _saveDialog.RegisterTextEnter(_saveDialogLineEdit);
        _saveDialog.Confirmed += OnSaveDialogConfirmed;
        AddChild(_saveDialog);
        AudioBus.AttachClick(_saveDialog.GetOkButton());

        _saveErrorDialog = new AcceptDialog
        {
            Title = "Save failed",
        };
        AddChild(_saveErrorDialog);
        AudioBus.AttachClick(_saveErrorDialog.GetOkButton());
    }

    private void OpenSaveDialog()
    {
        if (_saveDialog == null || _saveDialogLineEdit == null) return;
        int seed = _panel.CurrentSeed;
        _saveDialogLineEdit.Text = seed > 0 ? $"tutorial_seed{seed}" : "tutorial";
        _saveDialog.PopupCentered();
        _saveDialogLineEdit.GrabFocus();
        _saveDialogLineEdit.SelectAll();
    }

    private void OnSaveDialogConfirmed()
    {
        if (_saveDialogLineEdit == null) return;
        string name = SaveStore.SanitizeSlotName(_saveDialogLineEdit.Text);
        try
        {
            // Phase 3b reads the authored Tutorial off the BuildPane
            // (which owns the in-memory beat list for the session).
            // 3a wrote an empty Tutorial here unconditionally.
            _saveStore.WriteTutorial(
                name,
                _panel.BuildSaveState(),
                _panel.CurrentSeed,
                _players,
                _buildPane.CurrentTutorial);
        }
        catch (System.Exception ex)
        {
            ShowSaveError($"Could not save: {ex.Message}");
        }
    }

    private void ShowSaveError(string message)
    {
        if (_saveErrorDialog == null)
        {
            GD.PushError(message);
            return;
        }
        _saveErrorDialog.DialogText = message;
        _saveErrorDialog.PopupCentered();
    }

    private void BuildLoadDialog()
    {
        // Mirrors MapEditorScene.BuildLoadDialog — same Window-based
        // modal, populated on open from SaveStore.ListTutorials() against
        // TutorialsDirectory.
        _loadDialog = new Window
        {
            Title = "Load Tutorial",
            Size = new Vector2I(440, 360),
            Visible = false,
            Exclusive = true,
        };
        _loadDialog.CloseRequested += () => _loadDialog!.Hide();
        _loadDialog.WindowInput += OnLoadDialogInput;
        AddChild(_loadDialog);

        var scroll = new ScrollContainer
        {
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 16f,
            OffsetTop = 16f,
            OffsetRight = -16f,
            OffsetBottom = -16f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _loadDialog.AddChild(scroll);

        _loadDialogList = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _loadDialogList.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_loadDialogList);

        _loadErrorDialog = new AcceptDialog
        {
            Title = "Load failed",
        };
        AddChild(_loadErrorDialog);
        AudioBus.AttachClick(_loadErrorDialog.GetOkButton());
    }

    private void OnLoadDialogInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        _loadDialog?.Hide();
    }

    private void OpenLoadDialog()
    {
        if (_loadDialog == null || _loadDialogList == null) return;

        foreach (Node child in _loadDialogList.GetChildren())
        {
            child.QueueFree();
        }
        IReadOnlyList<SaveSlotInfo> slots = _saveStore.ListTutorials();
        if (slots.Count == 0)
        {
            var emptyLabel = new Label { Text = "No tutorials found." };
            emptyLabel.AddThemeFontSizeOverride("font_size", 18);
            _loadDialogList.AddChild(emptyLabel);
        }
        foreach (SaveSlotInfo info in slots)
        {
            string capturedName = info.SlotName;
            string label = $"{info.SlotName} — {FormatTimestamp(info.SavedAtUnix)}";
            var btn = new Button
            {
                Text = label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
            };
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.Pressed += () => OnLoadSlotPressed(capturedName);
            AudioBus.AttachClick(btn);
            _loadDialogList.AddChild(btn);
        }
        _loadDialog.PopupCentered();
    }

    private void OnLoadSlotPressed(string slotName)
    {
        try
        {
            LoadedSave loaded = _saveStore.LoadTutorial(slotName);
            _panel.LoadFromMap(loaded);
            // 3b: also restore the authored beats into the BuildPane.
            // A v3 file without a Tutorial block (e.g., a regular
            // user://maps/ map opened by mistake) yields loaded.Tutorial
            // == null; reset BuildPane to an empty Tutorial in that
            // case so a stale beat list doesn't bleed across loads.
            _buildPane.SetTutorial(loaded.Tutorial ?? new Tutorial());
            _loadDialog?.Hide();
        }
        catch (System.Exception ex)
        {
            ShowLoadError($"Could not load '{slotName}': {ex.Message}");
        }
    }

    private void ShowLoadError(string message)
    {
        if (_loadErrorDialog == null)
        {
            GD.PushError(message);
            return;
        }
        _loadErrorDialog.DialogText = message;
        _loadErrorDialog.PopupCentered();
    }

    private static string FormatTimestamp(long unixSeconds)
    {
        var dt = System.DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
        return dt.ToString("yyyy-MM-dd HH:mm");
    }
}
