using System.Collections.Generic;
using Godot;

/// <summary>
/// Map editor scene root. Hosts a <see cref="MapEditorPanel"/> for the
/// editor body and owns the scene-root chrome: <see cref="MapEditorHudView"/>
/// (palette + seed/Generate + undo bar + Save/Load/Exit), Save and Load
/// dialogs, and the SaveStore. Wires HUD events to panel methods —
/// the panel owns no chrome, the scene owns no draft state.
/// </summary>
public partial class MapEditorScene : Node2D
{
    private MapEditorHudView _hud = null!;
    private MapEditorPanel _panel = null!;
    private List<Player> _players = null!;
    private EscMenu _escMenu = null!;

    private SaveStore _saveStore = null!;
    private SaveNameModal? _saveModal;
    private SlotPickerDialog? _loadDialog;

    public override void _Ready()
    {
        _players = Player.BuildAllHumanRoster();

        _panel = new MapEditorPanel { Players = _players };
        AddChild(_panel);

        _hud = new MapEditorHudView();
        // Relay the HUD's reserved map insets to the editor map. Subscribe
        // BEFORE AddChild so the HUD's _Ready-time publish is caught; the
        // panel's Map is already in the tree (panel added above).
        _hud.MapInsetsChanged += (top, bottom) => _panel.Map.SetMapInsets(top, bottom);
        AddChild(_hud);
        _hud.EscRequested += OpenEscMenu;
        _hud.GenerateRequested += _panel.GenerateMap;
        _hud.PaletteSelectionChanged += _panel.SetSelectedPalette;
        _hud.UndoLastClicked += _panel.UndoLast;
        _hud.UndoAllClicked += _panel.UndoAll;
        _hud.RedoLastClicked += _panel.RedoLast;
        _hud.RedoAllClicked += _panel.RedoAll;
        // Sync HUD undo button enable state on every panel state change.
        _panel.UndoStateChanged += () =>
            _hud.SetUndoState(_panel.CanUndo, _panel.CanRedo);
        _hud.SetUndoState(canUndo: false, canRedo: false);

        _saveStore = new SaveStore();
        BuildSaveDialog();
        BuildLoadDialog();

        _escMenu = new EscMenu();
        AddChild(_escMenu);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        // These modals handle their own ESC-to-close.
        if (_escMenu.IsOpen) return;
        if (_saveModal?.IsOpen == true) return;
        GetViewport()?.SetInputAsHandled();
        // Escape ladders out: a non-hand palette drops to hand first
        // (canceling whatever paint mode is selected); ESC with hand
        // already active opens the menu modal.
        if (_hud.SelectedPaletteIndex != MapEditorHudView.HandPaletteIndex)
        {
            _hud.SelectHand();
            return;
        }
        OpenEscMenu();
    }

    private void OpenEscMenu()
    {
        _escMenu.Show("Menu", new[]
        {
            new EscMenu.Option("Resume", () => { }),
            new EscMenu.Option("Save Map", OpenSaveDialog),
            new EscMenu.Option("Load Map", OpenLoadDialog),
            new EscMenu.Option("Exit", ReturnToMainMenu),
        });
    }

    private void BuildSaveDialog()
    {
        // ModalChrome family name-entry modal (matches in-game Save Game).
        // Its built-in ShowError overlay replaces the old separate error
        // dialog; Confirmed does NOT auto-close, so the host closes on
        // success or shows an inline error on failure.
        _saveModal = new SaveNameModal("Save Map");
        _saveModal.Confirmed += OnSaveNameConfirmed;
        AddChild(_saveModal);
    }

    private void OpenSaveDialog()
    {
        if (_saveModal == null) return;
        int seed = _panel.CurrentSeed;
        _saveModal.Open(seed > 0 ? $"map_seed{seed}" : "map");
    }

    private void OnSaveNameConfirmed(string rawName)
    {
        if (_saveModal == null) return;
        string name = SaveStore.SanitizeSlotName(rawName);
        try
        {
            _saveStore.WriteMapSlot(name, _panel.BuildSaveState(), _panel.CurrentSeed, _players);
        }
        catch (System.Exception ex)
        {
            _saveModal.ShowError($"Could not save: {ex.Message}");
            return;
        }
        _saveModal.Close();
    }

    private void BuildLoadDialog()
    {
        _loadDialog = new SlotPickerDialog("Load Map", "Load failed");
        _loadDialog.Attach(this);
    }

    private void OpenLoadDialog()
    {
        if (_loadDialog == null) return;
        _loadDialog.ShowSlots(
            _saveStore.ListMaps(),
            "No maps found.",
            info => $"{info.SlotName} — {SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)}",
            OnLoadSlotPressed);
    }

    private void OnLoadSlotPressed(string slotName)
    {
        try
        {
            LoadedSave loaded = _saveStore.LoadMap(slotName);
            _panel.LoadFromMap(loaded);
            _loadDialog?.Hide();
        }
        catch (System.Exception ex)
        {
            _loadDialog?.ShowError($"Could not load '{slotName}': {ex.Message}");
        }
    }

    private void ReturnToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
