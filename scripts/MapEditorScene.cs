using System.Collections.Generic;
using Godot;

/// <summary>
/// Map editor scene root. Boots into a water-only stub map and lets the
/// user generate a fresh procedural map (HUD seed entry + Generate) or
/// paint individual tiles by clicking them with a palette swatch
/// selected. Owns a mutable <see cref="HexGrid"/> + water set as the
/// editor's draft — every push rebuilds a fresh <see cref="GameState"/>
/// from those, calls <see cref="HexMapView.ReloadState"/> on the live
/// view (preserving zoom/pan), then reapplies occupant visuals.
/// </summary>
public partial class MapEditorScene : Node2D
{
    private MapEditorHudView _hud = null!;
    private HexMapView _map = null!;
    private List<Player> _players = null!;
    private HexGrid _grid = new HexGrid();
    private HashSet<HexCoord> _water = new HashSet<HexCoord>();
    private IReadOnlyList<Territory> _territories = new List<Territory>();
    private readonly UndoStack<EditorSnapshot> _undoStack = new UndoStack<EditorSnapshot>();
    private SaveStore _saveStore = null!;
    private AcceptDialog? _saveDialog;
    private LineEdit? _saveDialogLineEdit;
    private AcceptDialog? _saveErrorDialog;
    private Window? _loadDialog;
    private VBoxContainer? _loadDialogList;
    private AcceptDialog? _loadErrorDialog;
    private int _mapSeed = 0;

    public override void _Ready()
    {
        _players = BuildPlayers();

        _map = new HexMapView();
        InitWaterOnly(_map.Cols, _map.Rows);
        _map.Init(BuildState());
        AddChild(_map);
        _map.CoordClicked += OnCoordClicked;

        _hud = new MapEditorHudView();
        AddChild(_hud);
        _hud.ExitClicked += ReturnToMainMenu;
        _hud.GenerateRequested += OnGenerateRequested;
        _hud.UndoLastClicked += OnUndoLastClicked;
        _hud.UndoAllClicked += OnUndoAllClicked;
        _hud.RedoLastClicked += OnRedoLastClicked;
        _hud.RedoAllClicked += OnRedoAllClicked;
        // SetUndoState is called inside _Ready after the HUD's own _Ready
        // has run (Godot guarantees parent _Ready completes after children
        // are added/Ready'd, and we just AddChild'd it). The buttons start
        // disabled by construction so this call is also defensive against
        // any future change to ordering.
        _hud.SetUndoState(canUndo: false, canRedo: false);
        _hud.SaveMapClicked += OpenSaveDialog;
        _hud.LoadMapClicked += OpenLoadDialog;

        _saveStore = new SaveStore();
        BuildSaveDialog();
        BuildLoadDialog();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        GetViewport()?.SetInputAsHandled();
        ReturnToMainMenu();
    }

    private void OnGenerateRequested(int seed)
    {
        _mapSeed = seed;
        MapGenResult mapGen = MapGenerator.BuildInitialGrid(_map.Cols, _map.Rows, _players, seed);
        _grid = mapGen.Grid;
        _water = new HashSet<HexCoord>(mapGen.WaterCoords);
        // Reset the territory thread on regen — the previous list points
        // at coords from the old grid, so reconcile from scratch.
        _territories = new List<Territory>();
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(_grid);
        _territories = CapitalReconciler.Reconcile(raw, _territories, _grid);
        // Generate is not undoable — drop any prior history so the new map
        // is a fresh starting point.
        _undoStack.Clear();
        // Animate seeded trees in on a fresh map.
        PushState(animateNewOccupants: true);
    }

    private void OnCoordClicked(HexCoord coord)
    {
        int idx = _hud.SelectedPaletteIndex;

        // Capture pre-paint state. If the paint turns out to be a no-op
        // (out of bounds, same color, water-on-water, etc.), MapEditPaint
        // returns the SAME territory list reference and we drop the
        // snapshot rather than pollute the stack with phantom entries.
        EditorSnapshot pre = EditorSnapshot.Capture(_grid, _water, _territories);
        IReadOnlyList<Territory> beforeRef = _territories;

        if (idx == MapEditorHudView.CapitalPaletteIndex)
        {
            _territories = MapEditPaint.PaintCapital(
                _grid, _water, _territories, _map.Cols, _map.Rows, coord);
        }
        else if (idx == MapEditorHudView.TowerPaletteIndex)
        {
            _territories = MapEditPaint.PaintTowerToggle(
                _grid, _water, _territories, _map.Cols, _map.Rows, coord);
        }
        else if (idx == MapEditorHudView.TreePaletteIndex)
        {
            _territories = MapEditPaint.PaintTreeToggle(
                _grid, _water, _territories, _map.Cols, _map.Rows, coord);
        }
        else if (idx == MapEditorHudView.WaterPaletteIndex)
        {
            _territories = MapEditPaint.PaintWater(
                _grid, _water, _territories, _map.Cols, _map.Rows, coord);
        }
        else
        {
            Color color = new Color(GameSettings.PlayerConfig[idx].Hex);
            _territories = MapEditPaint.PaintLand(
                _grid, _water, _territories, _map.Cols, _map.Rows, coord, color);
        }

        if (!ReferenceEquals(_territories, beforeRef))
        {
            _undoStack.PushBefore(pre);
        }

        // Per-paint rebuild: existing trees + graves should appear instantly,
        // not re-grow on every click.
        PushState(animateNewOccupants: false);
    }

    private void OnUndoLastClicked() => RunHistory(_undoStack.CanUndo, _undoStack.UndoLast);
    private void OnUndoAllClicked() => RunHistory(_undoStack.CanUndo, _undoStack.UndoAll);
    private void OnRedoLastClicked() => RunHistory(_undoStack.CanRedo, _undoStack.RedoLast);
    private void OnRedoAllClicked() => RunHistory(_undoStack.CanRedo, _undoStack.RedoAll);

    private void RunHistory(bool gate, System.Func<EditorSnapshot, EditorSnapshot> op)
    {
        if (!gate) return;
        EditorSnapshot current = EditorSnapshot.Capture(_grid, _water, _territories);
        ApplySnapshot(op(current));
    }

    private void ApplySnapshot(EditorSnapshot snap)
    {
        _territories = snap.ApplyTo(_grid, _water);
        // Don't animate trees/graves on undo or redo — restored occupants
        // were already there, they shouldn't reappear with a grow tween.
        PushState(animateNewOccupants: false);
    }

    private void InitWaterOnly(int cols, int rows)
    {
        _grid = new HexGrid();
        _water = new HashSet<HexCoord>();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                _water.Add(HexCoord.FromOffset(col, row));
            }
        }
        _territories = new List<Territory>();
    }

    private GameState BuildState() =>
        new GameState(
            _grid, _territories, _players, new TurnState(_players), new Treasury(), _water);

    /// <summary>
    /// Build a GameState whose TurnState starts at turn 0. That zero
    /// counter is the on-disk marker for "starting map" — the SaveStore
    /// drops it into the maps directory so a Load Map entry point (when
    /// added) can tell it apart from an in-progress game.
    /// </summary>
    private GameState BuildSaveState() =>
        new GameState(
            _grid,
            _territories,
            _players,
            new TurnState(_players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury(),
            _water);

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
        _saveDialogLineEdit.Text = _mapSeed > 0 ? $"map_seed{_mapSeed}" : "map";
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
            _saveStore.WriteMapSlot(name, BuildSaveState(), _mapSeed, _players);
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
            ApplyLoadedMap(loaded);
            _loadDialog?.Hide();
        }
        catch (System.Exception ex)
        {
            ShowLoadError($"Could not load '{slotName}': {ex.Message}");
        }
    }

    private void ApplyLoadedMap(LoadedSave loaded)
    {
        // Hand the loaded grid + water set to the editor's draft. The
        // territory list comes from the save unchanged. Treasury/turn
        // state are discarded — the editor doesn't track them.
        _grid = loaded.State.Grid;
        _water = new HashSet<HexCoord>(loaded.State.WaterCoords);
        _territories = loaded.State.Territories;
        _mapSeed = loaded.MasterSeed;

        // Loading is a fresh starting point, like Generate — drop the
        // undo history so subsequent paints stack cleanly on top.
        _undoStack.Clear();

        // Animate seeded trees in like a fresh generate.
        PushState(animateNewOccupants: true);
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

    private void PushState(bool animateNewOccupants)
    {
        GameState state = BuildState();
        _map.ReloadState(state, animateNewOccupants);
        // ReloadState rebuilds tile fills, water, borders — but trees +
        // capitals come from RefreshOccupantVisuals, which is normally
        // driven by GameController. Pass null currentPlayerColor so no
        // CTA pulsing fires (no "current player" exists in the editor).
        _map.RefreshOccupantVisuals(currentPlayerColor: null, state.Treasury);
        _hud.SetUndoState(_undoStack.CanUndo, _undoStack.CanRedo);
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
