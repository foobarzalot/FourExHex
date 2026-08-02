// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
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
    private SaveNameModal? _exportModal;
    private SlotPickerDialog? _loadDialog;
    // Export state stashed between the export prompt and the file dialog
    // (both are modal, so the board can't change in between).
    private GameState? _pendingExportState;
    private string _pendingExportAuthor = "";
    // The map's last loaded/saved slot name — the export prompt's default.
    // Null for a fresh unsaved map (falls back to the seed-based default).
    private string? _currentMapName;

    // The per-color roster the map will bake: kinds (incl. None)
    // and difficulties, chosen up-front for a New Map or derived from the file
    // for a Load Map. The editor's live preview roster (_players) stays all-Human
    // so no AI drives turns; these drive palette gating and the saved file.
    private PlayerKind[] _rosterKinds = null!;
    private Difficulty[] _rosterDifficulties = null!;
    private LoadedSave? _pendingMapToLoad;
    // Game mode baked into a saved map: from the New Map selector
    // for a fresh map, from the file for a loaded one. Threaded into
    // BuildSaveState so an authored Rising Tides map round-trips and plays as
    // Rising Tides. Editing itself is mode-agnostic (no turns advance).
    private GameMode _mapMode = GameMode.Freeform;

    public override void _Ready()
    {
        _saveStore = new SaveStore();
        ResolveEditorRequest();
        // Preview roster = the active (non-None) colors, all forced Human so no
        // AI drives turns and Generate only paints colors that are in play.
        // The baked kinds/difficulties live in _rosterKinds.
        _players = BuildEditorPreviewRoster();

        _panel = new MapEditorPanel { Players = _players };
        AddChild(_panel);
        if (_pendingMapToLoad != null) _panel.LoadFromMap(_pendingMapToLoad);

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

        // Gate the land palette to the active colors and mark the human ones.
        // The HUD's palette is built in its _Ready, run above.
        _hud.ApplyRosterKinds(_rosterKinds);

        BuildSaveDialog();
        BuildExportDialog();
        BuildLoadDialog();

        _escMenu = new EscMenu();
        AddChild(_escMenu);

#if DEBUG
        CheatMenu.Attach(this);
#endif
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        // These modals handle their own ESC-to-close.
        if (_escMenu.IsOpen) return;
        if (_saveModal?.IsOpen == true) return;
        if (_exportModal?.IsOpen == true) return;
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
        _escMenu.Show(Strings.Get(StringKeys.EditorMenuTitle), new[]
        {
            new EscMenu.Option(Strings.Get(StringKeys.MenuResume), () => { }),
            new EscMenu.Option(Strings.Get(StringKeys.EditorSaveMap), OpenSaveDialog),
            new EscMenu.Option(Strings.Get(StringKeys.EditorExportMap), OpenExportDialog),
            new EscMenu.Option(Strings.Get(StringKeys.MenuLoadMap), OpenLoadDialog),
            new EscMenu.Option(Strings.Get(StringKeys.MenuExit), ReturnToMainMenu),
        });
    }

    private void BuildSaveDialog()
    {
        // ModalChrome family name-entry modal (matches in-game Save Game).
        // Its built-in ShowError overlay replaces the old separate error
        // dialog; Confirmed does NOT auto-close, so the host closes on
        // success or shows an inline error on failure.
        _saveModal = new SaveNameModal(Strings.Get(StringKeys.EditorSaveMap));
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
        GameState state = _panel.BuildSaveState(_mapMode);

        // Validate the painted board against the chosen roster: a
        // color that owns land must be active, every active color must own land,
        // and at least two players must be present.
        System.Collections.Generic.IReadOnlyList<string> problems =
            MapRosterRules.ValidateForSave(state.Territories, _rosterKinds);
        if (problems.Count > 0)
        {
            Log.Info(Log.LogCategory.Display, "MapEditor: save blocked — " + string.Join("; ", problems));
            _saveModal.ShowError(string.Join("\n", problems));
            return;
        }

        try
        {
            // Bake the chosen kinds + difficulties into the file by serializing a
            // 6-slot roster carrying them (vs. the all-Human preview roster).
            _saveStore.WriteMapSlot(name, state, _panel.CurrentSeed, BuildBakeRoster());
        }
        catch (System.Exception ex)
        {
            _saveModal.ShowError(Strings.Get(StringKeys.SaveCouldNotSave,
                ("error", ex.Message)));
            return;
        }
        Log.Info(Log.LogCategory.Display,
            $"MapEditor: saved map \"{name}\" kinds=[{string.Join(",", _rosterKinds)}]");
        _currentMapName = name;
        _saveModal.Close();
    }

    private void BuildExportDialog()
    {
        // Two-field export prompt: map name (primary) + author (secondary).
        // It doubles as the export flow's error surface: it stays open
        // across the file dialog and only closes on success.
        _exportModal = new SaveNameModal(
            Strings.Get(StringKeys.EditorExportMap),
            fieldLabel: Strings.Get(StringKeys.ExportNameLabel),
            confirmLabel: Strings.Get(StringKeys.ButtonOk),
            secondaryFieldLabel: Strings.Get(StringKeys.ExportAuthorLabel));
        _exportModal.Confirmed += OnExportConfirmed;
        _exportModal.Closed += () => { _pendingExportState = null; };
        AddChild(_exportModal);
    }

    private void OpenExportDialog()
    {
        if (_exportModal == null) return;
        int seed = _panel.CurrentSeed;
        string defaultName = _currentMapName
            ?? (seed > 0 ? $"map_seed{seed}" : "map");
        _exportModal.Open(defaultName, UserSettings.AuthorName);
    }

    private void OnExportConfirmed(string rawName)
    {
        if (_exportModal == null) return;
        GameState state = _panel.BuildSaveState(_mapMode);

        // Same playability gate as Save Map — an unplayable map is not
        // worth sharing, and import validation on the other end would
        // reject it anyway.
        System.Collections.Generic.IReadOnlyList<string> problems =
            MapRosterRules.ValidateForSave(state.Territories, _rosterKinds);
        if (problems.Count > 0)
        {
            Log.Info(Log.LogCategory.Share,
                "[share] export blocked — " + string.Join("; ", problems));
            _exportModal.ShowError(string.Join("\n", problems));
            return;
        }

        string name = SaveStore.SanitizeSlotName(rawName);
        string author = _exportModal.SecondaryText.Trim();
        if (author.Length > MapImport.MaxAuthorLength)
        {
            author = author.Substring(0, MapImport.MaxAuthorLength);
        }
        UserSettings.AuthorName = author; // remember for next export
        _pendingExportAuthor = author;
        _pendingExportState = state;

        // Mobile (share plugin present): stage the file and hand it to the
        // OS share sheet — there's no meaningful "save as..." on iOS/Android,
        // so the prompt's name is final. Desktop: native save dialog,
        // pre-filled with the name; the filename picked there wins.
        if (ShareBridge.Available)
        {
            ExportViaShareSheet(name);
            return;
        }
        MapFileDialogs.ShowExport(this, name + MapFileDialogs.Extension,
            OnExportPathChosen);
    }

    private void ExportViaShareSheet(string name)
    {
        if (_exportModal == null || _pendingExportState == null) return;
        string json = SaveSerializer.SerializeMap(
            _pendingExportState, _panel.CurrentSeed, BuildBakeRoster(), name,
            author: _pendingExportAuthor.Length > 0 ? _pendingExportAuthor : null);
        string absolutePath;
        try
        {
            absolutePath = _saveStore.WriteExportTemp(
                name + MapFileDialogs.Extension, json);
        }
        catch (System.Exception ex)
        {
            Log.Warn(Log.LogCategory.Share,
                $"[share] export staging failed: {ex.Message}");
            _exportModal.ShowError(Strings.Get(StringKeys.ExportFailed,
                ("error", ex.Message)));
            return;
        }
        Log.Info(Log.LogCategory.Share,
            $"[share] exported '{name}' by '{_pendingExportAuthor}' -> share sheet " +
            $"({json.Length} chars)");
        _exportModal.Close();
        ShareBridge.ShareFile(absolutePath, "application/octet-stream", name);
    }

    private void OnExportPathChosen(string path)
    {
        if (_exportModal == null || _pendingExportState == null) return;
        string name = SaveStore.SanitizeSlotName(
            System.IO.Path.GetFileNameWithoutExtension(path));
        string json = SaveSerializer.SerializeMap(
            _pendingExportState, _panel.CurrentSeed, BuildBakeRoster(), name,
            author: _pendingExportAuthor.Length > 0 ? _pendingExportAuthor : null);
        using FileAccess f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (f == null)
        {
            Log.Warn(Log.LogCategory.Share,
                $"[share] export write failed: {FileAccess.GetOpenError()} at '{path}'");
            _exportModal.ShowError(Strings.Get(StringKeys.ExportFailed,
                ("error", FileAccess.GetOpenError().ToString())));
            return;
        }
        f.StoreString(json);
        Log.Info(Log.LogCategory.Share,
            $"[share] exported '{name}' by '{_pendingExportAuthor}' -> {path} " +
            $"({json.Length} chars)");
        _exportModal.Close();
    }

    private void BuildLoadDialog()
    {
        _loadDialog = new SlotPickerDialog(Strings.Get(StringKeys.MenuLoadMap), Strings.Get(StringKeys.MenuLoadFailed));
        _loadDialog.Attach(this);
    }

    private void OpenLoadDialog()
    {
        if (_loadDialog == null) return;
        // Match the menu's Load Map picker: thumbnail preview from
        // the maps directory and a short-name label.
        _loadDialog.ShowSlots(
            _saveStore.ListMaps(),
            Strings.Get(StringKeys.EditorNoMapsFound),
            info => info.SlotName + SlotPickerDialog.AuthorSuffix(info),
            OnLoadSlotPressed,
            thumbnailStore: _saveStore,
            previewMaps: true);
    }

    private void OnLoadSlotPressed(string slotName)
    {
        try
        {
            LoadedSave loaded = _saveStore.LoadMap(slotName);
            // Adopt the loaded map's baked roster so the palette
            // gates to its colors, Generate paints only them, and a re-save
            // preserves them.
            DeriveRosterFromLoad(loaded, out _rosterKinds, out _rosterDifficulties);
            _mapMode = loaded.State.Mode; // preserve mode on re-save
            _currentMapName = SaveStore.SanitizeSlotName(slotName);
            _players = BuildEditorPreviewRoster();
            _panel.Players = _players;
            _panel.LoadFromMap(loaded);
            _hud.ApplyRosterKinds(_rosterKinds);
            _loadDialog?.Hide();
        }
        catch (System.Exception ex)
        {
            _loadDialog?.ShowError(Strings.Get(StringKeys.MenuCouldNotLoad,
                ("name", slotName), ("error", ex.Message)));
        }
    }

    /// <summary>Resolve the menu's <see cref="MapEditorRequest"/> into the
    /// editor's baked roster. New Map uses the chosen kinds; Load
    /// Map defers the load to <c>_Ready</c> (via <see cref="_pendingMapToLoad"/>)
    /// and derives the roster from the file; a direct launch (diagnostics / no
    /// request) defaults to the all-Human 6-player roster.</summary>
    private void ResolveEditorRequest()
    {
        MapEditorRequest.Request? req = MapEditorRequest.Pending;
        MapEditorRequest.Pending = null;

        if (req is { Source: MapEditorRequest.Source.LoadMap, MapName: { } mapName })
        {
            // A corrupt file here (hand-dropped or imported into user://maps/)
            // must not crash the scene during _Ready — fall through to the
            // default all-Human new-map roster instead.
            try
            {
                _pendingMapToLoad = _saveStore.LoadMap(mapName);
                DeriveRosterFromLoad(_pendingMapToLoad, out _rosterKinds, out _rosterDifficulties);
                _mapMode = _pendingMapToLoad.State.Mode; // preserve mode on re-save
                _currentMapName = SaveStore.SanitizeSlotName(mapName);
                Log.Info(Log.LogCategory.Display,
                    $"MapEditor: load map \"{mapName}\" for editing (mode={_mapMode})");
                return;
            }
            catch (System.Exception ex)
            {
                Log.Warn(Log.LogCategory.Share,
                    $"[share] editor could not load map '{mapName}': {ex.Message}; " +
                    "falling back to a new map");
                _pendingMapToLoad = null;
            }
        }

        if (req is { Source: MapEditorRequest.Source.NewMap, Kinds: { } kinds })
        {
            _rosterKinds = kinds;
            _rosterDifficulties = req.Difficulties
                ?? Enumerable.Repeat(Difficulty.Soldier, kinds.Length).ToArray();
            _mapMode = GameSettings.Mode; // from the New Map page's Game Mode selector
            Log.Info(Log.LogCategory.Display,
                $"MapEditor: new map kinds=[{string.Join(",", _rosterKinds)}] mode={_mapMode}");
            return;
        }

        // No request (diagnostics / direct scene load): all-Human, all paintable.
        _rosterKinds = System.Linq.Enumerable
            .Repeat(PlayerKind.Human, GameSettings.PlayerConfig.Length).ToArray();
        _rosterDifficulties = System.Linq.Enumerable
            .Repeat(Difficulty.Soldier, GameSettings.PlayerConfig.Length).ToArray();
    }

    /// <summary>Derive a 6-slot kinds/difficulties pair from a loaded map
    /// (shared with import validation — see
    /// <see cref="MapRosterRules.DeriveKindsFromLoad"/>).</summary>
    private static void DeriveRosterFromLoad(
        LoadedSave loaded, out PlayerKind[] kinds, out Difficulty[] difficulties)
        => MapRosterRules.DeriveKindsFromLoad(loaded, out kinds, out difficulties);

    /// <summary>The editor's live preview roster: the active (non-None) colors,
    /// all Human so no AI runs and Generate paints only colors in play.</summary>
    private List<Player> BuildEditorPreviewRoster() =>
        MapRosterRules.PreviewRosterFromKinds(_rosterKinds);

    /// <summary>The 6-slot roster (carrying the chosen kinds + difficulties,
    /// including None) serialized into the saved map so a load restores it.</summary>
    private List<Player> BuildBakeRoster()
    {
        var roster = new System.Collections.Generic.List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            roster.Add(new Player(
                GameSettings.PlayerConfig[i].Name,
                PlayerId.FromIndex(i),
                _rosterKinds[i],
                _rosterDifficulties[i]));
        }
        return roster;
    }

    private void ReturnToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
