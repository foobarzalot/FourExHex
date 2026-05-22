using System.Collections.Generic;
using Godot;

/// <summary>
/// Top-level mode for the TutorialBuilder scene. The 3-mode topbar
/// switches between these; the scene's <see cref="TutorialBuilderScene"/>
/// owns the state machine.
/// </summary>
public enum TutorialMode { MapEdit, Record, Preview }

/// <summary>
/// TutorialBuilder scene root. Mirrors <see cref="MapEditorScene"/>'s
/// shape — instantiates the reusable <see cref="MapEditorPanel"/> plus
/// its palette HUD — and adds per-mode chrome (<see cref="RecordPane"/>,
/// <see cref="PreviewPane"/>). The panel is created once and never torn
/// down; mode switching toggles <see cref="MapEditorPanel.PaintingEnabled"/>
/// and the per-mode chrome's Visible flag, so the painted draft survives
/// every mode transition.
///
/// Mode switching and Save / Load Tutorial / Exit all flow through the
/// shared <see cref="EscMenu"/> (opened on ESC or via the map editor
/// HUD's Exit button). There is no dedicated top strip.
/// </summary>
public partial class TutorialBuilderScene : Node2D
{
    private MapEditorPanel _panel = null!;
    private MapEditorHudView _mapEditHud = null!;
    private EscMenu _escMenu = null!;
    private RecordPane _recordPane = null!;
    private PreviewPane _previewPane = null!;
    private List<Player> _players = null!;

    private SaveStore _saveStore = null!;
    private AcceptDialog? _saveDialog;
    private LineEdit? _saveDialogLineEdit;
    private AcceptDialog? _saveErrorDialog;
    private SlotPickerDialog? _loadDialog;
    private ConfirmationDialog? _discardConfirmDialog;
    private System.Action? _onDiscardConfirmed;

    // Captured when leaving Map Edit (the panel's _grid is shared with
    // the play state — Record / Preview mutate tile occupants in
    // place). Restored when returning to Map Edit so the draft visuals
    // and underlying tile occupants snap back to the painted state.
    private EditorSnapshot? _draftSnapshot;

    private TutorialMode _currentMode = TutorialMode.MapEdit;

    public override void _Ready()
    {
        _players = Player.BuildAllHumanRoster();

        // 1. The reusable Map Editor panel. Owns the map + draft state +
        //    paint stroke machine + undo. Painting starts enabled because
        //    the scene lands in Map Edit mode.
        _panel = new MapEditorPanel { Players = _players };
        AddChild(_panel);

        // 2. The Map Edit palette HUD at the top of the screen
        //    (TopOffsetPx defaults to 0). ShowSceneRootChrome = true
        //    surfaces the right-side Options button, which raises
        //    EscRequested → OpenEscMenu. The menu populated by
        //    OpenEscMenu (mode switches + Save / Load Tutorial + Exit)
        //    is TutorialBuilder-specific; Save Map / Load Map (Map
        //    Editor's menu items) don't apply here. When this HUD is
        //    hidden (Record / Preview submodes), HudView's own Options
        //    button takes over and raises its own EscRequested.
        _mapEditHud = new MapEditorHudView
        {
            ShowSceneRootChrome = true,
        };
        AddChild(_mapEditHud);
        _mapEditHud.EscRequested += OpenEscMenu;
        _mapEditHud.GenerateRequested += _panel.GenerateMap;
        _mapEditHud.PaletteSelectionChanged += _panel.SetSelectedPalette;
        _mapEditHud.UndoLastClicked += _panel.UndoLast;
        _mapEditHud.UndoAllClicked += _panel.UndoAll;
        _mapEditHud.RedoLastClicked += _panel.RedoLast;
        _mapEditHud.RedoAllClicked += _panel.RedoAll;
        _panel.UndoStateChanged += () =>
            _mapEditHud.SetUndoState(_panel.CanUndo, _panel.CanRedo);
        _mapEditHud.SetUndoState(canUndo: false, canRedo: false);

        _saveStore = new SaveStore();
        BuildSaveDialog();
        BuildLoadDialog();
        BuildDiscardConfirmDialog();

        // 3. Per-mode placeholder chrome. Both start Visible = false;
        //    SetMode flips visibility on transitions. Each pane builds
        //    its own HudView when it starts; that HudView's ESC handler
        //    is forwarded here so ESC in Record / Preview opens the
        //    same modal as ESC in Map Edit.
        _recordPane = new RecordPane { Visible = false };
        _recordPane.SetPanel(_panel);
        _recordPane.EscRequested += OpenEscMenu;
        AddChild(_recordPane);

        _previewPane = new PreviewPane { Visible = false };
        _previewPane.SetPanel(_panel);
        _previewPane.EscRequested += OpenEscMenu;
        AddChild(_previewPane);

        // 4. Shared modal — opened on ESC, populated per call with
        //    mode-switch buttons + Save / Load Tutorial + Exit.
        _escMenu = new EscMenu();
        AddChild(_escMenu);

        _mapEditHud.Visible = true;
        _panel.PaintingEnabled = true;
    }

    /// <summary>
    /// Mode-switch entry point. Idempotent on no-op. When the target
    /// is Map Edit and a recording exists, defer the switch behind a
    /// "Discard recording?" confirmation — the recorded beats only
    /// make sense against the painted draft they were recorded over,
    /// and Map Edit is the only mode that can mutate that draft.
    /// </summary>
    private void SetMode(TutorialMode mode)
    {
        if (mode == _currentMode) return;
        if (mode == TutorialMode.MapEdit && _recordPane.HasRecording)
        {
            ShowDiscardConfirm(() => ApplyModeSwitch(mode));
            return;
        }
        ApplyModeSwitch(mode);
    }

    /// <summary>
    /// Mode-switch state machine proper. Toggles painting on the panel,
    /// shows/hides each mode's chrome, and tears down / brings up the
    /// per-mode transient state. Preview → Record preserves the
    /// captured Replay and continues recording from the recorded end
    /// state; every other Record entry starts fresh against the
    /// current draft.
    /// </summary>
    private void ApplyModeSwitch(TutorialMode mode)
    {
        if (mode == _currentMode) return;
        TutorialMode previous = _currentMode;
        _currentMode = mode;

        Log.Debug(Log.LogCategory.Tutorial, $"[TutorialBuilder] SetMode: {previous} → {mode}");

        // Capture the draft on every exit from Map Edit so we can
        // restore tile occupants when we come back. Record / Preview
        // share the panel's _grid, so without this recruits built
        // during a recording stay drawn on tiles after the switch.
        if (previous == TutorialMode.MapEdit)
        {
            _draftSnapshot = _panel.SnapshotDraft();
        }

        _panel.PaintingEnabled = mode == TutorialMode.MapEdit;
        _mapEditHud.Visible = mode == TutorialMode.MapEdit;
        _recordPane.Visible = mode == TutorialMode.Record;
        _previewPane.Visible = mode == TutorialMode.Preview;

        if (previous == TutorialMode.Preview)
        {
            _previewPane.Pause();
        }
        if (previous == TutorialMode.Record)
        {
            _recordPane.StopRecording();
        }
        if (mode == TutorialMode.MapEdit)
        {
            _recordPane.DiscardRecording();
            // RestoreDraft mutates the panel's _grid back to the
            // captured tile occupants (clearing recruits / towers /
            // graves placed during Record / Preview) and calls
            // PushState internally to redraw the map.
            if (_draftSnapshot != null)
            {
                _panel.RestoreDraft(_draftSnapshot);
                _draftSnapshot = null;
            }
        }
        if (mode == TutorialMode.Record)
        {
            // Continue any non-empty pre-populated tutorial — covers
            // Preview → Record (preserve recording) and MapEdit → Record
            // after Load (continue authoring the loaded tutorial).
            // A fresh MapEdit → Record (no prior recording) or a
            // MapEdit → Record after a discard sees CurrentTutorial=null
            // and starts fresh.
            Tutorial? carry = _recordPane.CurrentTutorial;
            if (carry != null && carry.Replay.Beats.Count > 0)
            {
                _recordPane.ContinueRecording(carry);
            }
            else
            {
                _recordPane.StartRecording();
            }
        }
        if (mode == TutorialMode.Preview)
        {
            // Hand the RecordPane's authored Tutorial to PreviewPane.
            // RecordPane owns the in-memory Tutorial for the session;
            // PreviewPane builds its transient controller around it.
            Tutorial? authored = _recordPane.CurrentTutorial;
            Log.Debug(Log.LogCategory.Tutorial, $"[TutorialBuilder] Entering Preview. authored={(authored == null ? "null" : "non-null")}, beats={authored?.Replay.Beats.Count ?? -1}");
            if (authored != null && authored.Replay.Beats.Count > 0)
            {
                _previewPane.Start(authored);
            }
            else
            {
                GD.PushWarning("No recorded tutorial to preview. Switch to Record mode first.");
            }
        }
    }

    private void BuildDiscardConfirmDialog()
    {
        _discardConfirmDialog = new ConfirmationDialog
        {
            Title = "Discard recording?",
            DialogText = "Switching to Map Edit will clear the current tutorial recording. Continue?",
            OkButtonText = "Discard",
            Exclusive = true,
        };
        _discardConfirmDialog.Confirmed += () =>
        {
            System.Action? a = _onDiscardConfirmed;
            _onDiscardConfirmed = null;
            a?.Invoke();
        };
        _discardConfirmDialog.Canceled += () => _onDiscardConfirmed = null;
        AddChild(_discardConfirmDialog);
        AudioBus.AttachClick(_discardConfirmDialog.GetOkButton());
        AudioBus.AttachClick(_discardConfirmDialog.GetCancelButton());
    }

    private void ShowDiscardConfirm(System.Action onConfirmed)
    {
        if (_discardConfirmDialog == null) return;
        _onDiscardConfirmed = onConfirmed;
        _discardConfirmDialog.PopupCentered();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        // The modal handles its own ESC-to-close.
        if (_escMenu.IsOpen) return;
        GetViewport().SetInputAsHandled();
        // In Map Edit submode, ESC first drops a non-hand palette back
        // to Hand (mirrors the standalone Map Editor). The palette HUD
        // is hidden in Record / Preview, so this preempt only matters
        // here. Record / Preview never reach this branch because their
        // inner HudView consumes ESC and forwards EscRequested through
        // the pane (see RecordPane / PreviewPane wiring above).
        if (_currentMode == TutorialMode.MapEdit
            && _mapEditHud.SelectedPaletteIndex != MapEditorHudView.HandPaletteIndex)
        {
            _mapEditHud.SelectHand();
            return;
        }
        OpenEscMenu();
    }

    /// <summary>
    /// Show the shared menu modal. Options vary by submode: every
    /// submode offers Resume / mode switches (current submode disabled)
    /// / Save Tutorial / Load Tutorial / Exit.
    /// </summary>
    private void OpenEscMenu()
    {
        var options = new List<EscMenu.Option>
        {
            new("Resume", () => { }),
            new("Map Edit", () => SetMode(TutorialMode.MapEdit),
                Disabled: _currentMode == TutorialMode.MapEdit),
            new("Record", () => SetMode(TutorialMode.Record),
                Disabled: _currentMode == TutorialMode.Record),
            new("Preview", () => SetMode(TutorialMode.Preview),
                Disabled: _currentMode == TutorialMode.Preview),
            new("Save Tutorial", OpenSaveDialog),
            new("Load Tutorial", OpenLoadDialog),
            new("Exit", ReturnToMainMenu),
        };
        _escMenu.Show("Menu", options);
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
            // Read the authored Tutorial off the RecordPane (which owns
            // the in-memory Replay for the session). Bail with a clearer
            // error if recording hasn't run yet — the new schema
            // requires a non-null Replay on every saved Tutorial.
            Tutorial? authored = _recordPane.CurrentTutorial;
            if (authored == null)
            {
                ShowSaveError("Nothing recorded yet. Switch to Record and play a turn first.");
                return;
            }
            _saveStore.WriteTutorial(
                name,
                _panel.BuildSaveState(),
                _panel.CurrentSeed,
                _players,
                authored);
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
        _loadDialog = new SlotPickerDialog(
            "Load Tutorial", "Load failed", disableHorizontalScroll: true);
        _loadDialog.Attach(this);
    }

    private void OpenLoadDialog()
    {
        if (_loadDialog == null) return;
        _loadDialog.ShowSlots(
            _saveStore.ListTutorials(),
            "No tutorials found.",
            info => $"{info.SlotName} — {SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)}",
            OnLoadSlotPressed);
    }

    private void OnLoadSlotPressed(string slotName)
    {
        try
        {
            LoadedSave loaded = _saveStore.LoadTutorial(slotName);
            _panel.LoadFromMap(loaded);
            // Tutorial saves made mid-recording carry the post-replay
            // grid in loaded.State (the panel's _grid is shared with the
            // recording controller and mutates as beats execute). Reset
            // it back to the recording's starting frame so the painted
            // map is what the dev sees on a subsequent MapEdit-Discard.
            if (loaded.Tutorial?.Replay != null)
            {
                _panel.ResetToTutorialStart(loaded.Tutorial.Replay.InitialSnapshot);
            }
            _loadDialog?.Hide();
            // Prime RecordPane with the loaded Tutorial so the
            // subsequent SetMode(Record) carries it forward via
            // ContinueRecording. Empty / null tutorial → ApplyModeSwitch
            // falls through to StartRecording (a fresh recording on
            // the loaded map).
            if (loaded.Tutorial != null && loaded.Tutorial.Replay.Beats.Count > 0)
            {
                _recordPane.PrimeForContinue(loaded.Tutorial);
            }
            SetMode(TutorialMode.Record);
        }
        catch (System.Exception ex)
        {
            _loadDialog?.ShowError($"Could not load '{slotName}': {ex.Message}");
        }
    }
}
