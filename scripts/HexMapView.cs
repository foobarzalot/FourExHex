using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Pure rendering + input view over a <see cref="GameState"/>. Owns no
/// game data of its own — all grid/territory state lives in the injected
/// GameState. Responsibilities: draw tile fills, territory borders,
/// capitals, units, move-target rings, and selection highlights; emit
/// <see cref="TileClicked"/> for raw left-clicks. Does NOT mutate the
/// model — the controller owns all state changes.
/// </summary>
public partial class HexMapView : Node2D, IHexMapView
{
    /// <summary>
    /// Raised whenever the player left-clicks on the map. The argument is the
    /// tile they clicked, or null if the click was outside the grid. The
    /// view does not apply any "whose turn is it" or "placement mode"
    /// policy — it just reports the raw click; the controller decides.
    /// Suppressed when the same gesture exceeded the long-press threshold —
    /// in that case <see cref="TileLongClicked"/> fires instead.
    /// </summary>
    public event Action<HexTile?>? TileClicked;

    /// <summary>
    /// Raised on release of a left-press that was held at least
    /// <see cref="LongPressMs"/> milliseconds without exceeding the drag
    /// threshold. Mutually exclusive with <see cref="TileClicked"/> for
    /// a given gesture. The controller uses this for the "rally every
    /// movable unit toward this hex" action.
    /// </summary>
    public event Action<HexTile?>? TileLongClicked;

    /// <summary>
    /// Raised on a left-click whose coord is OUTSIDE the land grid
    /// (water, render-only water rim, or past the map). Replaces a
    /// would-be <see cref="TileClicked"/>(null) for that gesture so the
    /// controller can give rejection feedback anchored to the coord
    /// rather than treating it as "click outside" with no information.
    /// </summary>
    public event Action<HexCoord>? OffGridClicked;

    /// <summary>
    /// Raised on the same left-click as <see cref="TileClicked"/>, but with
    /// the raw <see cref="HexCoord"/> under the cursor — including water /
    /// off-grid coords that have no <see cref="HexTile"/>. Editor-only:
    /// the play scene's controller subscribes to TileClicked and ignores
    /// this; the map editor uses it to paint water cells back into land.
    /// </summary>
    public event Action<HexCoord>? CoordClicked;

    /// <summary>
    /// Raised on mouse motion with the hex coord currently under the
    /// cursor, or null when the cursor is off the (Cols × Rows) grid
    /// rectangle or over the HUD strip. Editor-only — the map editor
    /// uses this to drive a hover tooltip (see
    /// <see cref="HexHoverTooltip"/>). The play scene does not
    /// subscribe. Future hover-driven UI (palette previews, territory
    /// info popups) can hang off the same event.
    /// </summary>
    public event Action<HexCoord?>? CoordHovered;

    /// <summary>
    /// Raised once per unique hex visited during a paint gesture in
    /// <see cref="HexDragMode.Paint"/>. Fires on mouse-press with the
    /// initial cell and again whenever the cursor crosses into a new
    /// in-grid cell while the button is held. Editor-only — the play
    /// scene leaves the view in <see cref="HexDragMode.Pan"/> and
    /// never sees these.
    /// </summary>
    public event Action<HexCoord>? PaintCellEntered;

    /// <summary>
    /// Raised on mouse-release at the end of a paint gesture in
    /// <see cref="HexDragMode.Paint"/>. Fires exactly once per
    /// <see cref="PaintCellEntered"/>-bracketed stroke and is the
    /// signal for editor code to commit a single undo entry covering
    /// the whole stroke.
    /// </summary>
    public event Action? PaintStrokeEnded;

    /// <summary>
    /// Drag mode. <see cref="HexDragMode.Pan"/> (default) preserves the
    /// existing left-drag-pans / left-click-fires-CoordClicked behavior
    /// the play scene relies on. <see cref="HexDragMode.Paint"/> turns
    /// the left button into a paint gesture: the press fires
    /// <see cref="PaintCellEntered"/> immediately, motion fires it for
    /// each new cell crossed, release fires <see cref="PaintStrokeEnded"/>;
    /// no panning happens and <see cref="CoordClicked"/> /
    /// <see cref="TileClicked"/> / <see cref="TileLongClicked"/> are
    /// suppressed for that gesture. Set by the map editor based on the
    /// selected palette swatch.
    /// </summary>
    public HexDragMode DragMode { get; set; } = HexDragMode.Pan;

    [Export] public int Cols { get; set; } = 30;
    [Export] public int Rows { get; set; } = 20;
    [Export] public float HexSize { get; set; } = 48f;

    // Unit + capital fill/stroke colors. White = "selected by the
    // player" (move-source unit, or the capital of the selected
    // territory). Black = everything else. Pulse animation, not color,
    // signals "actionable".
    private static readonly Color OccupantSelectedColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color OccupantDefaultColor = new Color(0f, 0f, 0f, 1f);

    // Edge i of a pointy-top hex (vertex i -> vertex (i+1)%6) is shared with
    // the neighbor in this HexCoord.Directions index:
    //   edge 0 (right)       -> E  (dir 0)
    //   edge 1 (lower-right) -> SE (dir 5)
    //   edge 2 (lower-left)  -> SW (dir 4)
    //   edge 3 (left)        -> W  (dir 3)
    //   edge 4 (upper-left)  -> NW (dir 2)
    //   edge 5 (upper-right) -> NE (dir 1)
    private static readonly int[] EdgeToNeighborDirection = { 0, 5, 4, 3, 2, 1 };

    // Injected by the controller before AddChild so _Ready has a populated
    // grid and a fresh territory list to render.
    private GameState _state = null!;

    /// <summary>Pass-through to the game state's grid.</summary>
    public HexGrid Grid => _state.Grid;

    /// <summary>Pass-through to the game state's territory partition.</summary>
    public IReadOnlyList<Territory> Territories => _state.Territories;

    /// <summary>
    /// Wire this view to a <see cref="GameState"/>. Must be called before
    /// adding the HexMapView to the scene tree so <see cref="_Ready"/> can
    /// read the state to build visuals.
    /// </summary>
    public void Init(GameState state)
    {
        _state = state;
    }

    // Layered overlay children (added in this order so draw order is
    // fills -> outlines -> borders -> capitals -> trees -> graves -> units
    // -> deaths -> targets -> highlight). Trees draw under units so the
    // rare unit-on-tree-tile transient (e.g. mid-chop) reads correctly.
    // Deaths draws above units so a freshly-spawned grave grow-in reads
    // underneath the still-shrinking corpse it replaces.
    // Outlines live in their own layer (not as children of each fill) so
    // adjacent same-color tiles don't have one tile's fill overdraw the
    // other tile's outline along the shared edge — that overdraw left
    // the line asymmetric (one half ~0.4 alpha, the other ~0.64) and
    // could read as faint or missing on interior seams.
    private Node2D? _outlinesLayer;
    private Node2D? _towerCoverageLayer;
    private Node2D? _bordersLayer;
    private Node2D? _capitalsLayer;
    private Node2D? _rejectionsLayer;
    private Node2D? _treesLayer;
    private Node2D? _gravesLayer;
    private Node2D? _unitsLayer;
    private Node2D? _deathsLayer;
    private Node2D? _targetsLayer;
    private Node2D? _towerTargetsLayer;
    private Node2D? _highlightLayer;
    private readonly Dictionary<HexCoord, Node2D> _unitVisuals = new();
    private readonly Dictionary<HexCoord, Node2D> _capitalVisuals = new();

    // Tree and grave visuals persist across RefreshOccupantVisuals calls
    // (units, capitals, towers all rebuild every refresh; trees and
    // graves don't depend on per-refresh state, so we keep their nodes
    // alive). This also means a grow-in tween started on a freshly-
    // planted tree or grave is not interrupted by a subsequent refresh.
    private readonly Dictionary<HexCoord, Node2D> _treeVisuals = new();
    private readonly Dictionary<HexCoord, Node2D> _graveVisuals = new();

    // Per-tile fill polygon, owned by the view (was HexTile.Visual on the
    // model). Resynced from _state in RebuildAfterTerritoryChange.
    private readonly Dictionary<HexCoord, Polygon2D> _tileVisuals = new();

    // True except for one refresh after RebuildAfterTerritoryChange,
    // where we want trees and graves to reappear instantly (capture
    // chops removed trees; undo/redo restored a possibly different set
    // of either). Defaulting to true means seeded trees on a fresh game
    // DO animate in on the first refresh, which is the desired feel.
    private bool _animateNewTrees = true;
    private bool _animateNewGraves = true;

    // The territory currently drawn as highlighted. Pure view state — the
    // single source of truth lives in SessionState, but we cache it here
    // so RedrawHighlight doesn't need to take a parameter.
    private Territory? _highlightedTerritory;

    // The coord of the currently "picked up" unit (move source). Drives
    // the white ring color on that one visual; everything else is black.
    // Driven by the controller via ShowMoveSource.
    private HexCoord? _selectedUnit;

    // Every current-player unit that still has its move available this
    // turn. Each one pulses (scales up and back) in _Process so the
    // player can see at a glance which units are actionable. Rebuilt in
    // RefreshOccupantVisuals.
    private readonly HashSet<HexCoord> _pulsingUnits = new();

    // Every current-player capital whose territory can afford to buy
    // anything (a peasant is the cheapest purchase, so peasant
    // affordability subsumes tower affordability). Pulses in sync with
    // the actionable units so the player sees at a glance where they
    // can spend gold.
    private readonly HashSet<HexCoord> _pulsingCapitals = new();
    private double _pulseTime;
    private const float PulseAmplitude = 0.18f;
    private const float PulseRate = 8.0f; // rad/sec; ~1.3 Hz

    // Pan/drag state. The map renders in this Node2D's local space, so
    // panning is just translating Position. ToLocal() in mouse-to-hex
    // already accounts for this transform — no other math changes.
    private const float KeyboardPanStepPx = 75f;
    private const float DragThresholdPx = 5f;
    private bool _dragCandidate;
    private bool _isDragging;
    private Vector2 _dragStartScreen;
    private Vector2 _dragStartMapPosition;

    // Long-press = press held at least this long without dragging. Picks
    // the rally action instead of the normal click on release.
    private const ulong LongPressMs = 400UL;
    private ulong _pressStartMsec;

    // Paint-gesture state, used only when DragMode == Paint. _paintActive
    // is set on press and cleared on release; _lastPaintedCoord de-dups
    // the per-cell event when the cursor moves within the same hex.
    private bool _paintActive;
    private HexCoord _lastPaintedCoord;

    // Zoom state. _zoom is the active scale multiplier on this Node2D,
    // continuous in [_zoomMin, 1.0]. Wheel and +/- keys snap through
    // _zoomLevels (5 discrete stops); trackpad pinch and two-finger
    // scroll move _zoom continuously and re-sync the level index after.
    private const int ZoomLevelCount = 5;
    private float _zoom = 1f;
    private float _zoomMin = 1f;
    private float[] _zoomLevels = new[] { 1f, 1f, 1f, 1f, 1f };
    private int _zoomLevelIndex = ZoomLevelCount - 1;

    // Sensitivity for InputEventPanGesture (two-finger trackpad scroll).
    // Per-event delta is small and dimensionless (~0.03–1.1 in Godot 4.6
    // on macOS); exp() of the negated cumulative delta makes a brisk
    // swipe traverse the full zoom range in roughly one full gesture.
    private const float TrackpadScrollSensitivity = 0.04f;

    /// <summary>Pixel bounding box of the rendered grid, for centering.</summary>
    public Vector2 PixelSize => new Vector2(
        (Cols + 0.5f) * Mathf.Sqrt(3f) * HexSize,
        (1.5f * Rows + 0.5f) * HexSize);

    // The first hex (axial 0,0) is drawn at this offset from the view's
    // origin so the grid's visual bounding box starts at (0,0) local.
    private Vector2 FirstHexCenterOffset => new Vector2(
        0.5f * Mathf.Sqrt(3f) * HexSize,
        HexSize);

    public override void _Ready()
    {
        BuildStateVisuals();

        // Compute zoom levels for the current viewport before the initial
        // pan so ClampPan/VisualCenter use the right effective extent. The
        // resize hook re-runs both whenever the OS window changes size.
        RecomputeZoomLevels();
        GetViewport().SizeChanged += OnViewportResized;

        // Initial pan: geometric center of the map, clamped to bounds.
        // If the map fits in the viewport, ClampPan locks each axis to
        // its centered value (matches the previous one-shot centering
        // that lived in Main.cs).
        Position = ClampPan(VisualCenter() - PixelSize * 0.5f * _zoom);
    }

    /// <summary>
    /// Replace the live <see cref="GameState"/> and rebuild every visual
    /// derived from it (water, foam, tile fills, outlines, layer scaffolds,
    /// borders). Zoom and pan are preserved. Editor-only: gameplay code
    /// drives visual updates through the controller's per-event refresh
    /// paths instead of swapping the entire grid out from under the view.
    ///
    /// <paramref name="animateNewOccupants"/> controls whether the next
    /// <see cref="RefreshOccupantVisuals"/> applies the grow-in animation
    /// to trees and graves. Pass false for incremental edits (paint
    /// strokes) where existing occupants haven't actually changed and
    /// shouldn't re-grow each time the visuals are rebuilt; true (the
    /// default) for full-map loads where the seeded trees should animate
    /// in.
    /// </summary>
    public void ReloadState(GameState state, bool animateNewOccupants = true)
    {
        _state = state;
        BuildStateVisuals();
        if (!animateNewOccupants)
        {
            // Mirrors RebuildAfterTerritoryChange: the suppressed flags
            // get re-armed at the end of the next RefreshOccupantVisuals,
            // so future model-driven tree/grave placements still animate.
            _animateNewTrees = false;
            _animateNewGraves = false;
        }
    }

    /// <summary>
    /// Build (or rebuild) every child node and per-coord cache that derives
    /// from <see cref="_state"/>. Idempotent — on a reload, every prior
    /// child is queued for free first so we don't double-stack visuals.
    /// Does NOT touch zoom/pan — those are camera state, not map state.
    /// </summary>
    private void BuildStateVisuals()
    {
        // Free every prior child (no-op on the initial _Ready call since
        // no children exist yet). Same QueueFree-during-rebuild pattern as
        // RebuildAfterTerritoryChange / ClearLayer.
        foreach (Node child in GetChildren()) child.QueueFree();
        _unitVisuals.Clear();
        _capitalVisuals.Clear();
        _treeVisuals.Clear();
        _graveVisuals.Clear();
        _tileVisuals.Clear();
        _pulsingUnits.Clear();
        _pulsingCapitals.Clear();
        _highlightedTerritory = null;
        _selectedUnit = null;

        // Water cells render first so they sit behind everything else. They
        // are off-map for gameplay (not in _state.Grid) — only the renderer
        // sees them. Each is a flat-colored polygon to match the rest of
        // the game's geometric style.
        foreach (HexCoord waterCoord in _state.WaterCoords)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(waterCoord, HexSize);
            AddChild(CreateWaterHexVisual(center));
        }

        // Render-only extension: a ring of extra water hexes surrounding
        // the playable rectangle. Hides the half-hex rim at default zoom
        // and pushes the visible map edge further off-screen when zoomed.
        // These are not in _state.WaterCoords — they're presentation-only
        // and never reach gameplay rules or save state.
        const int WaterRimMargin = 4;
        for (int row = -WaterRimMargin; row < Rows + WaterRimMargin; row++)
        {
            for (int col = -WaterRimMargin; col < Cols + WaterRimMargin; col++)
            {
                if (row >= 0 && row < Rows && col >= 0 && col < Cols) continue;
                HexCoord coord = HexCoord.FromOffset(col, row);
                Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
                AddChild(CreateWaterHexVisual(center));
            }
        }

        // Per-edge shoreline foam. Each shore edge gets one independent
        // quad — concave shorelines render cleanly because no
        // interpolation crosses between edges.
        var shoreLayer = new Node2D { Name = "ShoreFoamLayer" };
        AddChild(shoreLayer);
        foreach (HexCoord waterCoord in _state.WaterCoords)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(waterCoord, HexSize);
            AddShoreFoamStrips(shoreLayer, center, waterCoord);
        }
        for (int row = -WaterRimMargin; row < Rows + WaterRimMargin; row++)
        {
            for (int col = -WaterRimMargin; col < Cols + WaterRimMargin; col++)
            {
                if (row >= 0 && row < Rows && col >= 0 && col < Cols) continue;
                HexCoord coord = HexCoord.FromOffset(col, row);
                Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
                AddShoreFoamStrips(shoreLayer, center, coord);
            }
        }
        // Bridge the gap between strips on adjacent water hexes around a
        // protruding land corner. At each land vertex shared with two
        // non-land hexes, place a small foam disk centered on the vertex.
        Vector2[] hexVerts = HexVertices();
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            Vector2 landCenter = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            for (int i = 0; i < 6; i++)
            {
                int dirA = EdgeToDirection[(i + 5) % 6];
                int dirB = EdgeToDirection[i];
                if (_state.Grid.Get(tile.Coord.Neighbor(dirA)) != null) continue;
                if (_state.Grid.Get(tile.Coord.Neighbor(dirB)) != null) continue;
                AddCornerFoamDisk(shoreLayer, landCenter + hexVerts[i]);
            }
        }

        // Tiles already exist in _state.Grid (populated by the controller
        // before AddChild). Create one Polygon2D fill per tile, owned by
        // the view in _tileVisuals. Recolors are NOT pushed by a model
        // setter — RebuildAfterTerritoryChange resyncs fills from _state
        // (the coalesced repaint path), so per-action model mutations
        // during an instant fast-forward don't leak to the screen.
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            Polygon2D fill = CreateHexVisual(center, PlayerPalette.ColorFor(tile.Owner));
            _tileVisuals[tile.Coord] = fill;
            AddChild(fill);
        }

        // All per-tile outlines go in one layer drawn after every fill,
        // so neighbor fills can never overdraw an outline. Each unique
        // edge is drawn exactly once for uniform thickness.
        _outlinesLayer = new Node2D { Name = "OutlinesLayer" };
        AddChild(_outlinesLayer);
        PopulateOutlinesLayer();

        GD.Print($"HexMapView: rendering {_state.Grid.Count} tiles across {_state.Territories.Count} territories.");

        // The tower-coverage tint sits above tile fills + outlines but
        // below borders, so the lift is subtle: the underlying territory
        // color shows through and crisp border lines stay on top.
        _towerCoverageLayer = new Node2D { Name = "TowerCoverageLayer" };
        AddChild(_towerCoverageLayer);
        _bordersLayer = new Node2D { Name = "BordersLayer" };
        AddChild(_bordersLayer);
        _capitalsLayer = new Node2D { Name = "CapitalsLayer" };
        AddChild(_capitalsLayer);
        _treesLayer = new Node2D { Name = "TreesLayer" };
        AddChild(_treesLayer);
        _gravesLayer = new Node2D { Name = "GravesLayer" };
        AddChild(_gravesLayer);
        _unitsLayer = new Node2D { Name = "UnitsLayer" };
        AddChild(_unitsLayer);
        _deathsLayer = new Node2D { Name = "DeathsLayer" };
        AddChild(_deathsLayer);
        _targetsLayer = new Node2D { Name = "TargetsLayer" };
        AddChild(_targetsLayer);
        _towerTargetsLayer = new Node2D { Name = "TowerTargetsLayer" };
        AddChild(_towerTargetsLayer);
        _highlightLayer = new Node2D { Name = "HighlightLayer" };
        AddChild(_highlightLayer);
        // Rejection overlays sit on top of everything so a red flash is
        // unambiguous. Persistent — never cleared by RefreshOccupantVisuals
        // — so an in-flight tween doesn't get QueueFree'd mid-pulse.
        _rejectionsLayer = new Node2D { Name = "RejectionsLayer" };
        AddChild(_rejectionsLayer);

        DrawTerritoryBorders();
        // Occupant visuals are drawn by the controller via
        // RefreshOccupantVisuals once it knows the current player and
        // treasury. We don't draw them here because they'd all be non-CTA
        // by default and the controller would immediately overwrite them.
    }

    /// <summary>
    /// Rebuild derived view state after the territory list has changed
    /// (capture, undo, redo). Clears and redraws borders + resets the
    /// move-target overlay. Callers should also refresh the highlight
    /// (via <see cref="ShowHighlight"/>) and the occupant visuals (via
    /// <see cref="RefreshOccupantVisuals"/>) as part of the same pass.
    /// </summary>
    public void RebuildAfterTerritoryChange()
    {
        // Resync every tile fill from the model — the single coalesced
        // repaint path for ownership color (HexTile no longer pushes via
        // a setter). Under an instant fast-forward the per-capture call
        // is _suppressMapRebuild-gated and the driver calls this once
        // per turn / at batch end, so captures no longer recolor
        // tile-by-tile mid-turn. Must run BEFORE the _silentMode
        // early-out below — that guard only concerns tree/grave teardown,
        // and the instant turn-boundary repaint runs with silent still on.
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            if (_tileVisuals.TryGetValue(tile.Coord, out Polygon2D? fill)
                && fill != null)
            {
                fill.Color = PlayerPalette.ColorFor(tile.Owner);
            }
        }

        ClearLayer(_bordersLayer);
        ClearLayer(_targetsLayer);
        DrawTerritoryBorders();

        // Silent batch (AI under Instant): leave tree and grave visuals
        // in place. The controller skips the per-capture RefreshOccupant-
        // Visuals that would otherwise rebuild them, so tearing them
        // down here would make trees vanish for several frames until the
        // end-of-batch refresh recreates them. The final refresh diffs
        // _treeVisuals against the current model state and only frees
        // trees that were actually chopped — correct outcome, no flicker.
        if (_silentMode) return;

        // Tear down all tree and grave visuals and force the next refresh
        // to rebuild them without the grow-in animation. Captures and
        // undo/redo are the only callers, and neither should make existing
        // trees or graves appear to "grow." A capture-chop has already
        // removed its tree from the model; an undo restoring a chopped
        // tree (or a pre-bankruptcy state) should resurrect it instantly.
        foreach (Node2D visual in _treeVisuals.Values)
        {
            visual?.QueueFree();
        }
        _treeVisuals.Clear();
        _animateNewTrees = false;

        foreach (Node2D visual in _graveVisuals.Values)
        {
            visual?.QueueFree();
        }
        _graveVisuals.Clear();
        _animateNewGraves = false;

        // Cancel any in-flight death animations — the corpses they show
        // belonged to a state that no longer applies.
        ClearLayer(_deathsLayer);
    }

    /// <summary>
    /// Draw a bright perimeter around <paramref name="selected"/>, or
    /// clear the highlight if null. The view does not own selection
    /// state — the controller calls this on every change.
    /// </summary>
    public void ShowHighlight(Territory? selected)
    {
        _highlightedTerritory = selected;
        RedrawHighlight();
    }

    /// <summary>
    /// Highlight the given coords as valid move/placement targets with
    /// green concentric rings sized to <paramref name="level"/> — the
    /// preview matches the unit shape (peasant=1 ring, spearman=2,
    /// knight=3, baron=3+dot) so the player sees what the destination
    /// will hold. Pass an empty list to clear.
    /// </summary>
    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level)
    {
        ClearLayer(_targetsLayer);
        if (_targetsLayer == null) return;

        var color = new Color(0.2f, 1f, 0.3f, 0.9f);
        const float ringWidth = 4f;
        int rings = level switch
        {
            UnitLevel.Peasant => 1,
            UnitLevel.Spearman => 2,
            UnitLevel.Knight => 3,
            UnitLevel.Baron => 3,
            _ => 1,
        };

        foreach (HexCoord coord in coords)
        {
            // Wrap rings in a Node2D so the pulse loop in _Process can
            // scale the whole preview together (children are positioned
            // around (0,0); the parent's Position is the tile center).
            var preview = new Node2D
            {
                Position = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize),
            };
            for (int i = 0; i < rings; i++)
            {
                preview.AddChild(CreateCircleOutline(HexSize * UnitRingRadii[i], color, ringWidth));
            }
            if (level == UnitLevel.Baron)
            {
                preview.AddChild(CreateFilledDisc(HexSize * UnitDotRadius, color));
            }
            _targetsLayer.AddChild(preview);
        }
    }

    /// <summary>
    /// Highlight valid tower-placement coords with a tower-shaped preview
    /// in the move-target green. Called by the controller while in
    /// BuildingTower mode. Pass an empty sequence to clear.
    /// </summary>
    public void ShowTowerTargets(IEnumerable<HexCoord> coords)
    {
        ClearLayer(_towerTargetsLayer);
        if (_towerTargetsLayer == null) return;

        foreach (HexCoord coord in coords)
        {
            Node2D preview = CreateTowerPreviewVisual();
            preview.Position = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
            _towerTargetsLayer.AddChild(preview);
        }
    }

    /// <summary>
    /// Subtle white tint over already-tower-defended coords in the
    /// selected territory, shown only while the player is planning a
    /// tower placement. See <see cref="IHexMapView.ShowTowerCoverage"/>.
    /// </summary>
    public void ShowTowerCoverage(IEnumerable<HexCoord> coords)
    {
        ClearLayer(_towerCoverageLayer);
        if (_towerCoverageLayer == null) return;

        Vector2[] verts = HexVertices();
        // Black-alpha overlay darkens the territory color, reading as
        // "in shadow" / "already protected". The controller hands us a
        // deduplicated coord set, so each tile gets exactly one overlay
        // even when two towers cover it — no double-darkening.
        var tint = new Color(0f, 0f, 0f, 0.22f);
        foreach (HexCoord coord in coords)
        {
            var overlay = new Polygon2D
            {
                Position = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize),
                Color = tint,
                Polygon = verts,
            };
            _towerCoverageLayer.AddChild(overlay);
        }
    }

    /// <summary>
    /// Tell the view which unit the player has picked up to move.
    /// Recolors that unit's rings white (and the previous selection's
    /// rings back to black). Pass null to clear the selection.
    /// </summary>
    public void ShowMoveSource(HexCoord? coord)
    {
        if (Equals(_selectedUnit, coord)) return;

        HexCoord? previous = _selectedUnit;
        _selectedUnit = coord;

        if (previous.HasValue) RebuildUnitVisualAt(previous.Value);
        if (coord.HasValue) RebuildUnitVisualAt(coord.Value);
    }

    public override void _Process(double delta)
    {
        int targetCount = _targetsLayer?.GetChildCount() ?? 0;
        int towerTargetCount = _towerTargetsLayer?.GetChildCount() ?? 0;
        if (_pulsingUnits.Count == 0
            && _pulsingCapitals.Count == 0
            && targetCount == 0
            && towerTargetCount == 0) return;

        _pulseTime += delta;
        // sin returns [-1,1]; shift to [0,1] and map to scale
        // [1, 1 + amplitude] so the pulse only grows outward, in
        // sync across every actionable occupant and target ring.
        float phase = (float)System.Math.Sin(_pulseTime * PulseRate) * 0.5f + 0.5f;
        float scale = 1f + PulseAmplitude * phase;
        var pulsedScale = new Vector2(scale, scale);
        foreach (HexCoord coord in _pulsingUnits)
        {
            if (_unitVisuals.TryGetValue(coord, out Node2D? visual) && visual != null)
            {
                visual.Scale = pulsedScale;
            }
        }
        foreach (HexCoord coord in _pulsingCapitals)
        {
            if (_capitalVisuals.TryGetValue(coord, out Node2D? visual) && visual != null)
            {
                visual.Scale = pulsedScale;
            }
        }
        if (_targetsLayer != null)
        {
            foreach (Node child in _targetsLayer.GetChildren())
            {
                if (child is Node2D ring) ring.Scale = pulsedScale;
            }
        }
        if (_towerTargetsLayer != null)
        {
            foreach (Node child in _towerTargetsLayer.GetChildren())
            {
                if (child is Node2D preview) preview.Scale = pulsedScale;
            }
        }
    }

    /// <summary>
    /// Rebuild a single unit's visual in place — used by
    /// <see cref="ShowMoveSource"/> when the selection toggles between
    /// refreshes (the controller sets selection AFTER calling
    /// RefreshOccupantVisuals, so the freshly-built visual doesn't yet
    /// know about it). No-op if the coord has no live unit visual.
    /// </summary>
    private void RebuildUnitVisualAt(HexCoord coord)
    {
        if (!_unitVisuals.TryGetValue(coord, out Node2D? old) || old == null) return;
        HexTile? tile = _state.Grid.Get(coord);
        if (tile?.Unit == null) return;

        Vector2 center = old.Position;
        bool selected = _selectedUnit.HasValue && _selectedUnit.Value == coord;

        old.QueueFree();
        Node2D fresh = CreateUnitVisual(selected, tile.Unit.Level);
        fresh.Position = center;
        _unitsLayer?.AddChild(fresh);
        _unitVisuals[coord] = fresh;
    }

    private static void ClearLayer(Node2D? layer)
    {
        if (layer == null) return;
        foreach (Node child in layer.GetChildren())
        {
            child.QueueFree();
        }
    }

    /// <summary>
    /// Rebuild every occupant visual (units + capitals) using the CTA
    /// coloring rules: the current player's actionable things get a
    /// white interior, everything else gets black. All shapes have a
    /// black border. Pass <paramref name="currentPlayer"/> = null to
    /// render everything non-CTA (e.g., while no turn is active).
    /// </summary>
    public void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury)
    {
        // Detect Unit→Grave transitions BEFORE the units layer is cleared.
        // Each dying unit's visual is reparented to _deathsLayer and
        // tweened out so the grave underneath can grow into view. The
        // coords are passed to the grave-spawn loop below so the matching
        // grave's grow tween can stagger after the corpse's shrink.
        var dyingCoords = new HashSet<HexCoord>();
        foreach (KeyValuePair<HexCoord, Node2D> kvp in _unitVisuals)
        {
            HexTile? t = _state.Grid.Get(kvp.Key);
            if (t?.Occupant is Grave)
            {
                Node2D corpse = kvp.Value;
                _unitsLayer?.RemoveChild(corpse);
                if (_silentMode)
                {
                    // Skip the shrink tween — under Instant the human
                    // shouldn't see corpses fading on their next turn.
                    corpse.QueueFree();
                }
                else
                {
                    _deathsLayer?.AddChild(corpse);
                    StartShrinkAndFreeAnimation(corpse);
                }
                dyingCoords.Add(kvp.Key);
            }
        }
        foreach (HexCoord c in dyingCoords) _unitVisuals.Remove(c);

        ClearLayer(_unitsLayer);
        ClearLayer(_capitalsLayer);
        _unitVisuals.Clear();
        _capitalVisuals.Clear();
        _pulsingUnits.Clear();
        _pulsingCapitals.Clear();

        // Capital that gets the white "selected" color: the one in the
        // currently highlighted territory, if any. Controllers always
        // call ShowHighlight before RefreshOccupantVisuals on selection
        // changes, so reading _highlightedTerritory here is current.
        HexCoord? selectedCapital = _highlightedTerritory?.Capital;

        var actionableCapitals = new HashSet<HexCoord>();
        if (currentPlayer.HasValue)
        {
            foreach (Territory territory in Territories)
            {
                if (territory.Owner != currentPlayer.Value) continue;
                if (!territory.HasCapital) continue;
                // Peasant is the cheapest purchase (10g) and is cheaper
                // than a tower (15g), so peasant-affordability is a
                // sufficient proxy for "this territory can spend gold".
                if (PurchaseRules.CanAffordPeasant(territory, treasury))
                {
                    actionableCapitals.Add(territory.Capital!.Value);
                }
            }
        }

        // Diff trees and graves against the previous refresh so we only
        // animate newly-planted/dug ones. Both are removed lazily here
        // when they disappear from the model (trees: chopped via movement
        // or grown into; graves: stomped by a moving unit, or upgraded
        // into a tree at start-of-turn).
        var currentTreeCoords = new HashSet<HexCoord>();
        var currentGraveCoords = new HashSet<HexCoord>();
        foreach (HexTile tile in Grid.Tiles)
        {
            if (tile.Occupant is Tree) currentTreeCoords.Add(tile.Coord);
            else if (tile.Occupant is Grave) currentGraveCoords.Add(tile.Coord);
        }
        var staleTreeCoords = new List<HexCoord>();
        foreach (HexCoord c in _treeVisuals.Keys)
        {
            if (!currentTreeCoords.Contains(c)) staleTreeCoords.Add(c);
        }
        foreach (HexCoord c in staleTreeCoords)
        {
            _treeVisuals[c]?.QueueFree();
            _treeVisuals.Remove(c);
        }
        // Graves that disappeared this refresh: if the new occupant at
        // that coord is a Tree (start-of-turn grave→tree promotion),
        // animate the grave shrinking away just like a dying unit, and
        // tell the tree branch to delay its grow-in tween. Otherwise
        // (undo, capture-stomp), free instantly.
        var staleGraveCoords = new List<HexCoord>();
        var graveToTreeCoords = new HashSet<HexCoord>();
        foreach (HexCoord c in _graveVisuals.Keys)
        {
            if (!currentGraveCoords.Contains(c)) staleGraveCoords.Add(c);
        }
        foreach (HexCoord c in staleGraveCoords)
        {
            Node2D? graveVisual = _graveVisuals[c];
            HexTile? newTile = _state.Grid.Get(c);
            if (graveVisual != null && _animateNewTrees && newTile?.Occupant is Tree && !_silentMode)
            {
                _gravesLayer?.RemoveChild(graveVisual);
                _deathsLayer?.AddChild(graveVisual);
                StartShrinkAndFreeAnimation(graveVisual);
                graveToTreeCoords.Add(c);
            }
            else
            {
                graveVisual?.QueueFree();
            }
            _graveVisuals.Remove(c);
        }

        foreach (HexTile tile in Grid.Tiles)
        {
            if (tile.Occupant == null) continue;

            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);

            if (tile.Occupant is Unit unit)
            {
                bool selected = _selectedUnit.HasValue && _selectedUnit.Value == tile.Coord;
                Node2D visual = CreateUnitVisual(selected, unit.Level);
                visual.Position = center;
                _unitsLayer?.AddChild(visual);
                _unitVisuals[tile.Coord] = visual;

                bool actionable = currentPlayer.HasValue
                    && unit.Owner == currentPlayer.Value
                    && !unit.HasMovedThisTurn;
                if (actionable) _pulsingUnits.Add(tile.Coord);
            }
            else if (tile.Occupant is Capital)
            {
                bool selected = selectedCapital.HasValue && selectedCapital.Value == tile.Coord;
                Node2D visual = CreateCapitalVisual(selected);
                visual.Position = center;
                _capitalsLayer?.AddChild(visual);
                _capitalVisuals[tile.Coord] = visual;

                if (actionableCapitals.Contains(tile.Coord))
                {
                    _pulsingCapitals.Add(tile.Coord);
                }
            }
            else if (tile.Occupant is Grave)
            {
                if (!_graveVisuals.ContainsKey(tile.Coord))
                {
                    Node2D visual = CreateGraveVisual();
                    visual.Position = center;
                    _gravesLayer?.AddChild(visual);
                    _graveVisuals[tile.Coord] = visual;
                    if (_animateNewGraves && !_silentMode)
                    {
                        StartGraveGrowAnimation(visual, dyingCoords.Contains(tile.Coord));
                    }
                }
                // Existing grave visuals are left in place.
            }
            else if (tile.Occupant is Tree)
            {
                if (!_treeVisuals.ContainsKey(tile.Coord))
                {
                    Node2D anchor = BuildTreeAnchor(center);
                    _treesLayer?.AddChild(anchor);
                    _treeVisuals[tile.Coord] = anchor;
                    if (_animateNewTrees && !_silentMode)
                    {
                        StartTreeGrowAnimation(anchor, graveToTreeCoords.Contains(tile.Coord));
                    }
                }
                // Existing tree visuals are left in place.
            }
            else if (tile.Occupant is Tower)
            {
                Node2D visual = CreateTowerVisual();
                visual.Position = center;
                _capitalsLayer?.AddChild(visual);
            }
        }

        _animateNewTrees = true;
        _animateNewGraves = true;
    }

    /// <summary>
    /// Build a tree visual at <paramref name="center"/> with its pivot
    /// at the trunk bottom so a subsequent scale-up animation reads as
    /// "rising out of the ground." Returns the anchor (parent) Node2D —
    /// the caller is expected to AddChild it before invoking
    /// <see cref="StartTreeGrowAnimation"/>, since Godot tweens require
    /// the target node to already be inside the scene tree.
    /// </summary>
    private Node2D BuildTreeAnchor(Vector2 center)
    {
        // Trunk bottom in CreateTreeVisual's local coords sits at
        // y = +0.225 * HexSize (r * 0.75 where r = 0.3 * HexSize).
        // Place an anchor there and shift the tree up by the same
        // amount so it draws unchanged but its (0,0) pivot is the
        // trunk base.
        float trunkBottomOffset = HexSize * 0.225f;
        var anchor = new Node2D
        {
            Position = center + new Vector2(0f, trunkBottomOffset),
        };
        Node2D tree = CreateTreeVisual();
        tree.Position = new Vector2(0f, -trunkBottomOffset);
        anchor.AddChild(tree);
        return anchor;
    }

    // Shrink/grow timings for the bankruptcy death and the start-of-turn
    // grave→tree promotion. ShrinkDuration is shared by the unit-death
    // and grave-shrink tweens so the two transitions feel kin. The grave
    // grow and tree grow stagger after a preceding shrink by exactly
    // ShrinkDuration so the player reads "old thing dies, new thing
    // appears" rather than a crossfade.
    private const double ShrinkDurationSeconds = 0.25;
    private const double GraveGrowDurationSeconds = 0.35;
    private const double TreeGrowDurationSeconds = 0.7;
    private const double TreeFadeDurationSeconds = 0.5;

    /// <summary>
    /// Kick off the grow-in tween on a tree anchor that is already in
    /// the scene tree. If <paramref name="afterShrink"/> is true, the
    /// tween waits for a preceding grave-shrink to finish before
    /// starting, so the grave→tree promotion reads as sequential.
    /// </summary>
    private static void StartTreeGrowAnimation(Node2D anchor, bool afterShrink = false)
    {
        anchor.Scale = new Vector2(0.05f, 0.05f);
        anchor.Modulate = new Color(1f, 1f, 1f, afterShrink ? 0f : 0.3f);
        Tween tween = anchor.CreateTween();
        if (afterShrink)
        {
            tween.TweenInterval(ShrinkDurationSeconds);
        }
        tween.TweenProperty(anchor, "scale", Vector2.One, TreeGrowDurationSeconds)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(anchor, "modulate", new Color(1f, 1f, 1f, 1f), TreeFadeDurationSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Shrink a visual to nothing and free it. Used for unit corpses on
    /// bankruptcy and for graves being promoted into trees.
    /// </summary>
    private static void StartShrinkAndFreeAnimation(Node2D visual)
    {
        Tween tween = visual.CreateTween();
        tween.TweenProperty(visual, "scale", Vector2.Zero, ShrinkDurationSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(visual, "modulate", new Color(1f, 1f, 1f, 0f), ShrinkDurationSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.Finished += visual.QueueFree;
    }

    // Destruction-effect tuning. Three layered elements: a tile-shaped
    // flash for "something happened here", an expanding shockwave ring
    // that draws the eye outward, and a radial burst of shard polygons
    // colored to identify what was destroyed. Total lifetime ~0.55s —
    // long enough to register, short enough not to back up turn pacing.
    private const double DestructionFlashDuration = 0.35;
    private const double DestructionShockwaveDuration = 0.45;
    private const double DestructionShardDuration = 0.55;
    private const int UnitShardCount = 14;
    private const int TowerShardCount = 20;
    private const int TreeShardCount = 16;
    private static readonly Random DestructionRng = new Random();

    /// <summary>
    /// One-shot capture/chop visual. Spawns a tile-shaped white flash, an
    /// expanding shockwave ring, and a radial burst of shard polygons
    /// colored to match what was destroyed (unit owner color, stone for
    /// towers, green for trees). Graves are silent — burying isn't a
    /// destruction the player needs to see. All transient nodes free
    /// themselves when their tweens finish.
    /// </summary>
    /// <summary>True while an AI player runs under the "Instant" AI
    /// Speed setting — gates per-action sound/anim spawn calls so the
    /// AI batch is fully silent from the human's perspective. Toggled
    /// by GameController; game-end overlays (victory/defeat/bankruptcy)
    /// remain audible because they flow through Refresh, not through
    /// the gated Play* paths.</summary>
    private bool _silentMode;
    public void SetSilentMode(bool silent)
    {
        _silentMode = silent;
    }

    public void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed)
    {
        if (!UserSettings.VfxEnabled) return;
        if (_silentMode) return;
        if (destroyed is Grave) return;
        if (_deathsLayer == null) return;

        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);

        Color shardColor = destroyed switch
        {
            Unit u => PlayerPalette.ColorFor(u.Owner),
            Tower => new Color(0.72f, 0.72f, 0.76f, 1f),
            Tree => new Color(0.16f, 0.48f, 0.18f, 1f),
            _ => new Color(1f, 1f, 1f, 1f),
        };
        int shardCount = destroyed switch
        {
            Tower => TowerShardCount,
            Tree => TreeShardCount,
            _ => UnitShardCount,
        };

        SpawnDestructionFlash(center);
        SpawnDestructionShockwave(center, shardColor);
        for (int i = 0; i < shardCount; i++)
        {
            SpawnDestructionShard(center, shardColor, i, shardCount);
        }
    }

    /// <summary>
    /// Single sound-dispatch entry point. The <paramref name="at"/> coord
    /// is unused today — AudioBus plays through a single non-spatial 2D
    /// player — but the parameter keeps room for a later positional
    /// implementation without touching every caller. Silent-mode gates
    /// every per-action cue; <see cref="SoundEffect.Bankruptcy"/> and
    /// <see cref="SoundEffect.GameWon"/> are exempt (turn-/game-boundary
    /// events the user asked to still hear under Instant).
    /// </summary>
    public void PlaySound(SoundEffect kind, HexCoord? at = null)
    {
        bool exemptFromSilent = kind == SoundEffect.Bankruptcy || kind == SoundEffect.GameWon;
        if (_silentMode && !exemptFromSilent) return;
        switch (kind)
        {
            case SoundEffect.UnitPlaced: AudioBus.Instance.PlayUnitPlaced(); break;
            case SoundEffect.TowerPlaced: AudioBus.Instance.PlayTowerPlaced(); break;
            case SoundEffect.UnitCombined: AudioBus.Instance.PlayUnitCombined(); break;
            case SoundEffect.UnitDestroyed: AudioBus.Instance.PlayUnitDestroyed(); break;
            case SoundEffect.TowerDestroyed: AudioBus.Instance.PlayTowerDestroyed(); break;
            case SoundEffect.TreeCleared: AudioBus.Instance.PlayTreeCleared(); break;
            case SoundEffect.CapitalDestroyed: AudioBus.Instance.PlayCapitalDestroyed(); break;
            case SoundEffect.Bankruptcy: AudioBus.Instance.PlayBankruptcy(); break;
            case SoundEffect.GameWon: AudioBus.Instance.PlayGameWon(); break;
            case SoundEffect.Rally: AudioBus.Instance.PlayRally(); break;
            case SoundEffect.PlayerDefeated: AudioBus.Instance.PlayPlayerDefeated(); break;
        }
    }

    public void FlashRejection(HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders)
    {
        HexCoord[] defenders = blockingDefenders is HexCoord[] arr
            ? arr
            : System.Linq.Enumerable.ToArray(blockingDefenders);

        SpawnRejectionPulse(target, BuildShapeForRejection(shape));
        foreach (HexCoord coord in defenders)
        {
            if (coord == target) continue; // zero-length arrow; the target flash covers it
            SpawnDefenderArrow(coord, target);
        }

        if (defenders.Length > 0)
        {
            AudioBus.Instance.PlayRejectDefended();
        }
        else
        {
            AudioBus.Instance.PlayRejectGeneric();
        }
    }

    /// <summary>
    /// Target overlay: the silhouette of what the player tried to place
    /// (in red), with a black-outlined red "forbidden" circle + slash
    /// drawn over it. The outline guarantees visibility on red tiles
    /// where a pure-red ghost would disappear.
    /// </summary>
    private Node2D BuildShapeForRejection(RejectionShape shape)
    {
        var root = new Node2D();

        // Silhouette underneath — keeps the "what was being placed" cue.
        if (shape == RejectionShape.Tower)
        {
            root.AddChild(BuildRedTowerGhost());
        }
        else
        {
            UnitLevel level = shape switch
            {
                RejectionShape.Peasant => UnitLevel.Peasant,
                RejectionShape.Spearman => UnitLevel.Spearman,
                RejectionShape.Knight => UnitLevel.Knight,
                RejectionShape.Baron => UnitLevel.Baron,
                _ => UnitLevel.Peasant,
            };
            root.AddChild(BuildRedUnitGhost(level));
        }

        root.AddChild(BuildForbiddenSlash());
        return root;
    }

    private Node2D BuildRedUnitGhost(UnitLevel level)
    {
        Color red = new Color(1f, 0.15f, 0.15f, 1f);
        var node = new Node2D();
        int rings = level switch
        {
            UnitLevel.Peasant => 1,
            UnitLevel.Spearman => 2,
            UnitLevel.Knight => 3,
            UnitLevel.Baron => 3,
            _ => 1,
        };
        for (int i = 0; i < rings; i++)
        {
            node.AddChild(CreateCircleOutline(HexSize * UnitRingRadii[i], red, UnitRingWidth));
        }
        if (level == UnitLevel.Baron)
        {
            node.AddChild(CreateFilledDisc(HexSize * UnitDotRadius, red));
        }
        return node;
    }

    private Node2D BuildRedTowerGhost()
    {
        Color red = new Color(1f, 0.15f, 0.15f, 1f);
        Vector2[] verts = TowerShapeVertices();
        var body = new Polygon2D
        {
            Color = red,
            Polygon = verts,
        };
        body.AddChild(BuildClosedOutline(verts, 2f, new Color(0f, 0f, 0f, 1f)));
        return body;
    }

    /// <summary>
    /// International "no" sign: a red circle with a diagonal slash, both
    /// wrapped in black outlines so the symbol stays legible on red
    /// territory tiles where a pure-red glyph would vanish.
    /// </summary>
    private Node2D BuildForbiddenSlash()
    {
        Color red = new Color(0.95f, 0.1f, 0.1f, 1f);
        Color black = new Color(0f, 0f, 0f, 1f);
        const float ringWidth = 5f;
        const float blackPad = 3f; // how much wider the black underline is
        float radius = HexSize * 0.6f;

        var node = new Node2D();

        // Ring: black thicker underline + red on top.
        node.AddChild(CreateCircleOutline(radius, black, ringWidth + blackPad));
        node.AddChild(CreateCircleOutline(radius, red, ringWidth));

        // Diagonal slash from upper-left to lower-right (\ on screen — Y is
        // down in Godot 2D). Reach slightly outside the ring so it visually
        // crosses the boundary.
        float reach = radius * 0.74f; // sqrt(2)/2 ≈ 0.707 lands on the ring
        Vector2 a = new Vector2(-reach, -reach);
        Vector2 b = new Vector2(reach, reach);

        var slashBlack = new Line2D
        {
            Points = new[] { a, b },
            DefaultColor = black,
            Width = ringWidth + blackPad,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
        };
        var slashRed = new Line2D
        {
            Points = new[] { a, b },
            DefaultColor = red,
            Width = ringWidth,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
        };
        node.AddChild(slashBlack);
        node.AddChild(slashRed);

        return node;
    }

    /// <summary>
    /// Black arrow that grows from the defender's tile to the target,
    /// holds briefly, then fades out. Communicates causality — "this
    /// specific defender is what stopped you" — independently of any
    /// color contrast on the underlying tiles.
    /// </summary>
    private void SpawnDefenderArrow(HexCoord defenderCoord, HexCoord targetCoord)
    {
        if (_rejectionsLayer == null) return;

        Vector2 defenderPos = FirstHexCenterOffset + HexPixel.ToPixel(defenderCoord, HexSize);
        Vector2 targetPos = FirstHexCenterOffset + HexPixel.ToPixel(targetCoord, HexSize);
        if (defenderPos == targetPos) return;

        Color black = new Color(0f, 0f, 0f, 1f);
        const float shaftWidth = 6f;
        const float headSize = 13f;
        const double growSeconds = 0.40;
        const double holdSeconds = 0.18;
        const double fadeSeconds = 0.32;

        var line = new Line2D
        {
            Points = new[] { defenderPos, defenderPos },
            DefaultColor = black,
            Width = shaftWidth,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
        };
        _rejectionsLayer.AddChild(line);

        var head = new Polygon2D
        {
            Color = black,
            Polygon = ArrowheadVertices(headSize),
            Position = defenderPos,
            Rotation = (targetPos - defenderPos).Angle(),
            Visible = false,
        };
        _rejectionsLayer.AddChild(head);

        Tween tween = line.CreateTween();
        tween.TweenMethod(
            Callable.From<float>(t =>
            {
                Vector2 currentEnd = defenderPos.Lerp(targetPos, t);
                line.Points = new[] { defenderPos, currentEnd };
                head.Position = currentEnd;
                if (t > 0.05f) head.Visible = true;
            }),
            0f, 1f, growSeconds)
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenInterval(holdSeconds);
        tween.TweenProperty(line, "modulate", new Color(1f, 1f, 1f, 0f), fadeSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(head, "modulate", new Color(1f, 1f, 1f, 0f), fadeSeconds)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Finished += () =>
        {
            line.QueueFree();
            head.QueueFree();
        };
    }

    private static Vector2[] ArrowheadVertices(float size)
    {
        // Tip at origin, body trailing toward -X. Position the head at
        // the line's moving endpoint so the tip lands on that endpoint.
        return new[]
        {
            new Vector2(0f, 0f),
            new Vector2(-size * 1.6f, -size * 0.85f),
            new Vector2(-size * 1.0f, 0f),
            new Vector2(-size * 1.6f, size * 0.85f),
        };
    }

    /// <summary>
    /// Drop <paramref name="ghost"/> at <paramref name="coord"/>'s center
    /// in the persistent <see cref="_rejectionsLayer"/> and run a two-pulse
    /// fade tween. QueueFree on completion so we don't accumulate stale
    /// nodes when the player rapid-clicks invalid hexes.
    /// </summary>
    private void SpawnRejectionPulse(HexCoord coord, Node2D ghost)
    {
        if (_rejectionsLayer == null) return;
        ghost.Position = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
        ghost.Modulate = new Color(1f, 1f, 1f, 0.9f);
        _rejectionsLayer.AddChild(ghost);

        // Two pulses over ~1.3s, with a slow final fade so the ghost
        // lingers long enough to read at a glance:
        //   0.30s   dip to 0.3 alpha
        //   0.20s   rise back to 0.95
        //   0.30s   dip to 0.3 alpha
        //   0.50s   slow fade to fully transparent
        Tween tween = ghost.CreateTween();
        tween.TweenProperty(ghost, "modulate", new Color(1f, 1f, 1f, 0.3f), 0.30)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(ghost, "modulate", new Color(1f, 1f, 1f, 0.95f), 0.20)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(ghost, "modulate", new Color(1f, 1f, 1f, 0.3f), 0.30)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        tween.TweenProperty(ghost, "modulate", new Color(1f, 1f, 1f, 0f), 0.50)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        tween.Finished += ghost.QueueFree;
    }

    private void SpawnDestructionFlash(Vector2 center)
    {
        Vector2[] verts = HexVertices();
        // Inset slightly so the flash sits inside the tile borders rather
        // than spilling over neighbors.
        var inset = new Vector2[6];
        for (int i = 0; i < 6; i++) inset[i] = verts[i] * 0.95f;

        var flash = new Polygon2D
        {
            Position = center,
            Polygon = inset,
            Color = new Color(1f, 1f, 1f, 0.95f),
        };
        _deathsLayer!.AddChild(flash);

        Tween tween = flash.CreateTween();
        tween.TweenProperty(flash, "modulate", new Color(1f, 1f, 1f, 0f), DestructionFlashDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        tween.Finished += flash.QueueFree;
    }

    /// <summary>
    /// Bright ring that starts at the tile center and expands to ~1.7x
    /// the hex radius while fading. Tinted toward the destroyed thing's
    /// color so unit / tower captures still feel distinct from each other.
    /// </summary>
    private void SpawnDestructionShockwave(Vector2 center, Color tint)
    {
        const int segments = 36;
        float startRadius = HexSize * 0.25f;
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            points[i] = new Vector2(startRadius * Mathf.Cos(a), startRadius * Mathf.Sin(a));
        }
        // Lerp the tint toward white so the ring stays bright on dark tiles.
        Color ringColor = new Color(
            (tint.R + 1f) * 0.5f,
            (tint.G + 1f) * 0.5f,
            (tint.B + 1f) * 0.5f,
            1f);

        var ring = new Line2D
        {
            Position = center,
            Points = points,
            Width = 5f,
            DefaultColor = ringColor,
        };
        _deathsLayer!.AddChild(ring);

        const float endScale = 1.7f * 1f / 0.25f; // expand 0.25 → 1.7 hex radii
        Tween tween = ring.CreateTween();
        tween.TweenProperty(ring, "scale", new Vector2(endScale, endScale), DestructionShockwaveDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(ring, "modulate", new Color(1f, 1f, 1f, 0f), DestructionShockwaveDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.Finished += ring.QueueFree;
    }

    private void SpawnDestructionShard(Vector2 center, Color color, int index, int total)
    {
        // Even radial spread plus a small jitter so bursts look organic
        // rather than mechanical.
        float baseAngle = Mathf.Tau * index / total;
        float jitter = (float)(DestructionRng.NextDouble() - 0.5) * 0.4f;
        float angle = baseAngle + jitter;
        Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        // Shard geometry: small irregular triangle. Roughly 2x what was
        // there before so individual shards read against tile fills.
        float r = HexSize * (0.11f + 0.06f * (float)DestructionRng.NextDouble());
        var verts = new[]
        {
            new Vector2(r, 0f),
            new Vector2(-r * 0.6f, r * 0.7f),
            new Vector2(-r * 0.5f, -r * 0.6f),
        };
        var shard = new Polygon2D
        {
            Position = center,
            Polygon = verts,
            Color = color,
            Rotation = (float)(DestructionRng.NextDouble() * Mathf.Tau),
        };
        // Black outline so colored shards stay legible against same-color
        // tile fills (e.g., red shards from a Red unit on a now-Red tile).
        var outlineVerts = new[] { verts[0], verts[1], verts[2], verts[0] };
        shard.AddChild(new Line2D
        {
            Points = outlineVerts,
            Width = 1.5f,
            DefaultColor = new Color(0f, 0f, 0f, 0.85f),
        });
        _deathsLayer!.AddChild(shard);

        // Travel distance: about a full hex outward, with variance.
        float travel = HexSize * (0.7f + 0.4f * (float)DestructionRng.NextDouble());
        Vector2 endPos = center + dir * travel;
        float endRotation = shard.Rotation + (float)(DestructionRng.NextDouble() * Mathf.Tau - Mathf.Pi);

        Tween tween = shard.CreateTween();
        tween.TweenProperty(shard, "position", endPos, DestructionShardDuration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(shard, "rotation", endRotation, DestructionShardDuration)
            .SetTrans(Tween.TransitionType.Linear);
        tween.Parallel().TweenProperty(shard, "modulate", new Color(1f, 1f, 1f, 0f), DestructionShardDuration)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.Finished += shard.QueueFree;
    }

    /// <summary>
    /// Pop a freshly-spawned grave visual in. If <paramref name="afterDeath"/>
    /// is true, the grave waits for the corpse on top to finish shrinking
    /// before growing in — so the two read as a single death-then-burial
    /// sequence rather than a crossfade.
    /// </summary>
    private static void StartGraveGrowAnimation(Node2D visual, bool afterDeath)
    {
        visual.Scale = new Vector2(0.05f, 0.05f);
        visual.Modulate = new Color(1f, 1f, 1f, 0f);
        Tween tween = visual.CreateTween();
        if (afterDeath)
        {
            tween.TweenInterval(ShrinkDurationSeconds);
        }
        tween.TweenProperty(visual, "scale", Vector2.One, GraveGrowDurationSeconds)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(visual, "modulate", new Color(1f, 1f, 1f, 1f), GraveGrowDurationSeconds * 0.7)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    private Node2D CreateTowerVisual()
    {
        Vector2[] verts = TowerShapeVertices();
        var body = new Polygon2D
        {
            Color = new Color(0.72f, 0.72f, 0.76f, 1f),
            Polygon = verts,
        };
        body.AddChild(BuildClosedOutline(verts, 2f, new Color(0f, 0f, 0f, 1f)));
        return body;
    }

    /// <summary>
    /// Tower preview drawn as just the outline (no fill) in the
    /// move-target green. Marks which tiles are legal tower placements
    /// while in BuildingTower mode.
    /// </summary>
    private Node2D CreateTowerPreviewVisual()
    {
        var node = new Node2D();
        Vector2[] verts = TowerShapeVertices();
        node.AddChild(BuildClosedOutline(verts, 3f, new Color(0.2f, 1f, 0.3f, 0.9f)));
        return node;
    }

    /// <summary>
    /// Vertices of the stylized stone-rook tower silhouette: a
    /// crenellated body sized ~0.32 * HexSize so it reads as a
    /// structural element distinct from the round unit discs.
    /// </summary>
    private Vector2[] TowerShapeVertices()
    {
        float r = HexSize * 0.32f;
        float halfW = r;
        float top = -r;
        float bot = r * 0.85f;
        float merlonH = r * 0.35f;
        float merlonW = halfW * 0.4f;

        return new[]
        {
            new Vector2(-halfW, bot),
            new Vector2(-halfW, top + merlonH),
            new Vector2(-halfW, top),
            new Vector2(-halfW + merlonW, top),
            new Vector2(-halfW + merlonW, top + merlonH),
            new Vector2(-merlonW * 0.5f, top + merlonH),
            new Vector2(-merlonW * 0.5f, top),
            new Vector2(merlonW * 0.5f, top),
            new Vector2(merlonW * 0.5f, top + merlonH),
            new Vector2(halfW - merlonW, top + merlonH),
            new Vector2(halfW - merlonW, top),
            new Vector2(halfW, top),
            new Vector2(halfW, top + merlonH),
            new Vector2(halfW, bot),
        };
    }

    private static Line2D BuildClosedOutline(Vector2[] verts, float width, Color color)
    {
        var points = new Vector2[verts.Length + 1];
        for (int i = 0; i < verts.Length; i++) points[i] = verts[i];
        points[verts.Length] = verts[0];
        return new Line2D
        {
            Points = points,
            Width = width,
            DefaultColor = color,
        };
    }

    private Node2D CreateTreeVisual()
    {
        // Stylized conifer: dark green triangle with a small brown trunk.
        float r = HexSize * 0.45f;
        var canopyVerts = new[]
        {
            new Vector2(0f, -r),
            new Vector2(r * 0.85f, r * 0.4f),
            new Vector2(-r * 0.85f, r * 0.4f),
        };
        var canopy = new Polygon2D
        {
            Color = new Color(0.16f, 0.48f, 0.18f, 1f),
            Polygon = canopyVerts,
        };

        var outline = new Line2D
        {
            Points = new[]
            {
                canopyVerts[0], canopyVerts[1], canopyVerts[2], canopyVerts[0],
            },
            Width = 2f,
            DefaultColor = new Color(0f, 0f, 0f, 1f),
        };
        canopy.AddChild(outline);

        float tw = r * 0.18f;
        float ttop = r * 0.4f;
        float tbot = r * 0.75f;
        var trunk = new Polygon2D
        {
            Color = new Color(0.36f, 0.22f, 0.1f, 1f),
            Polygon = new[]
            {
                new Vector2(-tw, ttop),
                new Vector2(tw, ttop),
                new Vector2(tw, tbot),
                new Vector2(-tw, tbot),
            },
        };
        var trunkOutline = new Line2D
        {
            Points = new[]
            {
                new Vector2(-tw, ttop),
                new Vector2(tw, ttop),
                new Vector2(tw, tbot),
                new Vector2(-tw, tbot),
                new Vector2(-tw, ttop),
            },
            Width = 1.5f,
            DefaultColor = new Color(0f, 0f, 0f, 1f),
        };
        trunk.AddChild(trunkOutline);
        canopy.AddChild(trunk);

        return canopy;
    }

    // Unit ring radii, ordered outer → inner. The peasant gets just the
    // outer ring; spearman adds the middle ring; knight adds the inner;
    // baron adds a filled center dot on top of the knight's three rings.
    // The outer ring matches the move-target ring radius (0.55 * HexSize)
    // so a unit reads as the same on-tile footprint as the capture/chop
    // target indicator.
    private static readonly float[] UnitRingRadii = { 0.55f, 0.37f, 0.20f };
    private const float UnitRingWidth = 3f;
    private const float UnitDotRadius = 0.075f;
    private const int UnitRingSegments = 28;

    private Node2D CreateUnitVisual(bool selected, UnitLevel level)
    {
        Color color = selected ? OccupantSelectedColor : OccupantDefaultColor;
        var node = new Node2D();

        int rings = level switch
        {
            UnitLevel.Peasant => 1,
            UnitLevel.Spearman => 2,
            UnitLevel.Knight => 3,
            UnitLevel.Baron => 3,
            _ => 1,
        };

        for (int i = 0; i < rings; i++)
        {
            node.AddChild(CreateCircleOutline(HexSize * UnitRingRadii[i], color, UnitRingWidth));
        }

        if (level == UnitLevel.Baron)
        {
            node.AddChild(CreateFilledDisc(HexSize * UnitDotRadius, color));
        }

        return node;
    }

    private static Line2D CreateCircleOutline(float radius, Color color, float width)
    {
        var points = new Vector2[UnitRingSegments + 1];
        for (int i = 0; i <= UnitRingSegments; i++)
        {
            float angle = Mathf.Tau * i / UnitRingSegments;
            points[i] = new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
        }
        return new Line2D
        {
            Points = points,
            Width = width,
            DefaultColor = color,
        };
    }

    private static Polygon2D CreateFilledDisc(float radius, Color color)
    {
        const int segments = 16;
        var verts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            verts[i] = new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
        }
        return new Polygon2D
        {
            Color = color,
            Polygon = verts,
        };
    }

    private Node2D CreateGraveVisual()
    {
        // Symmetrical grey plus sign (cross) — symmetric in both axes.
        // Visually distinct from units (circles) and capitals (diamonds).
        float r = HexSize * 0.38f;
        float w = r * 0.32f; // half-width of each arm
        var verts = new[]
        {
            new Vector2(-w, -r),
            new Vector2( w, -r),
            new Vector2( w, -w),
            new Vector2( r, -w),
            new Vector2( r,  w),
            new Vector2( w,  w),
            new Vector2( w,  r),
            new Vector2(-w,  r),
            new Vector2(-w,  w),
            new Vector2(-r,  w),
            new Vector2(-r, -w),
            new Vector2(-w, -w),
        };

        var body = new Polygon2D
        {
            Color = new Color(0.55f, 0.55f, 0.55f, 1f),
            Polygon = verts,
        };

        var outlinePoints = new Vector2[verts.Length + 1];
        for (int i = 0; i < verts.Length; i++) outlinePoints[i] = verts[i];
        outlinePoints[verts.Length] = verts[0];

        var outline = new Line2D
        {
            Points = outlinePoints,
            Width = 2f,
            DefaultColor = new Color(0f, 0f, 0f, 1f),
        };
        body.AddChild(outline);

        return body;
    }

    // Capital star: outer point sized between the old diamond
    // (0.35 * HexSize) and the move-target ring (0.55 * HexSize), with
    // the inner radius set near the geometric pentagram ratio
    // (~0.382 * outer) for a clean five-point silhouette.
    private const float CapitalStarOuterRadius = 0.48f;
    private const float CapitalStarInnerRadius = 0.20f;

    private Node2D CreateCapitalVisual(bool selected)
    {
        Color color = selected ? OccupantSelectedColor : OccupantDefaultColor;
        float outer = HexSize * CapitalStarOuterRadius;
        float inner = HexSize * CapitalStarInnerRadius;

        var verts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            // Start at the top point and walk clockwise alternating
            // outer/inner vertices around the center.
            float angle = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
            float r = (i % 2 == 0) ? outer : inner;
            verts[i] = new Vector2(r * Mathf.Cos(angle), r * Mathf.Sin(angle));
        }

        return new Polygon2D
        {
            Color = color,
            Polygon = verts,
        };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            EmitCoordHovered(motion.Position);
            if (_paintActive)
            {
                EmitPaintCellIfChanged(motion.Position);
                GetViewport().SetInputAsHandled();
                return;
            }
            if (!_dragCandidate) return;
            Vector2 delta = motion.Position - _dragStartScreen;
            if (!_isDragging && delta.Length() > DragThresholdPx) _isDragging = true;
            if (_isDragging)
            {
                Position = ClampPan(_dragStartMapPosition + delta);
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            int keyDelta = key.Keycode switch
            {
                Key.Equal or Key.KpAdd => +1,
                Key.Minus or Key.KpSubtract => -1,
                _ => 0,
            };
            if (keyDelta != 0)
            {
                StepZoom(keyDelta, VisualCenter());
                GetViewport().SetInputAsHandled();
                return;
            }

            // Discrete pan: one tap = one fixed step. Modeled on zoom
            // (event-driven, ignore echo) so a focused LineEdit / open
            // popup gates the input via Godot's input chain, no manual
            // check needed.
            Vector2 panDir = key.Keycode switch
            {
                Key.W or Key.Up    => new Vector2(0f, -1f),
                Key.S or Key.Down  => new Vector2(0f, +1f),
                Key.A or Key.Left  => new Vector2(-1f, 0f),
                Key.D or Key.Right => new Vector2(+1f, 0f),
                _ => Vector2.Zero,
            };
            if (panDir != Vector2.Zero)
            {
                // Right/Down keys move the world view in that direction,
                // which means translating the map node OPPOSITE.
                Position = ClampPan(Position - panDir * KeyboardPanStepPx);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        // macOS trackpad two-finger scroll: smooth continuous zoom.
        // Negative delta.Y (swipe up) zooms in. exp() gives an
        // equal-feeling multiplicative change at any zoom level;
        // anchoring on the gesture position keeps the hex under the
        // cursor put.
        if (@event is InputEventPanGesture pan)
        {
            ApplyZoom(_zoom * Mathf.Exp(-pan.Delta.Y * TrackpadScrollSensitivity), pan.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        // macOS trackpad pinch: scale _zoom by the gesture's per-event
        // factor directly so the on-screen size tracks the fingers 1:1.
        if (@event is InputEventMagnifyGesture magnify)
        {
            ApplyZoom(_zoom * magnify.Factor, magnify.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is not InputEventMouseButton mouse) return;

        // Wheel zoom must be checked before the Left-button filter below
        // or wheel events would be silently dropped. Anchor on the cursor
        // so the hex under the pointer stays under the pointer.
        if (mouse.Pressed && (mouse.ButtonIndex == MouseButton.WheelUp || mouse.ButtonIndex == MouseButton.WheelDown))
        {
            int wheelDelta = mouse.ButtonIndex == MouseButton.WheelUp ? +1 : -1;
            StepZoom(wheelDelta, mouse.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (mouse.ButtonIndex != MouseButton.Left) return;

        if (mouse.Pressed)
        {
            if (DragMode == HexDragMode.Paint)
            {
                _paintActive = true;
                _lastPaintedCoord = default;
                EmitPaintCellIfChanged(mouse.Position, force: true);
                GetViewport().SetInputAsHandled();
                return;
            }
            _dragCandidate = true;
            _isDragging = false;
            _dragStartScreen = mouse.Position;
            _dragStartMapPosition = Position;
            _pressStartMsec = Time.GetTicksMsec();
            return;
        }

        // Button released. Paint stroke ends here in Paint mode and
        // suppresses the click events. In Pan mode, a drag swallows the
        // click; otherwise it falls through to the click dispatch below.
        if (_paintActive)
        {
            _paintActive = false;
            PaintStrokeEnded?.Invoke();
            GetViewport().SetInputAsHandled();
            return;
        }

        bool wasDragging = _isDragging;
        bool wasLongPress = !wasDragging
            && Time.GetTicksMsec() - _pressStartMsec >= LongPressMs;
        _dragCandidate = false;
        _isDragging = false;
        if (wasDragging) return;

        // Convert viewport position into our local space, then remove the
        // centering offset so the result is in axial-origin coordinates.
        Vector2 local = ToLocal(mouse.Position) - FirstHexCenterOffset;
        HexCoord coord = HexPixel.FromPixel(local, HexSize);

        // CoordClicked fires on every click that wasn't a drag, regardless
        // of whether the coord is in the grid — the editor needs to know
        // about water clicks too so it can paint over them. Long-press
        // is a play-scene rally gesture; the editor doesn't care about
        // the distinction.
        CoordClicked?.Invoke(coord);

        HexTile? hit = Grid.Contains(coord) ? Grid.Get(coord) : null;
        if (wasLongPress)
        {
            TileLongClicked?.Invoke(hit);
        }
        else if (hit != null)
        {
            TileClicked?.Invoke(hit);
        }
        else
        {
            OffGridClicked?.Invoke(coord);
        }
    }

    /// <summary>
    /// Emit <see cref="CoordHovered"/> with the hex under the cursor, or
    /// null when the cursor is outside the (Cols × Rows) offset rectangle.
    /// Skips the work entirely when no listener is attached — the play
    /// scene doesn't subscribe.
    ///
    /// Chrome (HUD strip, TutorialBuilder topbar, RecordPane strips) is
    /// handled by the natural input chain: chrome Controls have
    /// MouseFilter=Stop, so the underlying motion event is consumed
    /// before <see cref="_UnhandledInput"/> fires — this method is
    /// never called for cursors over chrome. The tooltip's own sensor
    /// Control catches the resulting <c>MouseExited</c> and clears any
    /// stale dwell state (see <see cref="HexHoverTooltip"/>).
    /// </summary>
    private void EmitCoordHovered(Vector2 viewportPos)
    {
        if (CoordHovered is null) return;

        Vector2 local = ToLocal(viewportPos) - FirstHexCenterOffset;
        HexCoord coord = HexPixel.FromPixel(local, HexSize);
        (int col, int row) = coord.ToOffset();
        bool inBounds = col >= 0 && col < Cols && row >= 0 && row < Rows;
        CoordHovered.Invoke(inBounds ? coord : (HexCoord?)null);
    }

    /// <summary>
    /// Emit <see cref="PaintCellEntered"/> when the cursor's current hex
    /// differs from the last one painted in this stroke (or always, when
    /// <paramref name="force"/> is set, used at stroke start). Coords
    /// outside the (Cols × Rows) offset rectangle are dropped — the
    /// editor's paint helpers no-op on out-of-bounds anyway, and this
    /// keeps the event clean.
    /// </summary>
    private void EmitPaintCellIfChanged(Vector2 viewportPos, bool force = false)
    {
        Vector2 local = ToLocal(viewportPos) - FirstHexCenterOffset;
        HexCoord coord = HexPixel.FromPixel(local, HexSize);
        (int col, int row) = coord.ToOffset();
        if (col < 0 || col >= Cols || row < 0 || row >= Rows) return;
        if (!force && coord.Equals(_lastPaintedCoord)) return;
        _lastPaintedCoord = coord;
        PaintCellEntered?.Invoke(coord);
    }

    /// <summary>Visible center of the play area in viewport space — accounts
    /// for the HUD's reserved strip at the top.</summary>
    private Vector2 VisualCenter()
    {
        Vector2 vp = GetViewportRect().Size;
        return new Vector2(vp.X * 0.5f, HudView.HudHeight + (vp.Y - HudView.HudHeight) * 0.5f);
    }

    /// <summary>Clamp the proposed Position so the map can't be dragged off-
    /// screen. If the map is smaller than the available area on an axis,
    /// that axis is locked to its centered value. The grid's effective
    /// pixel extent is PixelSize × _zoom because we apply zoom via
    /// Node2D.Scale.</summary>
    private Vector2 ClampPan(Vector2 desired)
    {
        Vector2 vp = GetViewportRect().Size;
        float availY = vp.Y - HudView.HudHeight;
        float w = PixelSize.X * _zoom;
        float h = PixelSize.Y * _zoom;
        float x = w <= vp.X
            ? (vp.X - w) * 0.5f
            : Mathf.Clamp(desired.X, vp.X - w, 0f);
        float y = h <= availY
            ? HudView.HudHeight + (availY - h) * 0.5f
            : Mathf.Clamp(desired.Y, HudView.HudHeight + availY - h, HudView.HudHeight);
        return new Vector2(x, y);
    }

    public void CenterOnTerritory(Territory territory)
    {
        if (!territory.HasCapital) return;
        // The capital's pixel position is in unscaled local space; scale
        // it to world space before subtracting from VisualCenter.
        Vector2 localCenter = FirstHexCenterOffset + HexPixel.ToPixel(territory.Capital!.Value, HexSize);
        Position = ClampPan(VisualCenter() - localCenter * _zoom);
    }

    /// <summary>Recompute zoom range and discrete levels for the current
    /// viewport size and snap _zoom into range. Called on _Ready and
    /// whenever the OS window resizes.</summary>
    private void RecomputeZoomLevels()
    {
        Vector2 vp = GetViewportRect().Size;
        Vector2 px = PixelSize;
        _zoomMin = ZoomMath.ComputeZoomMin(vp.X, vp.Y, HudView.HudHeight, px.X, px.Y);
        _zoomLevels = ZoomMath.BuildLevels(_zoomMin, ZoomLevelCount);

        _zoom = Mathf.Clamp(_zoom, _zoomMin, 1f);
        _zoomLevelIndex = ClosestLevelIndex(_zoom);
        Scale = new Vector2(_zoom, _zoom);
    }

    private int ClosestLevelIndex(float zoom)
    {
        int best = 0;
        float bestDelta = Mathf.Abs(_zoomLevels[0] - zoom);
        for (int i = 1; i < _zoomLevels.Length; i++)
        {
            float d = Mathf.Abs(_zoomLevels[i] - zoom);
            if (d < bestDelta)
            {
                bestDelta = d;
                best = i;
            }
        }
        return best;
    }

    private void OnViewportResized()
    {
        RecomputeZoomLevels();
        Position = ClampPan(Position);
    }

    /// <summary>Apply a new zoom factor while keeping the map point
    /// currently under <paramref name="anchorVp"/> (in viewport space)
    /// fixed under that same screen position. Clamps to the allowed
    /// range and re-syncs the discrete level index so subsequent
    /// wheel/key steps pick up from the right place.</summary>
    private void ApplyZoom(float newZoom, Vector2 anchorVp)
    {
        newZoom = Mathf.Clamp(newZoom, _zoomMin, 1f);
        if (Mathf.IsEqualApprox(newZoom, _zoom)) return;

        // ToLocal uses the current Position+Scale, so localUnderAnchor is
        // in the unscaled local frame. After we change Scale, we want
        // anchorVp == Position + localUnderAnchor * newZoom.
        Vector2 localUnderAnchor = ToLocal(anchorVp);
        Scale = new Vector2(newZoom, newZoom);
        _zoom = newZoom;
        Position = ClampPan(anchorVp - localUnderAnchor * newZoom);
        _zoomLevelIndex = ClosestLevelIndex(_zoom);
    }

    /// <summary>Discrete zoom step from wheel ticks or +/- keys. After a
    /// continuous gesture (pinch / two-finger scroll) the current _zoom
    /// may sit between levels; we step from the nearest level so a
    /// keypress always moves a visible step.</summary>
    private void StepZoom(int delta, Vector2 anchorVp)
    {
        int from = ClosestLevelIndex(_zoom);
        int next = Mathf.Clamp(from + delta, 0, _zoomLevels.Length - 1);
        if (next == from && Mathf.IsEqualApprox(_zoom, _zoomLevels[from])) return;
        ApplyZoom(_zoomLevels[next], anchorVp);
    }

    private Vector2[] HexVertices()
    {
        var verts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Pi / 180f * (60f * i - 30f);
            verts[i] = new Vector2(HexSize * Mathf.Cos(angle), HexSize * Mathf.Sin(angle));
        }
        return verts;
    }

    private Polygon2D CreateHexVisual(Vector2 position, Color color)
    {
        return new Polygon2D
        {
            Position = position,
            Color = color,
            Polygon = HexVertices(),
        };
    }

    private static readonly Color HexOutlineColor = new Color(0f, 0f, 0f, 0.4f);
    private const float HexOutlineWidth = 1.5f;

    // Draw each shared land/land edge exactly once (using a coord
    // tie-break for ownership) and each land/water edge exactly once.
    // Drawing each tile's full perimeter would double the line weight
    // on interior same-color seams while leaving coastal seams single,
    // which reads as uneven thickness.
    private void PopulateOutlinesLayer()
    {
        if (_outlinesLayer == null) return;
        Vector2[] verts = HexVertices();

        foreach (HexTile tile in _state.Grid.Tiles)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            for (int edge = 0; edge < 6; edge++)
            {
                int dir = EdgeToNeighborDirection[edge];
                HexCoord neighborCoord = tile.Coord.Neighbor(dir);
                HexTile? neighbor = _state.Grid.Get(neighborCoord);
                if (neighbor != null && CompareCoord(neighborCoord, tile.Coord) < 0)
                {
                    // Neighbor land tile is the canonical owner of this edge.
                    continue;
                }

                _outlinesLayer.AddChild(new Line2D
                {
                    Points = new[] { center + verts[edge], center + verts[(edge + 1) % 6] },
                    Width = HexOutlineWidth,
                    DefaultColor = HexOutlineColor,
                });
            }
        }
    }

    private static int CompareCoord(HexCoord a, HexCoord b)
    {
        if (a.Q != b.Q) return a.Q.CompareTo(b.Q);
        return a.R.CompareTo(b.R);
    }

    // Edge i of a hex (between vertex i and vertex (i+1)%6) maps to one
    // of HexCoord's 6 neighbor directions. Order derived from the vertex
    // angles in HexVertices() (60i-30 degrees, +Y down) and the direction
    // table in HexCoord.Directions (E, NE, NW, W, SW, SE).
    private static readonly int[] EdgeToDirection = { 0, 5, 4, 3, 2, 1 };

    // Foam strip width as a fraction of HexSize, measured perpendicular
    // to the shore edge into the water hex.
    private const float ShoreFoamInset = 0.30f;
    private static readonly Color ShoreFoamColor = new Color(0.95f, 1.0f, 1.0f);

    private static readonly Color WaterColor = new Color(0.20f, 0.42f, 0.65f, 1f);

    private Polygon2D CreateWaterHexVisual(Vector2 center)
    {
        // No Line2D outline — adjacent land hexes still draw their own
        // outlines on the water/land seam, but water/water seams should
        // read as one continuous body of water.
        return new Polygon2D
        {
            Position = center,
            Color = WaterColor,
            Polygon = HexVertices(),
        };
    }

    // For each water hex, group consecutive shore edges (edges whose
    // neighbor is land) into runs and emit one polygon per run. A run
    // covers edges runStart..runStart+runLen-1 and uses outer vertices
    // v[runStart..runStart+runLen] forward + inner vertices in reverse,
    // giving a single polygon with a continuous inner contour. This
    // smooths external corners of land into rounded foam wraps instead
    // of two strips meeting at a hard seam.
    //
    // The 6-shore-edge case (water hex fully enclosed by land) would
    // need a polygon-with-hole to handle in one piece, so we fall back
    // to emitting 6 quads — rare enough that the corner artifacts
    // there don't matter.
    private void AddShoreFoamStrips(Node2D parent, Vector2 center, HexCoord coord)
    {
        Vector2[] verts = HexVertices();
        bool[] isShore = new bool[6];
        int shoreCount = 0;
        for (int edge = 0; edge < 6; edge++)
        {
            int dir = EdgeToDirection[edge];
            if (_state.Grid.Get(coord.Neighbor(dir)) != null)
            {
                isShore[edge] = true;
                shoreCount++;
            }
        }
        if (shoreCount == 0) return;

        Vector2[] inner = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            inner[i] = verts[i] - verts[i].Normalized() * (HexSize * ShoreFoamInset);
        }

        if (shoreCount == 6)
        {
            for (int edge = 0; edge < 6; edge++)
            {
                EmitFoamStrip(parent, center,
                    verts[edge], verts[(edge + 1) % 6],
                    inner[(edge + 1) % 6], inner[edge]);
            }
            return;
        }

        // Start at a non-shore edge so we don't split a wrap-around run.
        int startEdge = 0;
        while (isShore[startEdge]) startEdge++;

        int processed = 0;
        int e = startEdge;
        while (processed < 6)
        {
            if (!isShore[e])
            {
                e = (e + 1) % 6;
                processed++;
                continue;
            }

            int runStart = e;
            int runLen = 0;
            while (runLen < 6 && isShore[e])
            {
                runLen++;
                processed++;
                e = (e + 1) % 6;
            }

            int outerCount = runLen + 1;
            var poly = new Vector2[outerCount * 2];
            var colors = new Color[outerCount * 2];
            for (int k = 0; k < outerCount; k++)
            {
                poly[k] = verts[(runStart + k) % 6];
                colors[k] = new Color(1f, 1f, 1f, 1f);
            }
            for (int k = 0; k < outerCount; k++)
            {
                poly[outerCount + k] = inner[(runStart + outerCount - 1 - k) % 6];
                colors[outerCount + k] = new Color(1f, 1f, 1f, 0f);
            }

            parent.AddChild(new Polygon2D
            {
                Position = center,
                Color = ShoreFoamColor,
                Polygon = poly,
                VertexColors = colors,
            });
        }
    }

    // Bridge polygon for the gap between strips on two water hexes that
    // meet at a protruding land vertex. Drawn as N independent triangles
    // (rather than a single fan polygon) because Polygon2D's auto
    // triangulation of a star-shaped vertex list isn't a fan and would
    // mis-interpolate the per-vertex alpha.
    private void AddCornerFoamDisk(Node2D parent, Vector2 worldCenter)
    {
        const int Segments = 8;
        float radius = HexSize * ShoreFoamInset * 1.1f;
        Color centerColor = new Color(1f, 1f, 1f, 1f);
        Color rimColor = new Color(1f, 1f, 1f, 0f);
        for (int i = 0; i < Segments; i++)
        {
            float a0 = i * Mathf.Tau / Segments;
            float a1 = (i + 1) * Mathf.Tau / Segments;
            Vector2 p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
            Vector2 p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
            parent.AddChild(new Polygon2D
            {
                Position = worldCenter,
                Color = ShoreFoamColor,
                Polygon = new[] { Vector2.Zero, p0, p1 },
                VertexColors = new[] { centerColor, rimColor, rimColor },
            });
        }
    }

    private void EmitFoamStrip(Node2D parent, Vector2 center,
        Vector2 outerA, Vector2 outerB, Vector2 innerB, Vector2 innerA)
    {
        parent.AddChild(new Polygon2D
        {
            Position = center,
            Color = ShoreFoamColor,
            Polygon = new[] { outerA, outerB, innerB, innerA },
            VertexColors = new[]
            {
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, 0f),
                new Color(1f, 1f, 1f, 0f),
            },
        });
    }

    private void DrawTerritoryBorders()
    {
        if (_bordersLayer == null) return;
        Vector2[] verts = HexVertices();

        foreach (HexTile tile in Grid.Tiles)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);

            for (int edge = 0; edge < 6; edge++)
            {
                int dir = EdgeToNeighborDirection[edge];
                HexCoord neighborCoord = tile.Coord.Neighbor(dir);
                HexTile? neighbor = Grid.Get(neighborCoord);

                bool isBoundary = neighbor == null || neighbor.Owner != tile.Owner;
                if (!isBoundary) continue;

                var line = new Line2D
                {
                    Points = new[] { center + verts[edge], center + verts[(edge + 1) % 6] },
                    Width = 4f,
                    DefaultColor = new Color(0f, 0f, 0f, 1f),
                };
                _bordersLayer.AddChild(line);
            }
        }
    }

    private void RedrawHighlight()
    {
        if (_highlightLayer == null) return;

        ClearLayer(_highlightLayer);

        if (_highlightedTerritory == null) return;

        Vector2[] verts = HexVertices();
        var inside = new HashSet<HexCoord>(_highlightedTerritory.Coords);

        foreach (HexCoord coord in _highlightedTerritory.Coords)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);

            for (int edge = 0; edge < 6; edge++)
            {
                int dir = EdgeToNeighborDirection[edge];
                HexCoord neighborCoord = coord.Neighbor(dir);

                if (inside.Contains(neighborCoord)) continue;

                var line = new Line2D
                {
                    Points = new[] { center + verts[edge], center + verts[(edge + 1) % 6] },
                    Width = 7f,
                    DefaultColor = new Color(1f, 1f, 1f, 1f),
                };
                _highlightLayer.AddChild(line);
            }
        }
    }
}
