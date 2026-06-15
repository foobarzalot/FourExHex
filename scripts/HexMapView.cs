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

    // Unit ring fill colors. White = "current player's unit that
    // still has its move this turn" (actionable); black = everything
    // else. The selected (picked-up) unit stays white — selection is
    // signalled by a dark halo behind the rings (see
    // ApplySelectionAffordance), not by color. Capitals use a
    // separate palette (UiPalette.Gold / UiPalette.BgDeep).
    private static readonly Color OccupantActionableColor = new Color(1f, 1f, 1f, 1f);
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
        RecomputeContentBox();
        // Frame the (content-aware) board if we're already in the tree — the
        // tutorial/preview path Inits an in-tree map after insets are wired, so
        // it must recenter here. The normal game Inits before AddChild (not in
        // tree); _Ready does its recenter there.
        if (IsInsideTree()) RecenterMap();
    }

    // Layered overlay children (added in this order so draw order is
    // fills -> outlines -> borders -> capitals -> trees -> graves ->
    // units -> deaths -> targets -> highlight). Trees draw under units
    // so the rare unit-on-tree-tile transient (e.g. mid-chop) reads
    // correctly. Deaths draws above units so a freshly-spawned grave
    // grow-in reads underneath the still-shrinking corpse it replaces.
    // Outlines live in their own layer (not as children of each fill)
    // and use per-tile player-dark borders, so adjacent same-color
    // tiles read as one smooth seam and adjacent different-color tiles
    // read as two thin dk lines side-by-side.
    private PolylineBatch? _outlinesLayer;
    private Node2D? _towerCoverageLayer;
    private PolylineBatch? _bordersLayer;
    private TriangleSoup? _goldBordersLayer;
    private Node2D? _capitalsLayer;
    private Node2D? _rejectionsLayer;
    private Node2D? _treesLayer;
    private Node2D? _gravesLayer;
    private Node2D? _unitsLayer;
    private Node2D? _deathsLayer;
    private Node2D? _targetsLayer;
    private Node2D? _towerTargetsLayer;
    private Node2D? _highlightLayer;
    private Node2D? _warningBadgesLayer;
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
    // True once _Ready hooked the viewport's SizeChanged (_ExitTree must
    // not disconnect a never-connected signal).
    private bool _viewportResizeHooked;

    // The territory currently drawn as highlighted. Pure view state — the
    // single source of truth lives in SessionState, but we cache it here
    // so RedrawHighlight doesn't need to take a parameter.
    private Territory? _highlightedTerritory;

    // The coord of the currently "picked up" unit (move source).
    // Drives the selection affordance (a dark halo behind that unit's
    // rings) and excludes that unit from the actionable pulse. Driven
    // by the controller via ShowMoveSource.
    private HexCoord? _selectedUnit;

    // The black hex drawn behind the selected unit's rings. Lives
    // in _unitsLayer at the selected unit's center. Built by
    // ApplySelectionAffordance, freed by ClearSelectionAffordance.
    private Node2D? _selectionBackdrop;

    // The player whose turn was active at the most recent
    // RefreshOccupantVisuals call. Cached so IsActionableUnit can
    // answer "is this coord still actionable?" between refreshes (used
    // by ShowMoveSource when restoring the pulse on a deselected
    // unit). Null while no turn is active (between games / pre-Init).
    private PlayerId? _currentPlayer;

    // Selection backdrop: a solid-black tile-sized hex drawn beneath
    // the selected unit's rings so the white rings sit on jet black
    // (instead of on the player's territory color).
    private static readonly Color SelectionBackdropColor = new Color(0f, 0f, 0f, 1f);

    // Every current-player unit that still has its move available this
    // turn. Each one pulses (scales up and back) in _Process so the
    // player can see at a glance which units are actionable. Rebuilt in
    // RefreshOccupantVisuals. The selected unit is deliberately
    // excluded so its lift+shadow reads as a static "held" state.
    private readonly HashSet<HexCoord> _pulsingUnits = new();

    // Every current-player capital whose territory can afford to buy
    // anything (a recruit is the cheapest purchase, so recruit
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

    // HUD-reserved insets the map must avoid when centering/clamping. The
    // HUD owns layout policy and pushes these via SetMapInsets; defaults
    // reproduce the legacy single-top-strip landscape behavior so nothing
    // changes until told otherwise (and so the headless view stays correct).
    private float _topInset = HudView.HudHeight;
    private float _bottomInset = 0f;

    // Board-local pixel pad (pre-zoom) around the nominal grid that the
    // player can scroll into. Sized to exceed worst-case D1 floating-HUD
    // occlusion (portrait bottom bar 200 + tutorial 60 = 260; portrait
    // stacked top chips ~148; landscape rails 78) so an edge hex can
    // always be panned clear of the chips/buttons that float over it.
    // Symmetric on all four sides — rotation flips axes so a single value
    // covers both orientations. Drives both the water rim render and the
    // ClampPan extent.
    private const float ScrollPaddingPx = 300f;

    // Board rotation: 0 in landscape, −90° (CCW) in portrait so the wide map
    // fills a tall viewport. The whole board node rotates; icon glyphs
    // counter-rotate (ApplyGlyphUpright) to stay upright. Resolved from the
    // viewport aspect via ScreenLayout, consistent with the HUD.
    private float _mapAngleRad = 0f;

    // Sensitivity for InputEventPanGesture (two-finger trackpad scroll).
    // Per-event delta is small and dimensionless (~0.03–1.1 in Godot 4.6
    // on macOS); exp() of the negated cumulative delta makes a brisk
    // swipe traverse the full zoom range in roughly one full gesture.
    private const float TrackpadScrollSensitivity = 0.04f;

    // Touchscreen pinch-to-zoom state. Touchscreens don't synthesize the
    // MagnifyGesture/PanGesture events the macOS trackpad sends, so we track
    // raw ScreenTouch/ScreenDrag fingers ourselves. _touchPoints maps each
    // active finger index → its last viewport position; a 2-finger gesture
    // drives the existing ApplyZoom path via ZoomMath.PinchZoom.
    // emulate_mouse_from_touch (Godot default) synthesizes mouse events from
    // finger 0 only, so single-finger tap/drag/long-press still flow through
    // the mouse code below untouched. _gestureWasPinch swallows the trailing
    // finger-0 mouse-release so ending a pinch never fires a spurious tap.
    private readonly Dictionary<int, Vector2> _touchPoints = new();
    private float _pinchPrevDist;
    private bool _gestureWasPinch;

    /// <summary>Pixel bounding box of the rendered grid, for centering.</summary>
    public Vector2 PixelSize => new Vector2(
        (Cols + 0.5f) * Mathf.Sqrt(3f) * HexSize,
        (1.5f * Rows + 0.5f) * HexSize);

    // The first hex (axial 0,0) is drawn at this offset from the view's
    // origin so the grid's visual bounding box starts at (0,0) local.
    private Vector2 FirstHexCenterOffset => new Vector2(
        0.5f * Mathf.Sqrt(3f) * HexSize,
        HexSize);

    // Unscaled board-pixel bounding box of the playable tiles (not the padded
    // nominal grid), cached when the state is set. Centering + pan-clamping
    // frame this so an off-center level still frames centered. Recomputed in
    // RecomputeContentBox (Init / ReloadState); the land set is static per game.
    private (float minX, float minY, float maxX, float maxY) _contentBox;

    private void RecomputeContentBox()
    {
        var coords = new System.Collections.Generic.List<HexCoord>();
        foreach (HexTile tile in _state.Grid.Tiles) coords.Add(tile.Coord);
        _contentBox = coords.Count > 0
            ? MapPlacement.ContentPixelBounds(coords, HexSize)
            : (0f, 0f, PixelSize.X, PixelSize.Y); // degenerate: fall back to grid
        int waterRimTiles = Mathf.CeilToInt(ScrollPaddingPx / (1.5f * HexSize)) + 1;
        Log.Debug(Log.LogCategory.Render,
            $"HexMapView: content box=({_contentBox.minX:0},{_contentBox.minY:0})-" +
            $"({_contentBox.maxX:0},{_contentBox.maxY:0}) vs grid PixelSize=({PixelSize.X:0},{PixelSize.Y:0}) " +
            $"scrollPad={ScrollPaddingPx:0} waterRimTiles={waterRimTiles} " +
            $"over {coords.Count} tiles.");
    }

    public override void _Ready()
    {
        BuildStateVisuals();

        // Resolve board rotation (portrait ⇒ −90° CCW) before the zoom/pan
        // math so the rotated extent is used from the first frame.
        ResolveRotation();

        // Compute zoom levels for the current viewport before the initial
        // pan so ClampPan/VisualCenter use the right effective extent. The
        // resize hook re-runs both whenever the OS window changes size.
        RecomputeZoomLevels();
        GetViewport().SizeChanged += OnViewportResized;
        _viewportResizeHooked = true;

        // Initial pan: geometric center of the map, clamped to bounds.
        // If the map fits in the viewport, ClampPan locks each axis to
        // its centered value (matches the previous one-shot centering
        // that lived in Main.cs).
        RecenterMap();
    }

    public override void _ExitTree()
    {
        // The root Window outlives this node across the game→menu swap;
        // without the unsubscribe a later resize invokes a handler on a
        // freed node. Guarded: disconnecting a never-connected Godot
        // signal errors.
        if (!_viewportResizeHooked) return;
        GetViewport().SizeChanged -= OnViewportResized;
        _viewportResizeHooked = false;
        Log.Debug(Log.LogCategory.Display,
            "HexMapView: viewport SizeChanged unsubscribed on exit");
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
        RecomputeContentBox();
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

        // Water cells + shoreline foam are STATIC (never change after init).
        // As individual Polygon2D they were ~1,870 separate canvas items =
        // ~1,870 draw calls every frame in the gl_compatibility renderer —
        // the dominant cost behind the device per-capture hitch (see
        // ARCHITECTURE.md "Draw-call batching (Android performance)").
        // Bake all of it into ONE vertex-colored triangle soup =
        // one draw call. Order matters within the soup: water first (behind),
        // foam after (on top). The whole bake sits behind the land tile
        // fills added below, matching the old child z-order.
        var bake = new TriangleSoupBuilder();
        Vector2[] waterHex = HexVertices();

        // Water cells — off-map for gameplay (not in _state.Grid), renderer
        // only — plus a render-only ring of rim water hexes that hides the
        // half-hex map edge at default zoom.
        foreach (HexCoord waterCoord in _state.WaterCoords)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(waterCoord, HexSize);
            bake.AddPolygon(center, waterHex, WaterColor, null);
        }
        // Rim depth (in tiles) sized to cover the ClampPan-allowed pad. Row
        // pitch is 1.5*HexSize (see PixelSize.Y); +1 is a safety margin so
        // the rendered water always extends beyond the reachable scroll
        // edge in both axes and at the corners.
        int waterRimMargin = Mathf.CeilToInt(ScrollPaddingPx / (1.5f * HexSize)) + 1;
        for (int row = -waterRimMargin; row < Rows + waterRimMargin; row++)
        {
            for (int col = -waterRimMargin; col < Cols + waterRimMargin; col++)
            {
                if (row >= 0 && row < Rows && col >= 0 && col < Cols) continue;
                HexCoord coord = HexCoord.FromOffset(col, row);
                Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
                bake.AddPolygon(center, waterHex, WaterColor, null);
            }
        }

        // Per-edge shoreline foam. Each shore edge gets one independent quad
        // — concave shorelines render cleanly because no interpolation
        // crosses between edges.
        foreach (HexCoord waterCoord in _state.WaterCoords)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(waterCoord, HexSize);
            AddShoreFoamStrips(bake, center, waterCoord);
        }
        for (int row = -waterRimMargin; row < Rows + waterRimMargin; row++)
        {
            for (int col = -waterRimMargin; col < Cols + waterRimMargin; col++)
            {
                if (row >= 0 && row < Rows && col >= 0 && col < Cols) continue;
                HexCoord coord = HexCoord.FromOffset(col, row);
                Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
                AddShoreFoamStrips(bake, center, coord);
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
                int dirA = EdgeToNeighborDirection[(i + 5) % 6];
                int dirB = EdgeToNeighborDirection[i];
                if (_state.Grid.Get(tile.Coord.Neighbor(dirA)) != null) continue;
                if (_state.Grid.Get(tile.Coord.Neighbor(dirB)) != null) continue;
                AddCornerFoamDisk(bake, landCenter + hexVerts[i]);
            }
        }

        var waterFoamBake = new TriangleSoup { Name = "WaterFoamBake" };
        AddChild(waterFoamBake);
        waterFoamBake.SetTriangles(bake.Points.ToArray(), bake.Colors.ToArray(), bake.Indices.ToArray());

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
        // Layer order on each land tile: fill → border (1.5px in this
        // tile's player-dark, full perimeter). The border sits on top
        // so seams between two players show both dk colors side-by-
        // side without one overdrawing the other. (The earlier
        // redesign also drew a bevel highlight + bottom-shadow chord
        // per tile, but the lines read as paper grain and hurt
        // readability — removed.)
        _outlinesLayer = new PolylineBatch { Name = "OutlinesLayer" };
        AddChild(_outlinesLayer);
        PopulateOutlinesLayer();

        Log.Debug(Log.LogCategory.Render, $"HexMapView: rendering {_state.Grid.Count} tiles across {_state.Territories.Count} territories.");
        Log.Debug(Log.LogCategory.Render,
            "HexMapView: player palette = " +
            string.Join(", ", System.Linq.Enumerable.Select(
                GameSettings.PlayerConfig, c => $"{c.Name}=#{c.Hex}")));

        // The tower-coverage tint sits above tile fills + outlines but
        // below borders, so the lift is subtle: the underlying territory
        // color shows through and crisp border lines stay on top.
        _towerCoverageLayer = new Node2D { Name = "TowerCoverageLayer" };
        AddChild(_towerCoverageLayer);
        _bordersLayer = new PolylineBatch { Name = "BordersLayer" };
        AddChild(_bordersLayer);
        // Gold-tile inner borders (issue #45): above territory borders so the
        // gold accent reads on top of the black boundary lines, but below
        // capitals / units / trees / towers so it never hides an occupant.
        // A filled hex-ring band (TriangleSoup) rather than a multiline stroke
        // so the corners miter cleanly with no gaps.
        _goldBordersLayer = new TriangleSoup { Name = "GoldBordersLayer" };
        AddChild(_goldBordersLayer);
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
        // Added last so badges draw on top of every other map layer
        // (including highlight, units, capitals).
        _warningBadgesLayer = new Node2D { Name = "WarningBadgesLayer" };
        AddChild(_warningBadgesLayer);
        // Rejection overlays sit on top of everything so a red flash is
        // unambiguous. Persistent — never cleared by RefreshOccupantVisuals
        // — so an in-flight tween doesn't get QueueFree'd mid-pulse.
        _rejectionsLayer = new Node2D { Name = "RejectionsLayer" };
        AddChild(_rejectionsLayer);

        DrawTerritoryBorders();
        DrawGoldBorders();
        DumpSceneComposition();
        // Occupant visuals are drawn by the controller via
        // RefreshOccupantVisuals once it knows the current player and
        // treasury. We don't draw them here because they'd all be non-CTA
        // by default and the controller would immediately overwrite them.
    }

    // Logs the just-completed frame's timing split when it ran long
    // (>50ms): CPU process/physics time plus render draw-call / object
    // counts, so a stall can be attributed to our C# (high cpuProc), the
    // GPU/canvas (high draws/objs), or an idle gap (all tiny). The whole
    // call is stripped from Release — the Performance reads never run there.
    [System.Diagnostics.Conditional("DEBUG")]
    private void LogLongFrame(double delta)
    {
        if (delta <= 0.05) return;
        double cpuProcMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        double cpuPhysMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        long draws = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        long objs = (long)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        Log.Debug(Log.LogCategory.Render,
            $"[hitch] long frame {delta * 1000.0:F0}ms cpuProc={cpuProcMs:F1}ms " +
            $"cpuPhys={cpuPhysMs:F1}ms draws={draws} objs={objs} @frame={Engine.GetProcessFrames()}");
    }

    private bool _composedDumped;

    // One-shot scene-composition dump: tallies every CanvasItem in the map
    // subtree by concrete type so we can see what makes up the per-frame
    // draw-call count (the device per-capture hitch was draw-call-bound; see
    // ARCHITECTURE.md "Draw-call batching (Android performance)"). Logs
    // once per session; whole method stripped from Release.
    [System.Diagnostics.Conditional("DEBUG")]
    private void DumpSceneComposition()
    {
        if (_composedDumped) return;
        _composedDumped = true;
        var counts = new System.Collections.Generic.SortedDictionary<string, int>();
        int total = 0;
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            if (n is CanvasItem && n != this)
            {
                string key = n.GetType().Name;
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                total++;
            }
            foreach (Node child in n.GetChildren()) stack.Push(child);
        }
        var sb = new System.Text.StringBuilder($"[hitch] composition total CanvasItems={total} :: ");
        foreach (System.Collections.Generic.KeyValuePair<string, int> kv in counts)
            sb.Append($"{kv.Key}={kv.Value} ");
        Log.Debug(Log.LogCategory.Render, sb.ToString());
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

        // Per-tile border colors are owner-keyed (player-dark per tile),
        // so an ownership change after a capture has to repaint the
        // OutlinesLayer too — fill recolor alone would leave a captured
        // tile's old player-dark stroke around its new fill.
        long tOutlines = Log.Stamp();
        PopulateOutlinesLayer();
        Log.Since(Log.LogCategory.Capture, "[hitch] PopulateOutlinesLayer", tOutlines);

        long tBorders = Log.Stamp();
        ClearLayer(_targetsLayer);
        DrawTerritoryBorders();
        DrawGoldBorders();
        Log.Since(Log.LogCategory.Capture, "[hitch] DrawTerritoryBorders", tBorders);
        Log.Debug(Log.LogCategory.Capture,
            $"[hitch] strokes outlines={_outlinesLayer?.StrokeCount ?? 0} " +
            $"borders={_bordersLayer?.StrokeCount ?? 0} trees={_treeVisuals.Count} graves={_graveVisuals.Count}");

        // Silent batch (AI under Instant): leave tree and grave visuals
        // in place. The controller skips the per-capture RefreshOccupant-
        // Visuals that would otherwise rebuild them, so tearing them
        // down here would make trees vanish for several frames until the
        // end-of-batch refresh recreates them. The final refresh diffs
        // _treeVisuals against the current model state and only frees
        // trees that were actually chopped — correct outcome, no flicker.
        if (_silentMode) return;

        long tTeardown = Log.Stamp();
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
        Log.Since(Log.LogCategory.Capture, "[hitch] tree/grave teardown", tTeardown);
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
    /// preview matches the unit shape (recruit=1 ring, soldier=2,
    /// captain=3, commander=3+dot) so the player sees what the destination
    /// will hold. Pass an empty list to clear.
    /// </summary>
    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level)
    {
        ClearLayer(_targetsLayer);
        if (_targetsLayer == null) return;

        var color = new Color(0.2f, 1f, 0.3f, 0.9f);
        int rings = level switch
        {
            UnitLevel.Recruit => 1,
            UnitLevel.Soldier => 2,
            UnitLevel.Captain => 3,
            UnitLevel.Commander => 3,
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
                preview.AddChild(CreateCircleOutline(
                    HexSize * UnitRingRadii[i], color, HexSize * UnitRingWidthFactors[i]));
            }
            if (level == UnitLevel.Commander)
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
        // Tower previews are built outside the RefreshOccupantVisuals pass, so
        // upright them here too (no-op at angle 0).
        ApplyGlyphUpright();
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
    /// Applies a dark-halo affordance behind that unit (and removes
    /// it from the actionable pulse set) and reverses the same effect
    /// on the previous selection. Pass null to clear. Both the new
    /// and previous units' rings stay white as long as they are
    /// still actionable — color is no longer a selection cue.
    /// </summary>
    public void ShowMoveSource(HexCoord? coord)
    {
        if (Equals(_selectedUnit, coord)) return;

        HexCoord? previous = _selectedUnit;
        _selectedUnit = coord;

        if (previous.HasValue)
        {
            ClearSelectionAffordance(previous.Value);
            if (IsActionableUnit(previous.Value)) _pulsingUnits.Add(previous.Value);
        }
        if (coord.HasValue)
        {
            _pulsingUnits.Remove(coord.Value);
            ApplySelectionAffordance();
        }

        Log.Debug(Log.LogCategory.Render,
            $"ShowMoveSource: prev={previous?.ToString() ?? "none"} next={coord?.ToString() ?? "none"} " +
            $"backdrop={(_selectionBackdrop != null ? "attached" : "cleared")}");
    }

    public override void _Process(double delta)
    {
        // Long-frame probe: catches the hitch frame as a whole, including
        // Godot's redraw/flush of newly created nodes that happens after
        // our capture-path C# returns (which the inline timers miss).
        LogLongFrame(delta);

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
    /// Attach the dark-halo selection affordance to
    /// <c>_selectedUnit</c> if its visual exists. No-op if there's no
    /// selection or the visual is missing (e.g., right after a move
    /// when the controller hasn't yet called ShowMoveSource(null)).
    /// Called from <see cref="ShowMoveSource"/> on pick-up and from
    /// <see cref="RefreshOccupantVisuals"/> to re-apply after a layer
    /// rebuild.
    /// </summary>
    private void ApplySelectionAffordance()
    {
        if (!_selectedUnit.HasValue) return;
        HexCoord coord = _selectedUnit.Value;
        if (!_unitVisuals.TryGetValue(coord, out Node2D? visual) || visual == null) return;

        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);

        // Free any leftover shadow before building a new one.
        if (_selectionBackdrop != null)
        {
            _selectionBackdrop.QueueFree();
            _selectionBackdrop = null;
        }

        // The backdrop is a tile-sized black hexagon at the unit's
        // center so the white rings read on jet black instead of on
        // the territory's player color. CreateHexVisual returns a
        // Polygon2D, which is itself a Node2D — store it in
        // _selectionBackdrop.
        Polygon2D backdrop = CreateHexVisual(center, SelectionBackdropColor);
        _unitsLayer?.AddChild(backdrop);
        // Put the backdrop immediately under the unit visual in child
        // order so it draws beneath the rings (later children draw on
        // top in a Node2D).
        if (_unitsLayer != null) _unitsLayer.MoveChild(backdrop, visual.GetIndex());
        _selectionBackdrop = backdrop;

        // Cancel any in-flight pulse scaling so the selection reads
        // as a static halo; the next _Process tick will leave the
        // scale alone since the selected unit is excluded from
        // _pulsingUnits.
        visual.Scale = Vector2.One;
    }

    /// <summary>
    /// Reverse <see cref="ApplySelectionAffordance"/> for the given
    /// (previously-selected) coord: free the shadow and reset the
    /// unit's scale so the next pulse tick starts from identity. The
    /// unit's color and position are unaffected — color stays white
    /// as long as it's still actionable, and the unit was never
    /// moved by the affordance.
    /// </summary>
    private void ClearSelectionAffordance(HexCoord coord)
    {
        if (_selectionBackdrop != null)
        {
            _selectionBackdrop.QueueFree();
            _selectionBackdrop = null;
        }
        if (_unitVisuals.TryGetValue(coord, out Node2D? visual) && visual != null)
        {
            visual.Scale = Vector2.One;
        }
    }

    /// <summary>
    /// True iff the live unit at <paramref name="coord"/> belongs to
    /// the current turn's player and still has its move available
    /// this turn. Reads <c>_currentPlayer</c> cached by the most
    /// recent <see cref="RefreshOccupantVisuals"/>.
    /// </summary>
    private bool IsActionableUnit(HexCoord coord)
    {
        if (!_currentPlayer.HasValue) return false;
        HexTile? tile = _state.Grid.Get(coord);
        if (tile?.Occupant is not Unit unit) return false;
        return unit.Owner == _currentPlayer.Value && !unit.HasMovedThisTurn;
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
        // Cache for IsActionableUnit so it can recompute the
        // actionable predicate without the controller passing the
        // player in again on every selection toggle.
        _currentPlayer = currentPlayer;

        // The previous _selectionBackdrop (if any) was a child of the
        // _unitsLayer that's about to be cleared; the ClearLayer call
        // below will QueueFree it. Drop the reference so we don't try
        // to free it again later. ApplySelectionAffordance below
        // re-attaches a fresh shadow if _selectedUnit still exists.
        _selectionBackdrop = null;
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

        var actionableCapitals = new HashSet<HexCoord>();
        if (currentPlayer.HasValue)
        {
            foreach (Territory territory in Territories)
            {
                if (territory.Owner != currentPlayer.Value) continue;
                if (!territory.HasCapital) continue;
                // The recruit is the cheapest purchase at every
                // difficulty (base < tower cost in every column), so
                // recruit-affordability is a sufficient proxy for "this
                // territory can spend gold".
                if (PurchaseRules.CanAffordRecruit(territory, treasury,
                        _state.Turns.CurrentPlayer.Difficulty))
                {
                    actionableCapitals.Add(territory.Capital!.Value);
                }
            }
        }
        // Diagnostic for the device-only "actionable capital stays dark" report:
        // emits only when the actionable set changes (this runs on every refresh,
        // so per-call logging would flood the log).
        LogActionableCapitalsIfChanged(actionableCapitals);

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

        long tRebuildLoop = Log.Stamp();
        foreach (HexTile tile in Grid.Tiles)
        {
            if (tile.Occupant == null) continue;

            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);

            if (tile.Occupant is Unit unit)
            {
                bool actionable = currentPlayer.HasValue
                    && unit.Owner == currentPlayer.Value
                    && !unit.HasMovedThisTurn;
                bool selected = _selectedUnit.HasValue && _selectedUnit.Value == tile.Coord;
                Node2D visual = CreateUnitVisual(actionable, unit.Level);
                visual.Position = center;
                _unitsLayer?.AddChild(visual);
                _unitVisuals[tile.Coord] = visual;

                // The selected unit is rendered static (no pulse) and
                // gets a lift + drop shadow applied by
                // ApplySelectionAffordance below. Pulsing the selected
                // unit on top of the lift looked like a bug, so we
                // exclude it.
                if (actionable && !selected) _pulsingUnits.Add(tile.Coord);
            }
            else if (tile.Occupant is Capital)
            {
                bool actionable = actionableCapitals.Contains(tile.Coord);
                Node2D visual = CreateCapitalVisual(actionable);
                visual.Position = center;
                _capitalsLayer?.AddChild(visual);
                _capitalVisuals[tile.Coord] = visual;

                if (actionable) _pulsingCapitals.Add(tile.Coord);
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

        Log.Since(Log.LogCategory.Capture, "[hitch] occupant rebuild loop", tRebuildLoop);
        Log.Debug(Log.LogCategory.Capture,
            $"[hitch] occupants units={_unitVisuals.Count} capitals={_capitalVisuals.Count}");

        _animateNewTrees = true;
        _animateNewGraves = true;

        // Re-apply the selection halo on the selected unit now that
        // its visual has been freshly built. ApplySelectionAffordance
        // is a no-op if there's no selection or if the unit's visual
        // is missing.
        ApplySelectionAffordance();

        // Proves the actionable→white rule actually ran and reports
        // the selection state alongside it. Enable with
        // FOUREXHEX_LOG="Render:Debug".
        int actionableCount = _pulsingUnits.Count + (_selectedUnit.HasValue ? 1 : 0);
        int otherUnitCount = _unitVisuals.Count - actionableCount;
        Log.Debug(Log.LogCategory.Render,
            $"RefreshOccupantVisuals: actionable(white)={actionableCount} other(black)={otherUnitCount} " +
            $"selected={_selectedUnit?.ToString() ?? "none"} currentPlayer={currentPlayer?.ToString() ?? "none"}");

        // Repaint warning badges on every refresh: the human just
        // bought / moved / undid something that could have flipped a
        // territory between Healthy / NegativeDelta / BankruptNextTurn.
        // The badge layer is also cleared here for AI turns.
        long tBadges = Log.Stamp();
        RedrawWarningBadges();
        Log.Since(Log.LogCategory.Capture, "[hitch] RedrawWarningBadges", tBadges);

        // Freshly-built glyphs default to Rotation 0; counter-rotate them so
        // they stay upright when the board is rotated (no-op at angle 0).
        long tGlyph = Log.Stamp();
        ApplyGlyphUpright();
        Log.Since(Log.LogCategory.Capture, "[hitch] ApplyGlyphUpright", tGlyph);
    }

    // Previous actionable-capital set, so the diagnostic below logs only on a
    // change instead of on every RefreshOccupantVisuals.
    private readonly HashSet<HexCoord> _lastActionableCapitals = new();

    // Diagnostic for the device "actionable capital renders dark" report. Logs
    // only when the set changes. No release special-casing: the string.Join is
    // an argument to Log.Debug, which is itself [Conditional("DEBUG")], so the
    // whole message (and its formatting) is compiled out of release builds. If
    // a dark star's coord IS in this set it's a render bug; if it's absent it's
    // data. (On mobile debug builds every Log category is on — see LogBootstrap.)
    private void LogActionableCapitalsIfChanged(HashSet<HexCoord> actionable)
    {
        if (actionable.SetEquals(_lastActionableCapitals)) return;
        _lastActionableCapitals.Clear();
        _lastActionableCapitals.UnionWith(actionable);
        Log.Debug(Log.LogCategory.Render,
            $"actionable capitals changed: count={actionable.Count} coords=[{string.Join(", ", actionable)}]");
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
        //
        // Two nested nodes so rotation and the grow pivot don't fight:
        //  - placement: origin AT the tile center. ApplyGlyphUpright
        //    counter-rotates THIS, pivoting around the center, so a rotated
        //    board keeps the tree in place and upright.
        //  - anchor (child): origin at the trunk base, shifted down by
        //    trunkBottomOffset, with the tree drawn back up by the same
        //    amount — so the grow-in scale animation reads as "rising out of
        //    the ground" without moving the placement.
        float trunkBottomOffset = HexSize * 0.225f;
        var placement = new Node2D { Position = center };
        var anchor = new Node2D { Position = new Vector2(0f, trunkBottomOffset) };
        Node2D tree = CreateTreeVisual();
        tree.Position = new Vector2(0f, -trunkBottomOffset);
        anchor.AddChild(tree);
        placement.AddChild(anchor);
        return placement;
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
    private static void StartTreeGrowAnimation(Node2D placement, bool afterShrink = false)
    {
        // Scale the inner anchor (pivot at the trunk base), not the placement
        // node (pivot at the tile center) — so the tree rises out of the
        // ground rather than swelling from its middle.
        Node2D anchor = placement.GetChild<Node2D>(0);
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
    /// Speed setting, or for the whole of an instant-speed replay —
    /// gates every per-action sound/anim spawn call AND the Bankruptcy/
    /// GameWon cues so the fast-forward is fully silent from the human's
    /// perspective. Toggled by GameController. A human still hears their
    /// own bankruptcy / game-won fanfare because a human's own turn is
    /// never silent (this flag is only set while an AI acts under
    /// Instant, or across an instant replay — never on a live human
    /// turn). Game-over visual overlays flow through Refresh, not
    /// PlaySound, so they render regardless.</summary>
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
            Tree => BoardPalette.ForestCanopy,
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
        ApplyGlyphUpright();
    }

    /// <summary>
    /// Single sound-dispatch entry point. The <paramref name="at"/> coord
    /// is unused today — AudioBus plays through a single non-spatial 2D
    /// player — but the parameter keeps room for a later positional
    /// implementation without touching every caller. Silent-mode gates
    /// every cue with no exceptions: a silent AI-Instant batch or an
    /// instant replay is a fully silent fast-forward. Bankruptcy/GameWon
    /// still reach a human because a human's own turn is never silent
    /// (silent mode is only on while an AI acts under Instant, or for the
    /// whole of an instant replay).
    /// </summary>
    public void PlaySound(SoundEffect kind, HexCoord? at = null)
    {
        if (_silentMode) return;
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
                RejectionShape.Recruit => UnitLevel.Recruit,
                RejectionShape.Soldier => UnitLevel.Soldier,
                RejectionShape.Captain => UnitLevel.Captain,
                RejectionShape.Commander => UnitLevel.Commander,
                _ => UnitLevel.Recruit,
            };
            root.AddChild(BuildRedUnitGhost(level));
        }

        root.AddChild(BuildForbiddenSlash());
        return root;
    }

    private Node2D BuildRedUnitGhost(UnitLevel level)
    {
        Color red = BoardPalette.RejectRed;
        var node = new Node2D();
        int rings = level switch
        {
            UnitLevel.Recruit => 1,
            UnitLevel.Soldier => 2,
            UnitLevel.Captain => 3,
            UnitLevel.Commander => 3,
            _ => 1,
        };
        for (int i = 0; i < rings; i++)
        {
            node.AddChild(CreateCircleOutline(
                HexSize * UnitRingRadii[i], red, HexSize * UnitRingWidthFactors[i]));
        }
        if (level == UnitLevel.Commander)
        {
            node.AddChild(CreateFilledDisc(HexSize * UnitDotRadius, red));
        }
        return node;
    }

    private Node2D BuildRedTowerGhost()
    {
        Color red = BoardPalette.RejectRed;
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
        Color red = BoardPalette.RejectRed;
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

    // Castle fill + stroke per the redesign spec: warm dark slate body
    // (#4a4640) with a thin ink-faint stroke (no thick black outline).
    private static readonly Color CastleFillColor = BoardPalette.CastleFill;
    private static readonly Color CastleStrokeColor = UiPalette.BgDeep;

    private Node2D CreateTowerVisual()
    {
        Vector2[] verts = TowerShapeVertices();
        var body = new Polygon2D
        {
            Color = CastleFillColor,
            Polygon = verts,
        };
        body.AddChild(BuildClosedOutline(verts, 1.2f, CastleStrokeColor));
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

    // Conifer: dark green canopy triangle with a brown trunk, both
    // stroked in BgDeep. Matches the HUD palette's tree icon
    // (HudIcons.DrawTree) so the same shape appears in the map editor
    // toolbar swatch and on the tile itself. The redesign spec called
    // for a single bare triangle, but the user preferred the trunked
    // conifer — easier to read at small scales and visually consistent
    // with the existing icon language.
    private static readonly Color ForestCanopyColor = BoardPalette.ForestCanopy;
    private static readonly Color ForestTrunkColor = BoardPalette.ForestTrunk;
    private static readonly Color ForestStrokeColor = UiPalette.BgDeep;

    private Node2D CreateTreeVisual()
    {
        float r = HexSize * 0.45f;
        var canopyVerts = new[]
        {
            new Vector2(0f, -r),
            new Vector2(r * 0.85f, r * 0.4f),
            new Vector2(-r * 0.85f, r * 0.4f),
        };
        var canopy = new Polygon2D
        {
            Color = ForestCanopyColor,
            Polygon = canopyVerts,
        };
        canopy.AddChild(BuildClosedOutline(canopyVerts, 1.5f, ForestStrokeColor));

        float tw = r * 0.18f;
        float ttop = r * 0.4f;
        float tbot = r * 0.75f;
        var trunkVerts = new[]
        {
            new Vector2(-tw, ttop),
            new Vector2( tw, ttop),
            new Vector2( tw, tbot),
            new Vector2(-tw, tbot),
        };
        var trunk = new Polygon2D
        {
            Color = ForestTrunkColor,
            Polygon = trunkVerts,
        };
        trunk.AddChild(BuildClosedOutline(trunkVerts, 1.5f, ForestStrokeColor));
        canopy.AddChild(trunk);

        return canopy;
    }

    // Unit ring radii (outer → inner) per the redesign spec: recruit gets
    // just the outer ring; soldier adds the middle; captain adds the
    // inner; commander adds a filled center dot on top of the captain's three
    // rings. The outer ring matches the move-target ring radius
    // (0.50 * HexSize) so a unit reads as the same on-tile footprint as
    // the capture/chop target indicator. Stroke widths scale with HexSize
    // (outer thickest, inner thinnest) so the concentric rings read as a
    // single graphic instead of three independent circles.
    private static readonly float[] UnitRingRadii = { 0.50f, 0.34f, 0.20f };
    private static readonly float[] UnitRingWidthFactors = { 0.06f, 0.05f, 0.045f };
    private const float UnitDotRadius = 0.08f;
    private const int UnitRingSegments = 28;

    private Node2D CreateUnitVisual(bool actionable, UnitLevel level)
    {
        Color color = actionable ? OccupantActionableColor : OccupantDefaultColor;
        var node = new Node2D();

        int rings = level switch
        {
            UnitLevel.Recruit => 1,
            UnitLevel.Soldier => 2,
            UnitLevel.Captain => 3,
            UnitLevel.Commander => 3,
            _ => 1,
        };

        for (int i = 0; i < rings; i++)
        {
            node.AddChild(CreateCircleOutline(
                HexSize * UnitRingRadii[i], color, HexSize * UnitRingWidthFactors[i]));
        }

        if (level == UnitLevel.Commander)
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

    // Dead-unit cross per the redesign spec: two round-capped diagonal
    // strokes in muted slate (oklch 0.45 0.012 60 ≈ #74706a). The spec
    // color is near-isoluminant with several player fills (Red, Blue,
    // Green, Purple all sit around oklch L≈0.5), so each diagonal is
    // doubled — a slightly wider BgDeep underlay first, then the slate
    // on top — giving every X a dark halo that keeps it legible on any
    // player color.
    private static readonly Color GraveCrossColor = BoardPalette.GraveCross;
    private const float GraveCrossArmReach = 0.32f;
    private const float GraveCrossWidthFactor = 0.10f;
    private const float GraveCrossHaloWidthFactor = 0.14f;

    private Node2D CreateGraveVisual()
    {
        float reach = HexSize * GraveCrossArmReach;
        float coreWidth = HexSize * GraveCrossWidthFactor;
        float haloWidth = HexSize * GraveCrossHaloWidthFactor;
        var node = new Node2D();
        var diag1 = new[] { new Vector2(-reach, -reach), new Vector2(reach, reach) };
        var diag2 = new[] { new Vector2(-reach, reach), new Vector2(reach, -reach) };

        node.AddChild(BuildGraveStroke(diag1, haloWidth, UiPalette.BgDeep));
        node.AddChild(BuildGraveStroke(diag2, haloWidth, UiPalette.BgDeep));
        node.AddChild(BuildGraveStroke(diag1, coreWidth, GraveCrossColor));
        node.AddChild(BuildGraveStroke(diag2, coreWidth, GraveCrossColor));
        return node;
    }

    private static Line2D BuildGraveStroke(Vector2[] points, float width, Color color)
    {
        return new Line2D
        {
            Points = points,
            Width = width,
            DefaultColor = color,
            Antialiased = true,
            BeginCapMode = Line2D.LineCapMode.Round,
            EndCapMode = Line2D.LineCapMode.Round,
        };
    }

    // Capital star: outer point sized between the old diamond
    // (0.35 * HexSize) and the move-target ring (0.55 * HexSize), with
    // the inner radius set near the geometric pentagram ratio
    // (~0.382 * outer) for a clean five-point silhouette.
    private const float CapitalStarOuterRadius = 0.48f;
    private const float CapitalStarInnerRadius = 0.20f;

    // Capital star: BgDeep fill with a thin white stroke so the
    // silhouette stays legible on any player fill. The fill flips to
    // brass (gold) iff the capital is actionable — owned by the current
    // player with an affordable action — which is also exactly when it
    // pulses. Bright == has an action; idle (dark) == nothing to do.
    private static readonly Color CapitalStrokeColor = new Color(1f, 1f, 1f, 0.95f);
    private const float CapitalStrokeWidth = 0.6f;

    private Node2D CreateCapitalVisual(bool actionable)
    {
        Color fill = actionable ? UiPalette.Gold : UiPalette.BgDeep;
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

        var body = new Polygon2D
        {
            Color = fill,
            Polygon = verts,
        };
        body.AddChild(BuildClosedOutline(verts, CapitalStrokeWidth, CapitalStrokeColor));
        return body;
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
                LogCameraState("keypan");
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

        // Touchscreen multi-touch. A second finger landing begins a pinch;
        // we cancel the in-flight finger-0 drag/click so the map doesn't pan
        // mid-pinch and the trailing release doesn't register as a tap.
        if (@event is InputEventScreenTouch touch)
        {
            if (touch.Pressed)
            {
                _touchPoints[touch.Index] = touch.Position;
                // First finger of a brand-new gesture: clear any stale pinch
                // flag so a normal tap isn't swallowed.
                if (_touchPoints.Count == 1) _gestureWasPinch = false;
                if (_touchPoints.Count == 2)
                {
                    _gestureWasPinch = true;
                    _dragCandidate = false;
                    _isDragging = false;
                    _pinchPrevDist = TwoFingerDistance();
                    Log.Debug(Log.LogCategory.Input,
                        $"HexMapView: pinch begin, startDist={_pinchPrevDist:0.0}.");
                    GetViewport().SetInputAsHandled();
                }
            }
            else
            {
                _touchPoints.Remove(touch.Index);
                if (_touchPoints.Count < 2 && _pinchPrevDist > 0f)
                {
                    Log.Debug(Log.LogCategory.Input,
                        $"HexMapView: pinch end at zoom={_zoom:0.000}.");
                    _pinchPrevDist = 0f;
                }
            }
            return;
        }

        if (@event is InputEventScreenDrag drag)
        {
            _touchPoints[drag.Index] = drag.Position;
            if (_touchPoints.Count == 2)
            {
                float curDist = TwoFingerDistance();
                float newZoom = ZoomMath.PinchZoom(_zoom, _pinchPrevDist, curDist);
                Vector2 anchor = TwoFingerMidpoint();
                Log.Debug(Log.LogCategory.Input,
                    $"HexMapView: pinch update dist {_pinchPrevDist:0.0}→{curDist:0.0}, zoom {_zoom:0.000}→{newZoom:0.000}.");
                ApplyZoom(newZoom, anchor);
                _pinchPrevDist = curDist;
                GetViewport().SetInputAsHandled();
            }
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

        // A pinch just ended: this is the trailing emulated finger-0 release.
        // Swallow it so the multi-touch gesture never registers as a tap or
        // (since pinches usually exceed the long-press threshold) a rally.
        if (_gestureWasPinch)
        {
            _gestureWasPinch = false;
            _dragCandidate = false;
            _isDragging = false;
            return;
        }

        bool wasDragging = _isDragging;
        bool wasLongPress = !wasDragging
            && Time.GetTicksMsec() - _pressStartMsec >= LongPressMs;
        _dragCandidate = false;
        _isDragging = false;
        if (wasDragging)
        {
            LogCameraState("pan");
            return;
        }

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
    /// for the HUD's reserved insets at the top and bottom.</summary>
    private Vector2 VisualCenter()
    {
        Vector2 vp = GetViewportRect().Size;
        float availY = vp.Y - _topInset - _bottomInset;
        return new Vector2(vp.X * 0.5f, _topInset + availY * 0.5f);
    }

    /// <summary>Clamp the proposed Position so the map can't be dragged off-
    /// screen. If the map is smaller than the available area on an axis,
    /// that axis is locked to its centered value. The grid's effective
    /// pixel extent is PixelSize × _zoom because we apply zoom via
    /// Node2D.Scale.</summary>
    private Vector2 ClampPan(Vector2 desired)
    {
        Vector2 vp = GetViewportRect().Size;
        float availX = vp.X;
        float availY = vp.Y - _topInset - _bottomInset;
        // On-screen bounding box of the (scaled + rotated) full nominal grid
        // (Cols×Rows), relative to this node's origin. Pan-clamping frames the
        // whole grid — NOT the content box — so panning stays as free as it was
        // before content-aware framing landed: a sparsely-painted editor map or
        // an off-center level can still pan across the full board. Initial
        // centering is content-aware separately in RecenterMap. At angle 0 this
        // reduces to (0,0,w·zoom,h·zoom), the legacy landscape clamp.
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.RotatedBoardBox(PixelSize.X, PixelSize.Y, _zoom, _mapAngleRad);
        // Symmetric scrollable pad applied in viewport space — a rotated
        // symmetric pad is still symmetric, so we widen the rotated AABB
        // directly instead of feeding the pad through RotatedBoardBox.
        // This lets edge hexes pan clear of the floating HUD chips/buttons
        // that overlay the viewport corners (issue #16). Sized in board-
        // local pixels and scaled by zoom to match the rest of the box.
        float pad = ScrollPaddingPx * _zoom;
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;
        float boxW = maxX - minX;
        float boxH = maxY - minY;
        float x = boxW <= availX
            ? (availX - boxW) * 0.5f - minX
            : Mathf.Clamp(desired.X, availX - maxX, -minX);
        float y = boxH <= availY
            ? _topInset + (availY - boxH) * 0.5f - minY
            : Mathf.Clamp(desired.Y, _topInset + availY - maxY, _topInset - minY);
        return new Vector2(x, y);
    }

    public void CenterOnTerritory(Territory territory)
    {
        if (!territory.HasCapital) return;
        // The capital's pixel position is in unscaled local space; map it to a
        // world offset (zoom + rotation) before subtracting from VisualCenter.
        Vector2 localCenter = FirstHexCenterOffset + HexPixel.ToPixel(territory.Capital!.Value, HexSize);
        Position = ClampPan(VisualCenter() - ToWorldOffset(localCenter, _zoom));
    }

    /// <summary>Frame the camera at <paramref name="zoom"/> with the view
    /// centered <paramref name="contentCenterOffset"/> away from the content
    /// box's center (content/local coords, +x right / +y down). Clamps the
    /// zoom to the allowed range and re-syncs the discrete level index, like
    /// a user gesture would. Callers use this to open a scene with a
    /// hand-tuned framing instead of the RecenterMap fit default (e.g. the
    /// tutorial's landscape camera, #14).</summary>
    public void SetCamera(float zoom, Vector2 contentCenterOffset)
    {
        _zoom = Mathf.Clamp(zoom, _zoomMin, 1f);
        Scale = new Vector2(_zoom, _zoom);
        _zoomLevelIndex = ClosestLevelIndex(_zoom);
        var contentCenter = new Vector2(
            (_contentBox.minX + _contentBox.maxX) * 0.5f,
            (_contentBox.minY + _contentBox.maxY) * 0.5f) + contentCenterOffset;
        Position = ClampPan(VisualCenter() - ToWorldOffset(contentCenter, _zoom));
        LogCameraState("set");
    }

    /// <summary>Center the (possibly rotated) content in the play area. Uses the
    /// content box center (not the nominal grid center) so a level whose tiles
    /// sit off-center in a padded grid still frames centered.</summary>
    private void RecenterMap()
    {
        var contentCenter = new Vector2(
            (_contentBox.minX + _contentBox.maxX) * 0.5f,
            (_contentBox.minY + _contentBox.maxY) * 0.5f);
        Position = ClampPan(VisualCenter() - ToWorldOffset(contentCenter, _zoom));

        // Centering instrumentation (Render:Debug, compile-stripped from
        // release; fires only on recenter — setup / orientation flip / inset
        // change). Logs the inputs and the resulting on-screen content rect so
        // a framing regression is diagnosable without seeing the view: compare
        // the onscreen rect against the play area (vp minus insets).
        (float rMinX, float rMinY, float rMaxX, float rMaxY) =
            MapPlacement.RotatedRectBox(_contentBox.minX, _contentBox.minY,
                _contentBox.maxX, _contentBox.maxY, _zoom, _mapAngleRad);
        (float cMinX, float cMinY, float cMaxX, float cMaxY) =
            MapPlacement.RotatedBoardBox(PixelSize.X, PixelSize.Y, _zoom, _mapAngleRad);
        float clampPad = ScrollPaddingPx * _zoom;
        Log.Debug(Log.LogCategory.Render,
            $"RecenterMap: angle={Mathf.RadToDeg(_mapAngleRad):0}deg zoom={_zoom:0.00} " +
            $"vp={GetViewportRect().Size} insets=({_topInset:0},{_bottomInset:0}) " +
            $"contentBox=({_contentBox.minX:0},{_contentBox.minY:0})-({_contentBox.maxX:0},{_contentBox.maxY:0}) " +
            $"rotatedBox=({rMinX:0},{rMinY:0})-({rMaxX:0},{rMaxY:0}) " +
            $"paddedClamp=({cMinX - clampPad:0},{cMinY - clampPad:0})-({cMaxX + clampPad:0},{cMaxY + clampPad:0}) " +
            $"center={contentCenter} pos={Position} " +
            $"=> onscreen=({Position.X + rMinX:0},{Position.Y + rMinY:0})-({Position.X + rMaxX:0},{Position.Y + rMaxY:0})");
    }

    /// <summary>Viewport-space distance between the two active pinch fingers.
    /// Assumes exactly two entries in <see cref="_touchPoints"/>.</summary>
    private float TwoFingerDistance()
    {
        var fingers = new Vector2[2];
        int i = 0;
        foreach (Vector2 p in _touchPoints.Values) fingers[i++] = p;
        return fingers[0].DistanceTo(fingers[1]);
    }

    /// <summary>Viewport-space midpoint of the two active pinch fingers —
    /// the zoom anchor so the point between the fingers stays put.</summary>
    private Vector2 TwoFingerMidpoint()
    {
        var fingers = new Vector2[2];
        int i = 0;
        foreach (Vector2 p in _touchPoints.Values) fingers[i++] = p;
        return (fingers[0] + fingers[1]) * 0.5f;
    }

    /// <summary>Map an unscaled local board offset to a world-space offset by
    /// applying the current zoom and board rotation. (The inverse direction —
    /// world→local for input — is handled by Godot's <c>ToLocal</c>.)</summary>
    private Vector2 ToWorldOffset(Vector2 localOffset, float zoom) =>
        (localOffset * zoom).Rotated(_mapAngleRad);

    /// <summary>Recompute zoom range and discrete levels for the current
    /// viewport size and snap _zoom into range. Called on _Ready and
    /// whenever the OS window resizes.</summary>
    private void RecomputeZoomLevels()
    {
        Vector2 vp = GetViewportRect().Size;
        // Fit-to-view against the ROTATED extent (width/height swap at ±90°).
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.RotatedBoardBox(PixelSize.X, PixelSize.Y, 1f, _mapAngleRad);
        _zoomMin = ZoomMath.ComputeZoomMin(
            vp.X, vp.Y, _topInset + _bottomInset, maxX - minX, maxY - minY);
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
        ulong frame = Engine.GetProcessFrames();
        ulong t0 = Time.GetTicksMsec();
        Vector2 vp = GetViewportRect().Size;
        Log.Debug(Log.LogCategory.Render,
            $"HexMapView: resize@frame={frame} t={t0}ms vp={vp.X}x{vp.Y}.");

        bool flipped = ResolveRotation();
        RecomputeZoomLevels();
        if (flipped)
        {
            // Orientation changed: re-upright the glyphs and recenter the
            // board (the old pan is meaningless under the new rotation).
            ApplyGlyphUpright();
            RecenterMap();
        }
        else
        {
            Position = ClampPan(Position);
        }

        Log.Debug(Log.LogCategory.Render,
            $"HexMapView: resize settled@frame={Engine.GetProcessFrames()} " +
            $"dt={Time.GetTicksMsec() - t0}ms flipped={flipped} angle={Mathf.RadToDeg(_mapAngleRad):0}°.");
    }

    /// <summary>Resolve board rotation from the viewport aspect (portrait ⇒
    /// −90° CCW, landscape ⇒ 0) and apply it to this node. Returns true if the
    /// angle changed.</summary>
    private bool ResolveRotation()
    {
        Vector2 vp = GetViewportRect().Size;
        ScreenOrientation orientation = ScreenLayout.Resolve(vp.X, vp.Y);
        float angle = orientation == ScreenOrientation.Portrait ? -Mathf.Pi / 2f : 0f;
        if (Mathf.IsEqualApprox(angle, _mapAngleRad)) return false;
        _mapAngleRad = angle;
        Rotation = angle;
        Log.Debug(Log.LogCategory.Render,
            $"HexMapView: map angle → {Mathf.RadToDeg(angle):0}° ({orientation}).");
        return true;
    }

    /// <summary>Keep icon glyphs upright when the board is rotated: counter-
    /// rotate each glyph node by −mapAngle so its net world rotation is 0,
    /// while its position still follows the rotated grid. Tower-placement
    /// previews are tower-shaped (have an "up") so they're included. Hex-cell-
    /// aligned overlays (tile fills, outlines, territory borders, water, shore
    /// foam, tower coverage, selection highlight, the symmetric move-target
    /// rings) are intentionally NOT touched — they rotate with the cells to
    /// stay aligned. The rejection layer is also excluded: its defender arrows
    /// are directional and must rotate with the board to keep pointing. The
    /// move-source selection backdrop lives inside <c>_unitsLayer</c> (so it
    /// draws right beneath the selected unit's rings) but is hex-cell-aligned,
    /// not directional — skip it so its edges keep matching the underlying
    /// tile fill in portrait.</summary>
    private void ApplyGlyphUpright()
    {
        float counter = -_mapAngleRad;
        Node2D?[] glyphLayers =
        {
            _unitsLayer, _capitalsLayer, _treesLayer, _gravesLayer,
            _deathsLayer, _warningBadgesLayer, _towerTargetsLayer,
        };
        int n = 0;
        int skipped = 0;
        foreach (Node2D? layer in glyphLayers)
        {
            if (layer == null) continue;
            foreach (Node child in layer.GetChildren())
            {
                if (child is Node2D node)
                {
                    if (ReferenceEquals(node, _selectionBackdrop))
                    {
                        // Hex-cell-aligned overlay — must rotate with the
                        // board, not counter to it. Leave Rotation at 0 so
                        // the parent's −90° in portrait carries through.
                        ++skipped;
                        continue;
                    }
                    node.Rotation = counter;
                    ++n;
                }
            }
        }
        Log.Debug(Log.LogCategory.Render,
            $"HexMapView: glyph-upright applied to {n} nodes (counter {Mathf.RadToDeg(counter):0}°, skipped {skipped} hex-aligned).");
    }

    /// <summary>Set the HUD-reserved top/bottom insets the map centers within.
    /// Called by the HUD (relayed through the scene root) when orientation
    /// flips or the portrait top bar shows/hides. Re-centers immediately.
    /// Before _Ready / outside the tree we only store — the initial
    /// RecomputeZoomLevels in _Ready will pick the stored values up.</summary>
    public void SetMapInsets(float top, float bottom)
    {
        if (Mathf.IsEqualApprox(top, _topInset) && Mathf.IsEqualApprox(bottom, _bottomInset)) return;
        _topInset = top;
        _bottomInset = bottom;
        Log.Debug(Log.LogCategory.Render, $"HexMapView: insets top={top} bottom={bottom}.");
        if (!IsInsideTree()) return;
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
        Position = ClampPan(anchorVp - ToWorldOffset(localUnderAnchor, newZoom));
        _zoomLevelIndex = ClosestLevelIndex(_zoom);
        LogCameraState("zoom");
    }

    /// <summary>Render-debug snapshot of the camera after a user pan/zoom:
    /// the zoom factor plus the content-space point under the viewport's
    /// visual center — together the spec needed to reproduce a framing as
    /// an initial view (see RecenterMap's contentCenter math).</summary>
    private void LogCameraState(string source)
    {
        Vector2 center = ToLocal(VisualCenter());
        Log.Debug(Log.LogCategory.Render,
            $"HexMapView: camera {source} zoom={_zoom:0.000} " +
            $"center=({center.X:0},{center.Y:0}) pos=({Position.X:0},{Position.Y:0})");
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

    private const float HexOutlineWidth = 1.5f;

    // Per-tile heraldic border: each land tile's full perimeter is its
    // own closed polyline in the owner's player-dark color. Two adjacent
    // land tiles render BOTH perimeters along the shared seam — same
    // owner reads as a single ~1.2px line (anti-aliased same-color
    // overlap), different owners read as two thin lines in each player's
    // dark, the "heraldic field boundary" the redesign calls for.
    // Coastal land/water edges only have the land side, so they're
    // single-line (same as before).
    private void PopulateOutlinesLayer()
    {
        if (_outlinesLayer == null) return;
        Vector2[] verts = HexVertices();

        int tiles = _state.Grid.Count;
        var segments = new List<Vector2>(tiles * 12);
        var colors = new List<Color>(tiles * 6);
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            Color c = PlayerPalette.DarkColorFor(tile.Owner);
            for (int e = 0; e < 6; e++)
            {
                segments.Add(center + verts[e]);
                segments.Add(center + verts[(e + 1) % 6]);
                colors.Add(c);
            }
        }
        _outlinesLayer.SetColored(segments.ToArray(), colors.ToArray(), HexOutlineWidth);
    }

    // Foam strip width as a fraction of HexSize, measured perpendicular
    // to the shore edge into the water hex.
    private const float ShoreFoamInset = 0.30f;
    private static readonly Color ShoreFoamColor = new Color(0.95f, 1.0f, 1.0f);

    private static readonly Color WaterColor = UiPalette.WaterDeep;

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
    private void AddShoreFoamStrips(TriangleSoupBuilder bake, Vector2 center, HexCoord coord)
    {
        Vector2[] verts = HexVertices();
        bool[] isShore = new bool[6];
        int shoreCount = 0;
        for (int edge = 0; edge < 6; edge++)
        {
            int dir = EdgeToNeighborDirection[edge];
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
                EmitFoamStrip(bake, center,
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

            bake.AddPolygon(center, poly, ShoreFoamColor, colors);
        }
    }

    // Bridge polygon for the gap between strips on two water hexes that
    // meet at a protruding land vertex. Drawn as N independent triangles
    // (rather than a single fan polygon) because Polygon2D's auto
    // triangulation of a star-shaped vertex list isn't a fan and would
    // mis-interpolate the per-vertex alpha.
    private void AddCornerFoamDisk(TriangleSoupBuilder bake, Vector2 worldCenter)
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
            bake.AddPolygon(worldCenter, new[] { Vector2.Zero, p0, p1 },
                ShoreFoamColor, new[] { centerColor, rimColor, rimColor });
        }
    }

    private void EmitFoamStrip(TriangleSoupBuilder bake, Vector2 center,
        Vector2 outerA, Vector2 outerB, Vector2 innerB, Vector2 innerA)
    {
        bake.AddPolygon(center, new[] { outerA, outerB, innerB, innerA },
            ShoreFoamColor, new[]
            {
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, 1f),
                new Color(1f, 1f, 1f, 0f),
                new Color(1f, 1f, 1f, 0f),
            });
    }

    private static readonly Color TerritoryBorderColor = new Color(0f, 0f, 0f, 1f);
    private const float TerritoryBorderWidth = 4f;

    private void DrawTerritoryBorders()
    {
        if (_bordersLayer == null) return;
        Vector2[] verts = HexVertices();

        var segments = new List<Vector2>();
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

                segments.Add(center + verts[edge]);
                segments.Add(center + verts[(edge + 1) % 6]);
            }
        }
        _bordersLayer.SetUniform(segments.ToArray(), TerritoryBorderColor, TerritoryBorderWidth);
    }

    private static readonly Color GoldBorderColor = new Color(1f, 0.84f, 0f, 1f);
    // Concentric hex factors (× the tile's circumradius) bounding the gold
    // ring band: the band spans from GoldBorderInner to GoldBorderOuter, so
    // its radial thickness is (Outer − Inner)·radius and it sits just inside
    // the territory border. Drawn as filled quads (one per edge, sharing
    // corner vertices) so the corners miter cleanly — a multiline stroke left
    // gaps at each corner.
    private const float GoldBorderOuter = 0.90f;
    private const float GoldBorderInner = 0.74f;

    /// <summary>
    /// Draw a gold hex-ring band inside every <see cref="HexTile.IsGold"/>
    /// tile. Batched into one TriangleSoup like the static water; runs on the
    /// same static-terrain repaint path as <see cref="DrawTerritoryBorders"/>.
    /// Independent of owner color and occupant, so a gold tile shows its ring
    /// under any player color and alongside a tree / tower / unit / capital.
    /// </summary>
    private void DrawGoldBorders()
    {
        if (_goldBordersLayer == null) return;
        Vector2[] verts = HexVertices();
        var outer = new Vector2[6];
        var inner = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            outer[i] = verts[i] * GoldBorderOuter;
            inner[i] = verts[i] * GoldBorderInner;
        }

        var builder = new TriangleSoupBuilder();
        foreach (HexTile tile in Grid.Tiles)
        {
            if (!tile.IsGold) continue;
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            for (int edge = 0; edge < 6; edge++)
            {
                int next = (edge + 1) % 6;
                // Trapezoid spanning one edge of the ring; adjacent quads
                // share the outer/inner corner vertices → no corner gap.
                var quad = new[] { outer[edge], outer[next], inner[next], inner[edge] };
                builder.AddPolygon(center, quad, GoldBorderColor, vertColors: null);
            }
        }
        _goldBordersLayer.SetTriangles(
            builder.Points.ToArray(), builder.Colors.ToArray(), builder.Indices.ToArray());
    }

    // Batched line drawer: draws ALL edge segments in a single
    // DrawMultiline / DrawMultilineColors call — one draw call instead of
    // one per segment. (Antialiased Line2D / DrawPolyline can't batch, so
    // ~2000 of them were ~2000 draw calls — the device per-capture hitch;
    // see ARCHITECTURE.md "Draw-call batching (Android performance)".)
    // AA is off here so the segments batch; smoothing comes
    // from project-level 2D MSAA. Points are consecutive pairs: each
    // [2i, 2i+1] is one segment.
    private sealed partial class PolylineBatch : Node2D
    {
        private Vector2[] _segments = System.Array.Empty<Vector2>();
        private Color[]? _segmentColors;
        private Color _uniformColor = Colors.Black;
        private float _width = 1f;

        public int StrokeCount => _segments.Length / 2;

        // Borders: every segment the same color.
        public void SetUniform(Vector2[] segments, Color color, float width)
        {
            _segments = segments;
            _segmentColors = null;
            _uniformColor = color;
            _width = width;
            QueueRedraw();
        }

        // Outlines: one color per segment (player-dark per tile).
        public void SetColored(Vector2[] segments, Color[] segmentColors, float width)
        {
            _segments = segments;
            _segmentColors = segmentColors;
            _width = width;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_segments.Length == 0) return;
            if (_segmentColors != null)
            {
                DrawMultilineColors(_segments, _segmentColors, _width);
            }
            else
            {
                DrawMultiline(_segments, _uniformColor, _width);
            }
        }
    }

    // Accumulates many small polygons into one vertex-colored, indexed
    // triangle array. Each source polygon is triangulated (Godot's ear
    // clipper, same as Polygon2D uses) and appended; the result is handed
    // to a single TriangleSoup node = one draw call for the whole batch.
    private sealed class TriangleSoupBuilder
    {
        public readonly List<Vector2> Points = new();
        public readonly List<Color> Colors = new();
        public readonly List<int> Indices = new();

        // local: polygon vertices in node-local space; offset shifts them to
        // world. baseColor is the modulate; vertColors (per vertex, or null
        // for solid) multiplies it — matching Polygon2D.Color × VertexColors.
        public void AddPolygon(Vector2 offset, Vector2[] local, Color baseColor, Color[]? vertColors)
        {
            int b = Points.Count;
            for (int i = 0; i < local.Length; i++)
            {
                Points.Add(offset + local[i]);
                Colors.Add(vertColors == null ? baseColor : baseColor * vertColors[i]);
            }
            int[] tri = Geometry2D.TriangulatePolygon(local);
            if (tri.Length == 0)
            {
                // Triangulation failed (degenerate) — fan fallback.
                for (int i = 1; i + 1 < local.Length; i++)
                {
                    Indices.Add(b);
                    Indices.Add(b + i);
                    Indices.Add(b + i + 1);
                }
            }
            else
            {
                foreach (int t in tri) Indices.Add(b + t);
            }
        }
    }

    // Draws an entire vertex-colored triangle array in a single canvas batch
    // (one draw call) via RenderingServer. Used to bake the static
    // water + shoreline foam (~1,870 Polygon2D ⇒ 1 draw) — see
    // ARCHITECTURE.md "Draw-call batching (Android performance)".
    private sealed partial class TriangleSoup : Node2D
    {
        private int[] _indices = System.Array.Empty<int>();
        private Vector2[] _points = System.Array.Empty<Vector2>();
        private Color[] _colors = System.Array.Empty<Color>();

        public int TriangleCount => _indices.Length / 3;

        public void SetTriangles(Vector2[] points, Color[] colors, int[] indices)
        {
            _points = points;
            _colors = colors;
            _indices = indices;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_indices.Length == 0) return;
            RenderingServer.CanvasItemAddTriangleArray(
                GetCanvasItem(), _indices, _points, _colors);
        }
    }

    // Selection outline drawn in two passes per boundary edge: a wider
    // semi-transparent halo (HexSize * 0.22 wide, white 35% alpha) under
    // a narrower solid warm-white core (HexSize * 0.10 wide). The halo
    // bleeds onto adjacent tiles so the selection reads as glowing; the
    // core stays crisp on the edge itself. Antialiased on both so the
    // outline looks clean on the curved hex corners.
    private const float SelectionHaloWidthFactor = 0.22f;
    private const float SelectionCoreWidthFactor = 0.10f;
    private static readonly Color SelectionHaloColor = new Color(1f, 1f, 1f, 0.35f);
    private static readonly Color SelectionCoreColor = new Color(0.98f, 0.97f, 0.92f, 1f);

    private void RedrawHighlight()
    {
        if (_highlightLayer == null) return;

        ClearLayer(_highlightLayer);

        if (_highlightedTerritory == null) return;

        Vector2[] verts = HexVertices();
        var inside = new HashSet<HexCoord>(_highlightedTerritory.Coords);
        float haloWidth = HexSize * SelectionHaloWidthFactor;
        float coreWidth = HexSize * SelectionCoreWidthFactor;

        foreach (HexCoord coord in _highlightedTerritory.Coords)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);

            for (int edge = 0; edge < 6; edge++)
            {
                int dir = EdgeToNeighborDirection[edge];
                HexCoord neighborCoord = coord.Neighbor(dir);

                if (inside.Contains(neighborCoord)) continue;

                Vector2 a = center + verts[edge];
                Vector2 b = center + verts[(edge + 1) % 6];

                _highlightLayer.AddChild(new Line2D
                {
                    Points = new[] { a, b },
                    Width = haloWidth,
                    DefaultColor = SelectionHaloColor,
                    Antialiased = true,
                    BeginCapMode = Line2D.LineCapMode.Round,
                    EndCapMode = Line2D.LineCapMode.Round,
                });
                _highlightLayer.AddChild(new Line2D
                {
                    Points = new[] { a, b },
                    Width = coreWidth,
                    DefaultColor = SelectionCoreColor,
                    Antialiased = true,
                    BeginCapMode = Line2D.LineCapMode.Round,
                    EndCapMode = Line2D.LineCapMode.Round,
                });
            }
        }
    }

    // Stamp a warning-sign triangle on the capital of every affected
    // territory belonging to the human whose turn it is. Bankruptcy =
    // red triangle / white border + glyph; negative-delta = yellow
    // triangle / black border + glyph. Never drawn during an AI turn —
    // warnings are an in-turn affordance for the human, not a scoreboard.
    private void RedrawWarningBadges()
    {
        if (_warningBadgesLayer == null) return;
        ClearLayer(_warningBadgesLayer);

        Player current = _state.Turns.CurrentPlayer;
        if (current == null || current.IsAi) return;

        foreach (Territory t in _state.Territories)
        {
            if (t.Owner != current.Id) continue;
            if (!t.HasCapital) continue;
            EconomyOutlook outlook = UpkeepRules.Classify(
                t, _state.Grid, _state.Treasury, current.Difficulty);
            if (outlook == EconomyOutlook.Healthy) continue;
            DrawWarningBadgeAt(t.Capital!.Value, outlook);
        }
    }

    private void DrawWarningBadgeAt(HexCoord capital, EconomyOutlook outlook)
    {
        Color fill, accent;
        switch (outlook)
        {
            case EconomyOutlook.BankruptNextTurn: fill = BoardPalette.WarnRed; accent = Colors.White; break;
            case EconomyOutlook.NegativeDelta:    fill = BoardPalette.WarnYellow; accent = Colors.Black; break;
            default: return;
        }

        Vector2 capitalCenter = FirstHexCenterOffset + HexPixel.ToPixel(capital, HexSize);
        // Tuck the badge into the capital's upper-LEFT corner per the
        // redesign spec so the capital glyph stays visible underneath
        // and the warning sits in a consistent corner across tiles. The offset
        // lives in board space (this layer rotates with the board in portrait),
        // so counter-rotate it by -_mapAngleRad to keep it pointing up-left on
        // screen in every orientation. (The badge's glyph is kept upright
        // separately by ApplyGlyphUpright.)
        Vector2 cornerOffset = new Vector2(-HexSize * 0.45f, -HexSize * 0.45f).Rotated(-_mapAngleRad);
        Vector2 badgePos = capitalCenter + cornerOffset;
        Log.Debug(Log.LogCategory.Render,
            $"[WarningBadge] capital={capital} offset={cornerOffset} (mapAngle {Mathf.RadToDeg(_mapAngleRad):0}°)");

        // Equilateral triangle pointing up, inscribed in radius r.
        const float Sqrt3Over2 = 0.8660254f;
        float r = HexSize * 0.45f;
        Vector2 vTop = new Vector2(0f, -r);
        Vector2 vBR  = new Vector2( r * Sqrt3Over2, r * 0.5f);
        Vector2 vBL  = new Vector2(-r * Sqrt3Over2, r * 0.5f);

        var badge = new Node2D { Position = badgePos };
        badge.AddChild(new Polygon2D { Polygon = new[] { vTop, vBR, vBL }, Color = fill });
        // Border: closed Line2D around the triangle (Line2D has no
        // auto-close, so repeat the first vertex).
        badge.AddChild(new Line2D
        {
            Points = new[] { vTop, vBR, vBL, vTop },
            Width = 2f,
            DefaultColor = accent,
        });

        // Exclamation glyph: vertical bar + dot below, both accent color.
        float barHalfWidth = r * 0.11f;
        float barTop = -r * 0.40f;
        float barBottom = r * 0.05f;
        badge.AddChild(new Polygon2D
        {
            Polygon = new[]
            {
                new Vector2(-barHalfWidth, barTop),
                new Vector2( barHalfWidth, barTop),
                new Vector2( barHalfWidth, barBottom),
                new Vector2(-barHalfWidth, barBottom),
            },
            Color = accent,
        });
        Polygon2D dot = CreateFilledDisc(r * 0.11f, accent);
        dot.Position = new Vector2(0f, r * 0.28f);
        badge.AddChild(dot);

        _warningBadgesLayer!.AddChild(badge);
    }
}
