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
        _territories = TerritoryFinder.Recompute(_grid, new List<Territory>());
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

    /// <summary>
    /// Overwrite the draft's tile colors + occupants + territories from
    /// a tutorial's <see cref="Replay.InitialSnapshot"/>. Called by the
    /// TutorialBuilder right after <see cref="LoadFromMap"/> so the
    /// panel's <c>_grid</c> reflects the painted starting map regardless
    /// of what state the save happened to capture — saves made
    /// mid-recording carry the post-replay state in <c>loaded.State</c>,
    /// which would otherwise become the discard-fallback when the dev
    /// switches back to Map Edit.
    /// </summary>
    public void ResetToTutorialStart(GameStateSnapshot snapshot)
    {
        var throwaway = new Treasury();
        _territories = snapshot.ApplyTo(_grid, throwaway);
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
    /// Build a live <see cref="GameState"/> whose player roster is
    /// <paramref name="roster"/> rather than the panel's
    /// <see cref="Players"/>. Used by the TutorialBuilder: Record mode
    /// uses an all-Human override roster so the dev plays hot-seat;
    /// Preview mode uses a player-0-Human / players-1-5-Computer
    /// override so the AI step machine kicks in for non-main players.
    /// The grid and tile-color partition are reused unchanged — the
    /// override roster must declare the same colors.
    /// </summary>
    public GameState BuildLiveStateWith(IReadOnlyList<Player> roster) =>
        new GameState(
            _grid, _territories, roster, new TurnState(roster), new Treasury(), _water);

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
            AudioBus.Instance.PlayUnitPlaced();
            Log.Debug(Log.LogCategory.Input, $"MapEditorPanel: placement sound (capital) at {coord}.");
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
            AudioBus.Instance.PlayUnitPlaced();
            Log.Debug(Log.LogCategory.Input, $"MapEditorPanel: placement sound (palette {SelectedPaletteIndex}) at {coord}.");
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
        PlayerId owner = PlayerId.FromIndex(idx - 1);
        _territories = MapEditPaint.PaintLand(
            _grid, _water, _territories, Map.Cols, Map.Rows, coord, owner);
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
        // driven by GameController. Pass null currentPlayer so no
        // CTA pulsing fires (no "current player" exists in the editor).
        Map.RefreshOccupantVisuals(currentPlayer: null, state.Treasury);

        DraftChanged?.Invoke();
        UndoStateChanged?.Invoke();
    }
}
