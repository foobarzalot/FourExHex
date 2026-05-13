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
    private AcceptDialog? _saveDialog;
    private LineEdit? _saveDialogLineEdit;
    private AcceptDialog? _saveErrorDialog;
    private Window? _loadDialog;
    private VBoxContainer? _loadDialogList;
    private AcceptDialog? _loadErrorDialog;

    public override void _Ready()
    {
        _players = BuildPlayers();

        _panel = new MapEditorPanel { Players = _players };
        AddChild(_panel);

        _hud = new MapEditorHudView();
        AddChild(_hud);
        _hud.EscRequested += OpenEscMenu;
        _hud.GenerateRequested += _panel.GenerateMap;
        _hud.PaletteSelectionChanged += _panel.SetSelectedPalette;
        _hud.UndoLastClicked += _panel.UndoLast;
        _hud.UndoAllClicked += _panel.UndoAll;
        _hud.RedoLastClicked += _panel.RedoLast;
        _hud.RedoAllClicked += _panel.RedoAll;
        _hud.SaveMapClicked += OpenSaveDialog;
        _hud.LoadMapClicked += OpenLoadDialog;

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
        // Modal already handles its own ESC-to-close.
        if (_escMenu.IsOpen) return;
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
            new EscMenu.Option("Exit", ReturnToMainMenu),
        });
    }

    private void BuildSaveDialog()
    {
        _saveDialog = new AcceptDialog
        {
            Title = "Save Map",
            OkButtonText = "Save",
            Exclusive = false,
        };
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        content.AddThemeConstantOverride("separation", 8);
        content.AddChild(new Label { Text = "Map name:" });
        _saveDialogLineEdit = new LineEdit
        {
            Text = "map",
            CustomMinimumSize = new Vector2(260, 30),
        };
        content.AddChild(_saveDialogLineEdit);
        _saveDialog.AddChild(content);
        _saveDialog.RegisterTextEnter(_saveDialogLineEdit);
        _saveDialog.Confirmed += OnSaveDialogConfirmed;
        AddChild(_saveDialog);
        // GetOkButton() requires the dialog be in the scene tree (the
        // button is auto-built lazily); attach the click sound after
        // AddChild, not before.
        AudioBus.AttachClick(_saveDialog.GetOkButton());

        _saveErrorDialog = new AcceptDialog
        {
            Title = "Save failed",
            OkButtonText = "OK",
        };
        AddChild(_saveErrorDialog);
        AudioBus.AttachClick(_saveErrorDialog.GetOkButton());
    }

    private void OpenSaveDialog()
    {
        if (_saveDialog == null || _saveDialogLineEdit == null) return;
        int seed = _panel.CurrentSeed;
        _saveDialogLineEdit.Text = seed > 0 ? $"map_seed{seed}" : "map";
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
            _saveStore.WriteMapSlot(name, _panel.BuildSaveState(), _panel.CurrentSeed, _players);
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
        // Mirrors MainMenuScene.BuildLoadDialog — same Window-based modal,
        // just listing maps from MapsDirectory instead of saves from
        // SaveDirectory.
        _loadDialog = new Window
        {
            Title = "Load Map",
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
            OkButtonText = "OK",
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
        IReadOnlyList<SaveSlotInfo> slots = _saveStore.ListMaps();
        if (slots.Count == 0)
        {
            var emptyLabel = new Label { Text = "No maps found." };
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
            LoadedSave loaded = _saveStore.LoadMap(slotName);
            _panel.LoadFromMap(loaded);
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
