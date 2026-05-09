# TutorialBuilder Phase 1: Refactor `MapEditorScene` → `MapEditorPanel`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pure refactor. Extract the Map Editor's draft state, paint logic, view ownership, undo stack, and hover tooltip into a reusable `MapEditorPanel : Node2D`. `MapEditorScene` becomes a thin host that wires its existing `MapEditorHudView` events to the panel. No behavior change. Sets up the reusable panel that Phase 2's `tutorial_builder.tscn` will host.

**Architecture:** `MapEditorPanel` owns view + draft state + paint logic + undo. `MapEditorScene` owns scene-root chrome (Save/Load dialogs, exit). `MapEditorHudView` is unchanged — `MapEditorScene` continues to wire its events, but now to the panel's public methods instead of inline handlers.

**Tech stack:** Godot 4.6.1 (.NET) + C# (net8.0).

**Spec reference:** [`docs/superpowers/specs/2026-05-09-tutorial-builder-design.md`](../specs/2026-05-09-tutorial-builder-design.md) §"`MapEditorPanel` refactor (Phase 1)".

**Refactor verification policy:** Per `CLAUDE.md` ("No new tests for a pure refactor"), Phase 1 adds **no** new unit tests. Verification = existing `EditorSnapshotTests`, `EditorUndoStackTests`, `MapEditPaintTests`, `MapGeneratorTests`, `SaveLoadEquivalenceTests`, `SaveSerializerTests` stay green + a final manual test of every editor workflow.

---

## File structure

| File | Status | Responsibility |
|---|---|---|
| `scripts/MapEditorPanel.cs` | **Create** | Owns HexMapView + draft state (grid, water, territories) + paint stroke state machine + undo stack + hover tooltip + paint logic. Exposes the public API the spec defines for cross-scene reuse. |
| `scripts/MapEditorPanel.cs.uid` | **Create (Godot generates)** | Stable resource ID. Generated automatically the first time Godot imports the script; commit it. |
| `scripts/MapEditorScene.cs` | **Modify** (full rewrite) | Slim scene root. Hosts `MapEditorPanel` + `MapEditorHudView`; owns Save/Load dialogs + `SaveStore` + Exit. Wires HUD events ↔ panel methods. |
| `tests/FourExHex.Tests.csproj` | **Modify** | Add `<Compile Include="..\scripts\MapEditorPanel.cs" />`? **No** — `MapEditorPanel` derives from `Node2D` and depends on `SceneTree`, so it stays out of the test assembly (same exclusion as `MapEditorScene.cs`, `HexMapView.cs`, etc., per `CLAUDE.md` §"Project Structure"). Skip this file change. |
| `scenes/map_editor.tscn` | **No change** | The scene file references `MapEditorScene.cs` by `.uid` — the script's class name and node type stay the same, so no `.tscn` edit needed. |

**Files NOT touched in Phase 1 (deferred to later phases):**
- `MapEditorHudView.cs` — unchanged. Its `Save Map` / `Load Map` / `Exit` buttons are still appropriate for the standalone Map Editor scene. The fact that `tutorial_builder.tscn` will need different chrome (Save Tutorial / Load Tutorial) is a Phase 2 concern; that scene will instantiate its own HUD, not reuse `MapEditorHudView`.
- All `Tutorial*` files — Phase 3+.

---

## Public API the panel must expose

These are the contracts later phases depend on. Phase 1 ships all of them, even where Phase 1 itself doesn't exercise them (e.g., `PaintingEnabled = false`, `SnapshotDraft`/`RestoreDraft`, `BuildLiveState`):

```csharp
public sealed partial class MapEditorPanel : Node2D
{
    // Construction config — set by host before AddChild.
    public IReadOnlyList<Player> Players { get; set; } = null!;

    // Exposed for the host's hover-tooltip / camera-control needs.
    public HexMapView Map { get; private set; } = null!;

    // Undo state, broadcast on UndoStateChanged.
    public bool CanUndo { get; private set; }
    public bool CanRedo { get; private set; }
    public event Action? DraftChanged;          // fired after every state mutation
    public event Action? UndoStateChanged;      // fired when CanUndo/CanRedo flip

    // Palette selection — host sets this from its own HUD's PaletteSelectionChanged.
    public int SelectedPaletteIndex { get; private set; }
    public void SetSelectedPalette(int index);

    // Generate / load.
    public void GenerateMap(int seed);
    public void LoadFromMap(LoadedSave loaded);

    // Snapshot/restore the draft (used by Phase 7+ for Preview cloning).
    public EditorSnapshot SnapshotDraft();
    public void RestoreDraft(EditorSnapshot snap);

    // Undo/redo.
    public void UndoLast();
    public void UndoAll();
    public void RedoLast();
    public void RedoAll();

    // Disable paint (Phase 2+ uses this when in Build/Preview mode). When false,
    // the panel ignores incoming paint events and CoordClicked dispatches.
    public bool PaintingEnabled { get; set; } = true;

    // State-builders. Used by SaveStore.WriteMapSlot (BuildSaveState) and by
    // Preview cloning in Phase 7+ (BuildLiveState).
    public GameState BuildLiveState();
    public GameState BuildSaveState();

    // Convenience: returns the current map seed (for "map_seed{n}" filename
    // suggestion in the host's save dialog). 0 if no seeded generate has run.
    public int CurrentSeed { get; }
}
```

---

## Tasks

Five tasks total. Each ends with build + (where applicable) test + commit. The bulk of the work is in Task 2 (one large file write) — the surrounding tasks are setup, wiring, and verification.

---

### Task 1: Read existing source so the refactor is precise

**Files:**
- Read: `scripts/MapEditorScene.cs` (current implementation)
- Read: `scripts/MapEditorHudView.cs` (event surface — what `MapEditorScene` currently subscribes to)
- Read: `scripts/EditorSnapshot.cs` (snapshot mechanics — already implements Capture / ApplyTo)
- Read: `scripts/HexMapView.cs:1-100` (Init / ReloadState / event surface)
- Read: `scripts/MapEditPaint.cs` (paint helpers — these stay pure, called from the panel)

- [ ] **Step 1: Read all five files**

```bash
# Use the Read tool, not bash cat, per CLAUDE.md tool guidance.
```

Confirm you understand:
- Which fields in `MapEditorScene` are draft-state vs scene-chrome (Save/Load dialogs, `_saveStore`, `_mapSeed`)
- Which methods are panel-internal (paint handlers, undo handlers, generate, ApplyPaintAt, etc.) vs scene-chrome (BuildSaveDialog, OpenSaveDialog, OnSaveDialogConfirmed, BuildLoadDialog, OpenLoadDialog, OnLoadSlotPressed, ApplyLoadedMap, ShowLoadError, FormatTimestamp, ReturnToMainMenu)
- That `EditorSnapshot.Capture(grid, water, territories)` and `snapshot.ApplyTo(grid, water)` already exist — `SnapshotDraft` / `RestoreDraft` are thin wrappers
- That `_mapSeed` stays in `MapEditorScene` (used for the save-dialog filename suggestion) but is **read** from the panel via `panel.CurrentSeed`

---

### Task 2: Create `MapEditorPanel.cs`

**Files:**
- Create: `scripts/MapEditorPanel.cs`

- [ ] **Step 1: Write the new file**

Create `scripts/MapEditorPanel.cs` with the following content:

```csharp
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Reusable Map Editor body. Owns the draft grid/water/territories, the
/// HexMapView instance, the paint-stroke state machine, the undo stack,
/// and the hover tooltip. Hosting scenes (MapEditorScene today,
/// TutorialBuilderScene from Phase 2) wire their HUD's events to the
/// public methods on this panel and consume DraftChanged /
/// UndoStateChanged for HUD enable/disable sync.
///
/// Does NOT own scene-root chrome (Save/Load dialogs, Exit). That's the
/// host's responsibility — Save Map vs Save Tutorial differ per host.
/// </summary>
public sealed partial class MapEditorPanel : Node2D
{
    public IReadOnlyList<Player> Players { get; set; } = null!;

    public HexMapView Map { get; private set; } = null!;
    private HexHoverTooltip _hoverTooltip = null!;

    private HexGrid _grid = new HexGrid();
    private HashSet<HexCoord> _water = new HashSet<HexCoord>();
    private IReadOnlyList<Territory> _territories = new List<Territory>();
    private readonly UndoStack<EditorSnapshot> _undoStack = new UndoStack<EditorSnapshot>();

    // Paint-stroke state. Captured at the first PaintCellEntered of a
    // stroke and consumed at PaintStrokeEnded so a whole drag becomes
    // exactly one undo entry (or zero, if nothing actually changed).
    // _toggleStrokeMode is non-null only for the tree/tower palettes:
    // it locks the stroke to "Add" or "Erase" after the first cell so
    // a single drag never both places and clears.
    private EditorSnapshot? _paintStrokePre;
    private bool _paintStrokeChanged;
    private ToggleStrokeMode? _toggleStrokeMode;

    private enum ToggleStrokeMode { Add, Erase }

    private int _mapSeed;

    public bool CanUndo => _undoStack.CanUndo;
    public bool CanRedo => _undoStack.CanRedo;
    public int SelectedPaletteIndex { get; private set; } = MapEditorHudView.HandPaletteIndex;
    public bool PaintingEnabled { get; set; } = true;
    public int CurrentSeed => _mapSeed;

    public event Action? DraftChanged;
    public event Action? UndoStateChanged;

    public override void _Ready()
    {
        if (Players == null)
        {
            throw new InvalidOperationException(
                "MapEditorPanel.Players must be set by the host before AddChild.");
        }

        Map = new HexMapView();
        InitWaterOnly(Map.Cols, Map.Rows);
        Map.Init(BuildLiveState());
        AddChild(Map);

        Map.CoordClicked += OnCoordClicked;
        Map.CoordHovered += OnCoordHovered;
        Map.PaintCellEntered += OnPaintCellEntered;
        Map.PaintStrokeEnded += OnPaintStrokeEnded;

        // Match the default palette (hand) to Pan mode.
        Map.DragMode = DragModeFor(SelectedPaletteIndex);

        _hoverTooltip = new HexHoverTooltip();
        AddChild(_hoverTooltip);
    }

    public void SetSelectedPalette(int index)
    {
        SelectedPaletteIndex = index;
        Map.DragMode = DragModeFor(index);
    }

    public void GenerateMap(int seed)
    {
        _mapSeed = seed;
        MapGenResult mapGen = MapGenerator.BuildInitialGrid(Map.Cols, Map.Rows, Players, seed);
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

    public void LoadFromMap(LoadedSave loaded)
    {
        // Hand the loaded grid + water set to the panel's draft. The
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

    public EditorSnapshot SnapshotDraft() =>
        EditorSnapshot.Capture(_grid, _water, _territories);

    public void RestoreDraft(EditorSnapshot snap)
    {
        _territories = snap.ApplyTo(_grid, _water);
        PushState(animateNewOccupants: false);
    }

    public void UndoLast() => RunHistory(_undoStack.CanUndo, _undoStack.UndoLast);
    public void UndoAll()  => RunHistory(_undoStack.CanUndo, _undoStack.UndoAll);
    public void RedoLast() => RunHistory(_undoStack.CanRedo, _undoStack.RedoLast);
    public void RedoAll()  => RunHistory(_undoStack.CanRedo, _undoStack.RedoAll);

    public GameState BuildLiveState() =>
        new GameState(
            _grid, _territories, Players, new TurnState(Players), new Treasury(), _water);

    /// <summary>
    /// Build a GameState whose TurnState starts at turn 0. That zero
    /// counter is the on-disk marker for "starting map" — the SaveStore
    /// drops it into the maps directory so a Load Map entry point can
    /// tell it apart from an in-progress game.
    /// </summary>
    public GameState BuildSaveState() =>
        new GameState(
            _grid,
            _territories,
            Players,
            new TurnState(Players, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury(),
            _water);

    private void OnCoordHovered(HexCoord? coord)
    {
        _hoverTooltip.NotifyHover(coord, Map.Cols);
    }

    private void OnCoordClicked(HexCoord coord)
    {
        if (!PaintingEnabled) return;
        // Only fires under Pan-mode palettes (hand, capital). Color /
        // water / tree / tower clicks come through OnPaintCellEntered.
        int idx = SelectedPaletteIndex;
        if (idx == MapEditorHudView.HandPaletteIndex) return;
        if (idx != MapEditorHudView.CapitalPaletteIndex) return;

        EditorSnapshot pre = EditorSnapshot.Capture(_grid, _water, _territories);
        IReadOnlyList<Territory> beforeRef = _territories;
        _territories = MapEditPaint.PaintCapital(
            _grid, _water, _territories, Map.Cols, Map.Rows, coord);
        if (!ReferenceEquals(_territories, beforeRef))
        {
            _undoStack.PushBefore(pre);
        }
        PushState(animateNewOccupants: false);
    }

    private void OnPaintCellEntered(HexCoord coord)
    {
        if (!PaintingEnabled) return;
        // First cell of a stroke captures the rollback snapshot and (for
        // tree/tower) locks the toggle direction so a single drag never
        // both places and clears. Subsequent cells reuse both.
        if (_paintStrokePre is null)
        {
            _paintStrokePre = EditorSnapshot.Capture(_grid, _water, _territories);
            _paintStrokeChanged = false;
            _toggleStrokeMode = ResolveToggleStrokeMode(SelectedPaletteIndex, coord);
        }

        IReadOnlyList<Territory> beforeRef = _territories;
        ApplyPaintAt(SelectedPaletteIndex, coord);
        if (!ReferenceEquals(_territories, beforeRef))
        {
            _paintStrokeChanged = true;
        }
        PushState(animateNewOccupants: false);
    }

    private void OnPaintStrokeEnded()
    {
        if (!PaintingEnabled)
        {
            _paintStrokePre = null;
            _paintStrokeChanged = false;
            _toggleStrokeMode = null;
            return;
        }
        if (_paintStrokePre is not null && _paintStrokeChanged)
        {
            _undoStack.PushBefore(_paintStrokePre);
            UndoStateChanged?.Invoke();
        }
        _paintStrokePre = null;
        _paintStrokeChanged = false;
        _toggleStrokeMode = null;
    }

    private void ApplyPaintAt(int idx, HexCoord coord)
    {
        if (idx == MapEditorHudView.WaterPaletteIndex)
        {
            _territories = MapEditPaint.PaintWater(
                _grid, _water, _territories, Map.Cols, Map.Rows, coord);
            return;
        }
        if (idx == MapEditorHudView.TreePaletteIndex)
        {
            if (!ToggleCellAllowed(coord, isTree: true)) return;
            _territories = MapEditPaint.PaintTreeToggle(
                _grid, _water, _territories, Map.Cols, Map.Rows, coord);
            return;
        }
        if (idx == MapEditorHudView.TowerPaletteIndex)
        {
            if (!ToggleCellAllowed(coord, isTree: false)) return;
            _territories = MapEditPaint.PaintTowerToggle(
                _grid, _water, _territories, Map.Cols, Map.Rows, coord);
            return;
        }
        // Color swatch: idx 1..PlayerConfig.Length. Index 0 is the hand
        // (Pan mode, never reaches here).
        Color color = new Color(GameSettings.PlayerConfig[idx - 1].Hex);
        _territories = MapEditPaint.PaintLand(
            _grid, _water, _territories, Map.Cols, Map.Rows, coord, color);
    }

    /// <summary>
    /// Decide the locked direction for a tree/tower drag stroke based on
    /// what's at the first cell. Tree/tower of the matching kind already
    /// present → Erase; anything else (empty, water, capital, opposite
    /// occupant) → Add. Returns null for non-toggle palettes.
    /// </summary>
    private ToggleStrokeMode? ResolveToggleStrokeMode(int idx, HexCoord firstCoord)
    {
        if (idx != MapEditorHudView.TreePaletteIndex
            && idx != MapEditorHudView.TowerPaletteIndex)
        {
            return null;
        }
        HexTile? tile = _grid.Get(firstCoord);
        if (tile == null) return ToggleStrokeMode.Add;
        bool isTree = idx == MapEditorHudView.TreePaletteIndex;
        bool present = isTree ? tile.Occupant is Tree : tile.Occupant is Tower;
        return present ? ToggleStrokeMode.Erase : ToggleStrokeMode.Add;
    }

    /// <summary>
    /// Gate a per-cell tree/tower toggle by the locked stroke direction.
    /// Add-mode skips cells that already carry the matching occupant
    /// (so a tree-add stroke doesn't accidentally erase trees it
    /// crosses); Erase-mode skips cells without it (so a tree-erase
    /// stroke doesn't drop trees onto bare ground or swap towers in).
    /// </summary>
    private bool ToggleCellAllowed(HexCoord coord, bool isTree)
    {
        HexTile? tile = _grid.Get(coord);
        bool present = tile != null
            && (isTree ? tile.Occupant is Tree : tile.Occupant is Tower);
        return _toggleStrokeMode switch
        {
            ToggleStrokeMode.Add => !present,
            ToggleStrokeMode.Erase => present,
            _ => true,
        };
    }

    private static HexDragMode DragModeFor(int idx) =>
        (idx == MapEditorHudView.HandPaletteIndex
         || idx == MapEditorHudView.CapitalPaletteIndex)
            ? HexDragMode.Pan
            : HexDragMode.Paint;

    private void RunHistory(bool gate, Func<EditorSnapshot, EditorSnapshot> op)
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

    private void PushState(bool animateNewOccupants)
    {
        GameState state = BuildLiveState();
        Map.ReloadState(state, animateNewOccupants);
        // ReloadState rebuilds tile fills, water, borders — but trees +
        // capitals come from RefreshOccupantVisuals, which is normally
        // driven by GameController. Pass null currentPlayerColor so no
        // CTA pulsing fires (no "current player" exists in the editor).
        Map.RefreshOccupantVisuals(currentPlayerColor: null, state.Treasury);

        DraftChanged?.Invoke();
        UndoStateChanged?.Invoke();
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

```bash
dotnet build FourExHex.csproj
```

Expected: build succeeds (zero errors). Warnings about unused fields/events are acceptable at this point (`MapEditorScene` doesn't subscribe yet — Task 3 fixes that).

- [ ] **Step 3: Run the existing test suite to confirm nothing broke**

```bash
dotnet test
```

Expected: all green. `MapEditorPanel.cs` is not pulled into the test assembly (it's a `Node2D`), so this validates that the production assembly builds and existing tests still pass.

- [ ] **Step 4: Stage but do NOT commit yet**

```bash
git add scripts/MapEditorPanel.cs
```

(The commit happens after Task 3, when `MapEditorScene` is wired up — committing now would leave dead code unreferenced.)

---

### Task 3: Rewrite `MapEditorScene.cs` to host the panel

**Files:**
- Modify: `scripts/MapEditorScene.cs` (full rewrite)

- [ ] **Step 1: Replace the file's contents**

Open `scripts/MapEditorScene.cs` and replace its entire contents with:

```csharp
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
        _hud.ExitClicked += ReturnToMainMenu;
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
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        GetViewport()?.SetInputAsHandled();
        // Escape ladders out: first press drops back to the hand
        // (canceling whatever paint mode is selected); a second press
        // with the hand already active exits the editor.
        if (_hud.SelectedPaletteIndex != MapEditorHudView.HandPaletteIndex)
        {
            _hud.SelectHand();
            return;
        }
        ReturnToMainMenu();
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
```

- [ ] **Step 2: Build**

```bash
dotnet build FourExHex.csproj
```

Expected: build succeeds, zero errors, zero warnings.

- [ ] **Step 3: Run the test suite**

```bash
dotnet test
```

Expected: all green. The test assembly doesn't compile `MapEditorScene.cs` or `MapEditorPanel.cs`, but the rules / snapshot / paint / save tests must all still pass.

---

### Task 4: Manual regression test

This is the load-bearing verification for a refactor. Per `CLAUDE.md`, the test/build cycle catches logic errors but not view-layer regressions.

**Files:** None modified. This task is verification only.

- [ ] **Step 1: Refresh the Godot asset cache** (in case the new `MapEditorPanel.cs` script needs a `.uid` generated)

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import
```

Expected: completes with no errors. A new `scripts/MapEditorPanel.cs.uid` file should appear.

- [ ] **Step 2: Stage the generated `.uid` file**

```bash
git add scripts/MapEditorPanel.cs.uid
```

- [ ] **Step 3: Launch the game and exercise the Map Editor**

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

Walk through this checklist in the live editor (handing off to the user — they'll run the game). Each item must work identically to today:

| # | Action | Expected |
|---|---|---|
| 1 | Launch, click "Map Editor" from main menu | Editor loads, water-only map, hand swatch selected |
| 2 | Type a seed, click Generate | Map regenerates with that seed; trees animate in |
| 3 | Click the red palette swatch, drag-paint over water | Land tiles appear in red as you drag |
| 4 | Click the hand swatch, drag the map | Map pans (no painting) |
| 5 | Click the capital swatch, click a red tile | Capital placed |
| 6 | Click the tree swatch, drag over land | Trees appear |
| 7 | Click the tree swatch again, drag over the same trees | Trees erase (toggle-direction lock) |
| 8 | Click the tower swatch, drag over land | Towers appear |
| 9 | Hover a hex, wait ~500ms | Tooltip shows lex index + (col, row) |
| 10 | Click "Undo Last" | Last paint stroke undone, button greys out if no more history |
| 11 | Press Z | Same as Undo Last |
| 12 | Click "Undo All", then "Redo All" | History fully unwound, then replayed |
| 13 | Press Y | Same as Redo Last |
| 14 | Click "Save Map", enter a name, confirm | Map saved (no error dialog) |
| 15 | Click "Load Map", click a slot | Map loads, undo history reset |
| 16 | Press Escape | Hand swatch reselected (if not already) |
| 17 | Press Escape again | Returns to main menu |

If anything misbehaves, **do not commit** — diagnose the regression first and fix in `MapEditorPanel.cs` or `MapEditorScene.cs`.

- [ ] **Step 4: Confirm the user signs off on the manual test**

Ask the user explicitly: "Manual test passed?" Wait for confirmation. Per `CLAUDE.md` ("Manual-test-after-every-change rule"), the user must confirm before the phase is considered done.

---

### Task 5: Commit

**Files:** None additionally modified.

- [ ] **Step 1: Stage everything**

```bash
git add scripts/MapEditorPanel.cs scripts/MapEditorPanel.cs.uid scripts/MapEditorScene.cs
```

- [ ] **Step 2: Verify the staged diff**

```bash
git diff --cached --stat
```

Expected: 3 files changed. `MapEditorPanel.cs` (new, ~280 lines), `MapEditorPanel.cs.uid` (new, single-line uid), `MapEditorScene.cs` (modified, net reduction — the new file is shorter than the old since the panel-internal logic moved out).

- [ ] **Step 3: Commit**

```bash
git commit -m "$(cat <<'EOF'
Extract MapEditorPanel from MapEditorScene (TutorialBuilder Phase 1)

Pure refactor. Move draft state (grid, water, territories), HexMapView
ownership, paint stroke state machine, undo stack, and hover tooltip
into a new MapEditorPanel : Node2D. MapEditorScene becomes a thin host
that owns Save/Load dialogs and wires its existing MapEditorHudView
events to panel methods. No behavior change.

Sets up the reusable panel that tutorial_builder.tscn will host in
Phase 2. Public API (PaintingEnabled / SnapshotDraft / RestoreDraft /
BuildLiveState) lands here even though Phase 1 doesn't exercise them —
later phases consume them without further panel changes.

See docs/superpowers/specs/2026-05-09-tutorial-builder-design.md
§"MapEditorPanel refactor (Phase 1)".

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify the commit**

```bash
git log -1 --stat
```

Expected: shows the commit with 3 files changed.

- [ ] **Step 5: Mark Phase 1 complete in the master plan**

Edit `docs/superpowers/plans/2026-05-09-tutorial-builder-master.md`. In the Phase 1 entry, change `**Status:** 📝 Plan written` to `**Status:** ✅ Complete`. Commit:

```bash
git add docs/superpowers/plans/2026-05-09-tutorial-builder-master.md
git commit -m "Mark TutorialBuilder Phase 1 complete

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Definition of done

Phase 1 is complete when **all of these are true**:

1. `git status` is clean.
2. `dotnet build FourExHex.csproj` succeeds with zero warnings.
3. `dotnet test` passes (every existing test green).
4. The manual regression checklist (Task 4 Step 3) passes in the live game.
5. The user has explicitly confirmed the manual test passed.
6. `MapEditorPanel.cs.uid` is committed.
7. The master plan's Phase 1 status is `✅ Complete`.

## What Phase 1 does NOT do

Per the user's "manually testable phases" + "no parallel execution" constraints:

- **No new feature** — this is a pure refactor. The user-facing experience is identical.
- **No `tutorial_builder.tscn`** — that's Phase 2.
- **No Beat schema, no `TutorialPlayer`, no JSON v3** — those are Phase 3.
- **No `MapEditorHudView` changes** — its `Save Map` / `Load Map` / `Exit` buttons are still appropriate for the Map Editor scene. `tutorial_builder.tscn` will instantiate its own different HUD in Phase 2.
- **No new unit tests** — per `CLAUDE.md` ("No new tests for a pure refactor"). Existing tests + manual regression are the safety net.

## Self-review notes

This plan was self-reviewed against the design spec section "`MapEditorPanel` refactor (Phase 1)". All public API methods called out in the spec are implemented in Task 2's code:

- `event Action? DraftChanged` — fired in `PushState`.
- `event Action<bool, bool>? UndoStateChanged` — actually fired as `event Action?` with the host reading `panel.CanUndo` / `CanRedo`. **Spec deviation noted**: the spec showed `event Action<bool, bool>?` but a no-arg event with the public properties is simpler and the host has to query state on other events anyway. Documenting the deviation here so a future maintainer doesn't get confused.
- `void LoadFromMap(LoadedSave loaded)` — implemented.
- `EditorSnapshot SnapshotDraft()` — implemented as `EditorSnapshot.Capture` wrapper.
- `void RestoreDraft(EditorSnapshot snap)` — implemented.
- `bool PaintingEnabled { get; set; }` — implemented; gates `OnCoordClicked`, `OnPaintCellEntered`, `OnPaintStrokeEnded`.
- `GameState BuildLiveState()` / `GameState BuildSaveState()` — implemented.

No placeholders, no TODO/TBD, all type signatures consistent across tasks.
