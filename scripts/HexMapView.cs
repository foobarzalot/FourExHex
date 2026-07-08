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
    // The baked water + shoreline-foam soup. Static in normal play, but Rising
    // Tides grows WaterCoords as shores submerge, so the reference
    // is kept to re-bake it in place (preserving z-order) on a structural change.
    private TriangleSoup? _waterFoamBake;
    // Mountain coords as of the last rebuild, for the Rising Tides demote effect:
    // a demoted shore mountain stays in the grid (only loses its
    // mountain flag), so it can't be found by the drowned-tile diff — instead
    // EmitRisingTidesFx diffs this against the current mountain set.
    private readonly HashSet<HexCoord> _lastMountainCoords = new();
    // Mountain inner borders: a black thickened hex-ring channel
    // mirroring the gold one, drawn as a batched TriangleSoup. Gold and mountain
    // are mutually exclusive so the two rings never overlap. Sits in the same
    // z-band as the gold channel.
    private TriangleSoup? _mountainBordersLayer;
    private Node2D? _capitalsLayer;
    private Node2D? _rejectionsLayer;
    private Node2D? _treesLayer;
    private Node2D? _gravesLayer;
    private Node2D? _unitsLayer;
    private Node2D? _seaVikingsLayer;
    private Node2D? _deathsLayer;
    private Node2D? _tideForecastLayer;
    private Node2D? _targetsLayer;
    private Node2D? _towerTargetsLayer;
    private Node2D? _highlightLayer;
    private Node2D? _focusPulseLayer;
    private Node2D? _warningBadgesLayer;
    // Fog Of War: the cover/dim overlay above everything. Empty outside Fog Of
    // War. Baked as one triangle soup (cover + dim hexes) so a full-map repaint
    // on a visibility change is a single draw call, not hundreds of Polygon2D
    // nodes. Stale tiles show only static terrain greyed beneath the dim — no
    // occupant glyphs — so there is no separate stale-occupant layer.
    private TriangleSoup? _fogLayer;
    private readonly Dictionary<HexCoord, Node2D> _unitVisuals = new();
    private readonly Dictionary<HexCoord, Node2D> _capitalVisuals = new();

    // Tree and grave visuals persist across RefreshOccupantVisuals calls
    // (units, capitals, towers all rebuild every refresh; trees and
    // graves don't depend on per-refresh state, so we keep their nodes
    // alive). This also means a grow-in tween started on a freshly-
    // planted tree or grave is not interrupted by a subsequent refresh.
    private readonly Dictionary<HexCoord, Node2D> _treeVisuals = new();
    private readonly Dictionary<HexCoord, Node2D> _graveVisuals = new();

    // Per-tile fill polygon, owned by the view. Resynced from _state in
    // RebuildAfterTerritoryChange.
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

    // The translucent darkening hex drawn behind the selected unit's
    // rings. Lives in _unitsLayer at the selected unit's center. Built
    // by ApplySelectionAffordance, freed by ClearSelectionAffordance.
    private Node2D? _selectionBackdrop;

    // The player whose turn was active at the most recent
    // RefreshOccupantVisuals call. Cached so IsActionableUnit can
    // answer "is this coord still actionable?" between refreshes (used
    // by ShowMoveSource when restoring the pulse on a deselected
    // unit). Null while no turn is active (between games / pre-Init).
    private PlayerId? _currentPlayer;

    // Selection backdrop: a translucent black tile-sized hex drawn
    // beneath the selected unit's rings. Darkens the tile enough that
    // the white rings still read, while letting the territory fill and
    // any co-located capital/tower/tree/grave show through.
    // Tune ~0.45-0.65 for ring contrast vs. feature visibility.
    private static readonly Color SelectionBackdropColor = new Color(0f, 0f, 0f, 0.42f);

    // Tutorial "tap this unit to pick it up" cue. A flashing
    // CTA-style highlight on the source unit's own tile — a white hex with
    // a black border whose alpha pulses, mirroring the HUD's tutorial-CTA
    // button flash (see HudView.StartCtaPulse). Deliberately distinct from
    // the green ShowMoveTargets rings, which mean "move TO here." Driven by
    // the controller via ShowSelectUnitCue; null in ordinary play.
    private HexCoord? _selectCueUnit;
    private Node2D? _selectCueNode;
    private Tween? _selectCueTween;
    private static readonly Color SelectCueFillColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color SelectCueBorderColor = new Color(0f, 0f, 0f, 1f);
    private const float SelectCueBorderWidth = 3f;
    // CTA-style flash, but only the white FILL pulses (the black border stays
    // steady so the tile frame always reads). The fill peaks translucent —
    // never opaque — so the actionable unit's white rings stay visible at the
    // top of the pulse instead of washing out. Sine, 0.55 s/leg
    // to match HudView's CTA cadence.
    private const float SelectCueFillMinAlpha = 0.18f;
    private const float SelectCueFillMaxAlpha = 0.55f;
    private const float SelectCuePulseHalfPeriod = 0.55f;

    // Terrain-intro focus pulse (issue #53): a standalone pulsing hex overlay
    // that draws the eye to the gold/mountain tile the camera pans to during
    // its first-encounter hint. Independent of the unit select cue above — no
    // unit bookkeeping — living on its own layer and torn down when the player
    // taps the hint away. White fill + yellow border, pulsing scale + fill
    // alpha on the same sine cadence as the select cue.
    private Node2D? _focusPulseNode;
    private Tween? _focusPulseTween;
    private static readonly Color FocusPulseFillColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color FocusPulseBorderColor = new Color(1f, 0.85f, 0.2f, 1f);
    private const float FocusPulseBorderWidth = 4f;
    private const float FocusPulseFillMinAlpha = 0.15f;
    private const float FocusPulseFillMaxAlpha = 0.55f;
    private const float FocusPulseMaxScale = 1.15f;

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

    // Camera-pan animation (CenterOnTerritory). Instead of snapping Position
    // to the new anchor, ease from _panFrom to _panTo over PanAnimDurationSec
    // via EasingMath.SmoothStep, advanced each frame in _Process. Any manual
    // pan/zoom gesture calls StopPan() so input is never fought.
    private const float PanAnimDurationSec = 0.22f;
    // Below this, a re-center is imperceptible — snap instead of animating.
    private const float PanSnapEpsilonPx = 1f;
    private bool _panActive;
    private Vector2 _panFrom;
    private Vector2 _panTo;
    private double _panElapsed;

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
    // _zoomMin sits ZoomOutGrace below the exact whole-map fit (_zoomFit)
    // so max zoom-out leaves margin around the board; _zoomFit is kept for
    // callers that need the exact fit (FrameWholeGrid's menu thumbnail).
    private const int ZoomLevelCount = 5;
    private const float ZoomOutGrace = 1.2f;
    private float _zoom = 1f;
    private float _zoomMin = 1f;
    private float _zoomFit = 1f;
    private float[] _zoomLevels = new[] { 1f, 1f, 1f, 1f, 1f };
    private int _zoomLevelIndex = ZoomLevelCount - 1;

    // HUD-reserved insets the map must avoid when centering/clamping. The
    // HUD owns layout policy and pushes these via SetMapInsets; defaults
    // reproduce the legacy single-top-strip landscape behavior so nothing
    // changes until told otherwise (and so the headless view stays correct).
    private float _topInset = HudView.HudHeight;
    private float _bottomInset = 0f;

    // Board-local pixel pad (pre-zoom) around the nominal grid that the
    // player can scroll into. Sized to comfortably exceed worst-case D1
    // floating-HUD occlusion (portrait bottom bar 200 + tutorial 60 = 260;
    // portrait stacked top chips ~148; landscape rails 78) so an edge hex can
    // always be panned well clear of the chips/buttons that float over it,
    // with open water to spare on device. Symmetric on all four sides —
    // rotation flips axes so a single value covers both orientations. Drives
    // both the water rim render and the ClampPan extent.
    private const float ScrollPaddingPx = 600f;

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
        // its centered value.
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
        // The tide-telegraph layer is freed with every other child above; drop the
        // diff cache so the next ShowTideForecast redraws onto the fresh layer.
        _shownTideForecast.Clear();
        // Same for the sea-viking layer's diff caches — and suppress the
        // spawn entrance for the next ShowSeaVikings pass, which re-adds
        // every EXISTING raider onto the fresh layer.
        _seaVikingVisuals.Clear();
        _seaGraveVisuals.Clear();
        _animateSeaSpawns = false;
        _tileVisuals.Clear();
        _pulsingUnits.Clear();
        _pulsingCapitals.Clear();
        _highlightedTerritory = null;
        _selectedUnit = null;
        // Full rebuild frees every child (incl. the select-unit cue node and
        // its tween); reset the cue state so it doesn't dangle.
        _selectCueUnit = null;
        _selectCueNode = null;
        _selectCueTween = null;
        // Same for the terrain-intro focus pulse (its own layer's children are
        // freed by the rebuild too).
        _focusPulseNode = null;
        _focusPulseTween = null;

        // Water cells + shoreline foam are STATIC (never change after init).
        // As individual Polygon2D they were ~1,870 separate canvas items =
        // ~1,870 draw calls every frame in the gl_compatibility renderer —
        // the dominant cost behind the device per-capture hitch (see
        // ARCHITECTURE.md "Draw-call batching (Android performance)").
        // Bake all of it into ONE vertex-colored triangle soup =
        // one draw call. Order matters within the soup: water first (behind),
        // foam after (on top). The whole bake sits behind the land tile
        // fills added below, matching the old child z-order.
        TriangleSoupBuilder bake = BuildWaterFoamSoup();
        _waterFoamBake = new TriangleSoup { Name = "WaterFoamBake" };
        AddChild(_waterFoamBake);
        _waterFoamBake.SetTriangles(bake.Points.ToArray(), bake.Colors.ToArray(), bake.Indices.ToArray());

        // Tiles already exist in _state.Grid (populated by the controller
        // before AddChild). Create one Polygon2D fill per tile, owned by
        // the view in _tileVisuals. Recolors are NOT pushed by a model
        // setter — RebuildAfterTerritoryChange resyncs fills from _state
        // (the coalesced repaint path), so per-action model mutations
        // during an instant fast-forward don't leak to the screen.
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            Polygon2D fill = CreateHexVisual(center, PlayerPalette.ColorFor(EffectiveOwner(tile)));
            _tileVisuals[tile.Coord] = fill;
            AddChild(fill);
        }

        // Rising Tides: baseline the mountain set so the first
        // demotion after a build/load is detected by EmitRisingTidesFx.
        _lastMountainCoords.Clear();
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            if (tile.IsMountain) _lastMountainCoords.Add(tile.Coord);
        }

        // Gold-tile inner borders: drawn just above the tile fills
        // but BELOW the per-tile outlines and territory borders, so both the
        // thin cell-to-cell outline and the thick boundary lines draw on top of
        // the gold accent. The gold ring reaches the tile edge, so if it sat
        // above the outlines it would erase the thin shared outline between two
        // adjacent gold tiles. A filled hex-ring band (TriangleSoup) rather than
        // a multiline stroke so the corners miter cleanly with no gaps.
        _goldBordersLayer = new TriangleSoup { Name = "GoldBordersLayer" };
        AddChild(_goldBordersLayer);

        // Mountain-tile inner borders: the same hex-ring channel as
        // gold but black and a touch thicker, in the same z-band (above fills,
        // below outlines/borders). Gold and mountain are mutually exclusive, so
        // the two channels never share a tile.
        _mountainBordersLayer = new TriangleSoup { Name = "MountainBordersLayer" };
        AddChild(_mountainBordersLayer);

        // All per-tile outlines go in one layer drawn after every fill,
        // so neighbor fills can never overdraw an outline. Each unique
        // edge is drawn exactly once for uniform thickness.
        // Layer order on each land tile: fill → border (1.5px in this
        // tile's player-dark, full perimeter). The border sits on top
        // so seams between two players show both dk colors side-by-
        // side without one overdrawing the other.
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
        _capitalsLayer = new Node2D { Name = "CapitalsLayer" };
        AddChild(_capitalsLayer);
        _treesLayer = new Node2D { Name = "TreesLayer" };
        AddChild(_treesLayer);
        _gravesLayer = new Node2D { Name = "GravesLayer" };
        AddChild(_gravesLayer);
        _unitsLayer = new Node2D { Name = "UnitsLayer" };
        AddChild(_unitsLayer);
        // Viking Raiders: raiders waiting at sea — same z-band as land
        // units (the two never overlap; sea glyphs sit on water coords).
        _seaVikingsLayer = new Node2D { Name = "SeaVikingsLayer" };
        AddChild(_seaVikingsLayer);
        _deathsLayer = new Node2D { Name = "DeathsLayer" };
        AddChild(_deathsLayer);
        // Rising Tides telegraph: drawn above units/deaths so the
        // "this tile will sink" cue tints the doomed tile and its occupant, but
        // below targets so move-target rings still read on top.
        _tideForecastLayer = new Node2D { Name = "TideForecastLayer" };
        AddChild(_tideForecastLayer);
        _targetsLayer = new Node2D { Name = "TargetsLayer" };
        AddChild(_targetsLayer);
        _towerTargetsLayer = new Node2D { Name = "TowerTargetsLayer" };
        AddChild(_towerTargetsLayer);
        _highlightLayer = new Node2D { Name = "HighlightLayer" };
        AddChild(_highlightLayer);
        // Terrain-intro focus pulse: its own layer above the highlight so the
        // first-encounter "look here" cue is never wiped by a territory-
        // highlight redraw (issue #53).
        _focusPulseLayer = new Node2D { Name = "FocusPulseLayer" };
        AddChild(_focusPulseLayer);
        // Added last so badges draw on top of every other map layer
        // (including highlight, units, capitals).
        _warningBadgesLayer = new Node2D { Name = "WarningBadgesLayer" };
        AddChild(_warningBadgesLayer);
        // Fog Of War: remembered occupants on stale tiles, then the cover/dim
        // overlay above everything map-related so never-seen tiles are fully
        // hidden and stale tiles read as dimmed. Both empty outside Fog Of War.
        _fogLayer = new TriangleSoup { Name = "FogLayer" };
        AddChild(_fogLayer);
        // Rejection overlays sit on top of everything so a red flash is
        // unambiguous. Persistent — never cleared by RefreshOccupantVisuals
        // — so an in-flight tween doesn't get QueueFree'd mid-pulse.
        _rejectionsLayer = new Node2D { Name = "RejectionsLayer" };
        AddChild(_rejectionsLayer);

        DrawTerritoryBorders();
        DrawGoldBorders();
        DrawMountains();
        RedrawFogOverlay();
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
    /// Bake the water cells + shoreline foam (plus the render-only rim ring)
    /// into one vertex-colored <see cref="TriangleSoupBuilder"/> — see the long
    /// comment in <see cref="BuildStateVisuals"/> for why this is a single draw
    /// call. Reads only <c>_state.WaterCoords</c> and <c>_state.Grid</c>, so it
    /// re-derives correctly after Rising Tides grows the water set.
    /// </summary>
    private TriangleSoupBuilder BuildWaterFoamSoup()
    {
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
                if (HitTestMath.InOffsetBounds(col, row, Cols, Rows)) continue;
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
                if (HitTestMath.InOffsetBounds(col, row, Cols, Rows)) continue;
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
        return bake;
    }

    /// <summary>
    /// Rising Tides: reconcile the land-fill <see cref="Polygon2D"/>s with the
    /// current grid and re-bake the water/foam soup in place so water/land draw
    /// correctly after the grid changes size. Two directions:
    ///   - <b>Shrank</b> (a tile submerged — its coord left the grid and entered
    ///     <c>_state.WaterCoords</c>): drop the stale land fill.
    ///   - <b>Grew</b> (tiles restored on a replay reset / undo past a tide — back
    ///     in the grid with their water cleared): recreate the missing land fill,
    ///     inserting it into the fill z-band (just under the gold-border layer) so
    ///     it draws below the outlines/borders rather than on top. Without this a
    ///     restored coord keeps the baked water hex under a freshly-stroked border
    ///     — the "water tile with black borders" glitch.
    /// Either way the water soup is re-baked from the (now correct)
    /// <c>_state.WaterCoords</c>. A no-op when the counts already match, so normal
    /// capture repaints (which never change grid size) pay one count comparison.
    /// </summary>
    private void SyncTileFillsToGridAndRebakeWater()
    {
        if (_tileVisuals.Count == _state.Grid.Count) return;

        if (_tileVisuals.Count > _state.Grid.Count)
        {
            var drowned = new List<HexCoord>();
            foreach (KeyValuePair<HexCoord, Polygon2D> kv in _tileVisuals)
            {
                if (!_state.Grid.Contains(kv.Key)) drowned.Add(kv.Key);
            }
            foreach (HexCoord coord in drowned)
            {
                _tileVisuals[coord]?.QueueFree();
                _tileVisuals.Remove(coord);
            }
            TriangleSoupBuilder bake = BuildWaterFoamSoup();
            _waterFoamBake?.SetTriangles(
                bake.Points.ToArray(), bake.Colors.ToArray(), bake.Indices.ToArray());
            Log.Debug(Log.LogCategory.Tide,
                $"[tide-view] pruned {drowned.Count} drowned land fill(s); " +
                $"rebaked water ({_state.WaterCoords.Count} coords)");
            return;
        }

        // Grew: recreate fills for restored coords. AddChild appends to the end
        // (top of the z-order), so MoveChild each new fill down to the gold-border
        // layer's slot — the boundary between the fill band and every overlay —
        // keeping it behind the outlines, borders, units, etc.
        int restored = 0;
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            if (_tileVisuals.ContainsKey(tile.Coord)) continue;
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            Polygon2D fill = CreateHexVisual(center, PlayerPalette.ColorFor(EffectiveOwner(tile)));
            _tileVisuals[tile.Coord] = fill;
            AddChild(fill);
            if (_goldBordersLayer != null) MoveChild(fill, _goldBordersLayer.GetIndex());
            restored++;
        }
        TriangleSoupBuilder regrow = BuildWaterFoamSoup();
        _waterFoamBake?.SetTriangles(
            regrow.Points.ToArray(), regrow.Colors.ToArray(), regrow.Indices.ToArray());
        Log.Debug(Log.LogCategory.Tide,
            $"[tide-view] restored {restored} land fill(s); " +
            $"rebaked water ({_state.WaterCoords.Count} coords)");
    }

    // Rising Tides FX captured at the top of a rebuild but spawned
    // only at the very end — RebuildAfterTerritoryChange does ClearLayer(
    // _deathsLayer) mid-method to cancel stale death animations, which would
    // wipe an effect spawned earlier in the same call. So capture the coords
    // (and the submerged tiles' fill colors, before the prune frees them) up
    // front, then flush the spawns after that clear (the same lifecycle slot
    // the controller's PlayDestructionEffect uses).
    private readonly List<(HexCoord Coord, Color LandColor)> _pendingSubmergeFx = new();
    private readonly List<HexCoord> _pendingDemoteFx = new();

    /// <summary>
    /// Detect what submerged / demoted since the last rebuild and stash it for
    /// <see cref="FlushRisingTidesFx"/>. Submerged tiles are those in
    /// <see cref="_tileVisuals"/> no longer in the grid (capture their fill color
    /// now — the prune frees them next); demoted mountains were mountains last
    /// rebuild, still in the grid, no longer flagged. <see cref="_lastMountainCoords"/>
    /// is refreshed every call (even when silent) so a later rebuild can't
    /// re-fire a stale demote. When silent (Instant AI / instant replay) nothing
    /// is stashed — the effect is suppressed.
    /// </summary>
    private void CaptureRisingTidesFx()
    {
        _pendingSubmergeFx.Clear();
        _pendingDemoteFx.Clear();

        var submerged = new List<HexCoord>();
        foreach (KeyValuePair<HexCoord, Polygon2D> kv in _tileVisuals)
        {
            if (!_state.Grid.Contains(kv.Key)) submerged.Add(kv.Key);
        }

        var currentMountains = new HashSet<HexCoord>();
        foreach (HexTile t in _state.Grid.Tiles)
        {
            if (t.IsMountain) currentMountains.Add(t.Coord);
        }
        var demoted = new List<HexCoord>();
        foreach (HexCoord c in _lastMountainCoords)
        {
            if (!currentMountains.Contains(c) && _state.Grid.Contains(c)) demoted.Add(c);
        }
        _lastMountainCoords.Clear();
        _lastMountainCoords.UnionWith(currentMountains);

        if (submerged.Count == 0 && demoted.Count == 0) return;

        Log.Debug(Log.LogCategory.Tide,
            $"[tide-fx] submerged={submerged.Count} demoted={demoted.Count} " +
            $"silent={_silentMode} vfx={UserSettings.VfxEnabled} sfx={UserSettings.SfxEnabled}");

        // Instant AI / instant replay: suppress entirely (don't stash).
        if (_silentMode) return;

        foreach (HexCoord coord in submerged)
        {
            Color landColor =
                _tileVisuals.TryGetValue(coord, out Polygon2D? fill) && fill != null
                    ? fill.Color
                    : WaterColor;
            _pendingSubmergeFx.Add((coord, landColor));
        }
        _pendingDemoteFx.AddRange(demoted);
    }

    /// <summary>
    /// Spawn the FX stashed by <see cref="CaptureRisingTidesFx"/>. Called at the
    /// end of the rebuild, after <c>ClearLayer(_deathsLayer)</c>, so the fresh
    /// nodes survive. One SFX per event type (not per tile) so a future
    /// multi-tile budget doesn't smear the sound.
    /// </summary>
    private void FlushRisingTidesFx()
    {
        if (_pendingSubmergeFx.Count == 0 && _pendingDemoteFx.Count == 0) return;

        foreach ((HexCoord coord, Color landColor) in _pendingSubmergeFx)
        {
            PlaySubmergeEffect(coord, landColor);
        }
        if (_pendingSubmergeFx.Count > 0) PlaySound(SoundEffect.TileSubmerged);

        foreach (HexCoord coord in _pendingDemoteFx)
        {
            PlayMountainDemoteEffect(coord);
        }
        if (_pendingDemoteFx.Count > 0) PlaySound(SoundEffect.TowerDestroyed);

        _pendingSubmergeFx.Clear();
        _pendingDemoteFx.Clear();
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
        // Rising Tides: detect submerge / mountain-demote FIRST, while the drowned
        // tiles' fills are still present (the prune frees them next) and before
        // DrawMountains repaints — but defer SPAWNING the effects to the end (see
        // FlushRisingTidesFx), past the ClearLayer(_deathsLayer) below.
        CaptureRisingTidesFx();
        // Rising Tides: reconcile land fills with the grid (drop drowned tiles,
        // or recreate restored ones on a replay/undo reset) and re-bake water
        // before the fill/border resync below re-reads the grid.
        SyncTileFillsToGridAndRebakeWater();

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
                fill.Color = PlayerPalette.ColorFor(EffectiveOwner(tile));
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
        DrawMountains();
        RedrawFogOverlay();
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

        // Rising Tides: spawn the submerge / demote FX now, after the deaths-layer
        // clear, so they aren't wiped (they live on _deathsLayer too).
        FlushRisingTidesFx();
    }

    /// <summary>
    /// Draw a bright perimeter around <paramref name="selected"/>, or
    /// clear the highlight if null. The view does not own selection
    /// state — the controller calls this on every change.
    /// </summary>
    public void ShowHighlight(Territory? selected)
    {
        // Skip the redraw when the incoming region is visually identical to
        // the one already shown (same owner + exact coord set — a fresh
        // Territory object from a post-capture Recompute arrives every AI
        // preview beat). Redrawing tears down and rebuilds the outline,
        // which overlaps old and new strokes for a frame and strobes on
        // sustained beat sequences (the Viking Raiders phase runs dozens of
        // consecutive beats on one growing neutral territory).
        bool sameRegion = SameHighlightRegion(_highlightedTerritory, selected);
        if (Log.IsEnabled(Log.LogCategory.Render, Log.LogLevel.Debug))
        {
            static HexCoord MinCoord(Territory t)
            {
                HexCoord min = default;
                bool first = true;
                foreach (HexCoord c in t.Coords)
                {
                    if (first || c.CompareTo(min) < 0) { min = c; first = false; }
                }
                return min;
            }
            Log.Debug(Log.LogCategory.Render,
                selected == null
                    ? $"[highlight] cleared (skip={sameRegion})"
                    : $"[highlight] owner={selected.Owner} size={selected.Size} " +
                      $"min={MinCoord(selected)} skip={sameRegion}");
        }
        // Keep the newest Territory object even when skipping — later
        // comparisons must run against the current partition's instance.
        _highlightedTerritory = selected;
        if (sameRegion) return;
        RedrawHighlight();
    }

    /// <summary>True iff the two highlight targets draw the same outline:
    /// both null, or same owner and exactly the same coord set.</summary>
    private static bool SameHighlightRegion(Territory? shown, Territory? incoming)
    {
        if (shown == null || incoming == null) return shown == null && incoming == null;
        if (shown.Owner != incoming.Owner || shown.Size != incoming.Size) return false;
        var shownCoords = new HashSet<HexCoord>(shown.Coords);
        foreach (HexCoord c in incoming.Coords)
        {
            if (!shownCoords.Contains(c)) return false;
        }
        return true;
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

    // Rising Tides telegraph palette/cadence. The cue ALTERNATES the
    // before/after shoreline: a single group cross-fades by alpha (min↔max). For a
    // SUBMERGING tile the group is the real water color plus, per edge, a cover
    // quad over the OLD coastal foam (sea-facing edges) and a NEW white foam strip
    // (land-facing edges). At trough (invisible) the real land + its baked coast
    // show ("before"); at peak (full water) the tile is open sea with the new coast
    // drawn ("after"). A DEMOTE-ONLY shore mountain (won't sink) instead fades its
    // mountain RING toward the tile's land color — the ring's alpha appears to
    // animate down to "demoted to lowland" and back, in sync.
    private const float TideForecastSubmergeMinAlpha = 0.0f;  // land + its coast show -> "before"
    private const float TideForecastSubmergeMaxAlpha = 1.0f;  // tile == open sea -> "after"
    private const float TideForecastPulseHalfPeriod = 1.2f;   // 2.4s full period (slowed 50%)
    // What the tide telegraph is currently drawing, so ShowTideForecast can diff
    // and leave the running pulse tweens alone unless the forecast set changes
    // (RefreshViews fires far more often on AI turns; rebuilding every call reset
    // the cadence). Cleared by BuildStateVisuals when the layers are recreated.
    private readonly List<TideStep> _shownTideForecast = new();

    /// <summary>
    /// Rising Tides: telegraph the given steps as tiles eroding at the
    /// END of the current player's turn. A submerging tile cross-fades between its
    /// land look (before) and the open-sea look with the new coastline (after); a
    /// demote-only shore mountain fades its ring toward lowland and back. Pass an
    /// empty sequence to clear.
    ///
    /// Two refresh-frequency guards: the whole cue is SUPPRESSED at instant speed
    /// (<see cref="_silentMode"/> — Instant AI batch / instant replay), and
    /// otherwise this only rebuilds when the forecast set actually CHANGES, leaving
    /// the running pulse tweens alone across the frequent RefreshViews calls of a
    /// turn (rebuilding every call restarted the cadence — fast on AI turns).
    /// </summary>
    public void ShowTideForecast(IEnumerable<TideStep> steps)
    {
        if (_tideForecastLayer == null) return;

        // Suppress entirely at instant speed; otherwise the desired set IS the
        // forecast. (Empty when suppressed, so the diff below clears any cue.)
        var desired = new List<TideStep>();
        if (!_silentMode)
        {
            foreach (TideStep step in steps) desired.Add(step);
        }

        if (TideForecastsEqual(_shownTideForecast, desired)) return; // unchanged: keep tweens
        _shownTideForecast.Clear();
        _shownTideForecast.AddRange(desired);

        ClearLayer(_tideForecastLayer);
        foreach (TideStep step in desired)
        {
            if (step.DemoteOnly)
            {
                // Shore mountain: erodes to lowland but stays land — fade its
                // mountain ring toward the tile's land color and back, in sync.
                DrawMountainErosionTelegraph(step.Coord);
            }
            else
            {
                // Submerge: cross-fade the tile to the real water color, hide the
                // OLD coastal foam, and fade in the NEW shoreline that forms once
                // the tile is sea — so it alternates between before and after.
                DrawTideTelegraphTile(step.Coord, WaterColor,
                    TideForecastSubmergeMinAlpha, TideForecastSubmergeMaxAlpha);
            }
        }
    }

    // Fog Of War: the current projection from the single human's perspective,
    // or null when fog is off. Set by ShowFog; consulted by the render paths to
    // paint stale tiles from memory, hide never-seen tiles, and dim the rest.
    private FogView? _fog;
    // Never-seen tiles are covered with a cool dark "mist" (a touch cooler/darker
    // than the warm BgDeep canvas, so unseen area reads as atmosphere, not a hole);
    // kept opaque so terrain stays hidden. Stale (explored, out of sight) tiles get
    // a translucent cool blue-grey "memory" wash over their greyed terrain — a
    // legible, distant register clearly between vivid-live and dark-fog.
    private static readonly Color FogCoverColor = new Color(0.09f, 0.11f, 0.15f, 1f);
    private static readonly Color FogStaleDimColor = new Color(0.16f, 0.20f, 0.30f, 0.45f);
    // Per-vertex alpha multiplier used to feather tier frontiers (see
    // RedrawFogOverlay): both stale and fog fade toward transparent at vertices
    // touching a more-revealed neighbour, so visible→stale and visible→fog read
    // as a soft ~1-hex gradient instead of a hard hexagon. (The cost is a faint
    // terrain hint in that one-hex band — terrain only, never ownership, which is
    // already None on non-visible tiles.)
    private const float FogFeatherEdge = 0f;

    /// <summary>
    /// Fog Of War: store the human's visibility projection (null = fog off). The
    /// fill/border/overlay repaint runs only when the visible set changes (a
    /// territory change or the first push after a build) — the common
    /// every-refresh call where the set is unchanged is free. Occupants are
    /// repainted right after by the controller's RefreshViews, now seeing the
    /// current projection.
    /// </summary>
    public void ShowFog(FogView? fog)
    {
        bool visibilityChanged = !FogVisibleEqual(_fog, fog);
        _fog = fog;
        if (visibilityChanged) RepaintFogVisuals();
    }

    private static bool FogVisibleEqual(FogView? a, FogView? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.Visible.Count == b.Visible.Count && a.Visible.SetEquals(b.Visible);
    }

    // Per-tile fog classification. With fog off (_fog == null) every tile is
    // Visible, so all render paths behave exactly as before.
    private VisibilityTier FogTierOf(HexCoord coord) =>
        _fog == null ? VisibilityTier.Visible
                     : VisibilityRules.TierOf(coord, _fog.Visible, _state);

    // The owner a tile is PAINTED as: live only when in current sight; otherwise
    // neutral (None) — stale tiles read as grey terrain with no ownership, and
    // fogged tiles are hidden by the cover anyway. Identity outside Fog Of War.
    private PlayerId EffectiveOwner(HexTile tile)
    {
        if (_fog == null) return tile.Owner;
        return _fog.Visible.Contains(tile.Coord) ? tile.Owner : PlayerId.None;
    }

    // Resync fills, outlines, borders, decorations, and the fog overlay to the
    // current projection — the visual half of a visibility change, without the
    // destructive tree/grave teardown of RebuildAfterTerritoryChange.
    private void RepaintFogVisuals()
    {
        if (_tileVisuals.Count == 0) return; // not built yet
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            if (_tileVisuals.TryGetValue(tile.Coord, out Polygon2D? fill) && fill != null)
                fill.Color = PlayerPalette.ColorFor(EffectiveOwner(tile));
        }
        PopulateOutlinesLayer();
        DrawTerritoryBorders();
        DrawGoldBorders();
        DrawMountains();
        RedrawFogOverlay();
    }

    // Bake the cover (opaque, over never-seen cells) + dim (translucent, over
    // stale cells) overlay across the WHOLE map extent — land, water, and the
    // off-map rim — so unseen ocean and coastlines are hidden just like land.
    // Empty (single empty soup) outside Fog Of War.
    private void RedrawFogOverlay()
    {
        if (_fogLayer == null) return;
        if (_fog == null)
        {
            _fogLayer.SetTriangles(
                System.Array.Empty<Vector2>(),
                System.Array.Empty<Color>(),
                System.Array.Empty<int>());
            return;
        }

        Vector2[] hex = HexVertices();
        var bake = new TriangleSoupBuilder();
        var perimeter = new Color[6];
        // Same extent the water/foam bake covers (board + scroll-pad rim), so
        // every cell the player could pan to is fogged until seen.
        int margin = Mathf.CeilToInt(ScrollPaddingPx / (1.5f * HexSize)) + 1;
        for (int row = -margin; row < Rows + margin; row++)
        {
            for (int col = -margin; col < Cols + margin; col++)
            {
                HexCoord coord = HexCoord.FromOffset(col, row);
                VisibilityTier tier = FogTierOf(coord);
                if (tier == VisibilityTier.Visible) continue;
                Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
                Color tint = tier == VisibilityTier.Fog ? FogCoverColor : FogStaleDimColor;
                FillFeatherPerimeter(coord, tier, tint, perimeter);
                // Center stays at full tint; perimeter fades at frontier vertices.
                // Fan triangulation (shared center) keeps the gradient spike-free.
                bake.AddFan(center, hex, tint, perimeter);
            }
        }
        _fogLayer.SetTriangles(
            bake.Points.ToArray(), bake.Colors.ToArray(), bake.Indices.ToArray());
    }

    // Compute the 6 perimeter colours for a fog/stale hex, fading the tint's alpha
    // at vertices on a frontier with a more-revealed neighbour so the tier softens
    // over ~1 hex. Each vertex v is shared by the two edges meeting there, whose
    // neighbours are EdgeToNeighborDirection[(v+5)%6] and [v]; a vertex touching a
    // more-revealed neighbour drops to FogFeatherEdge. (Spike-free because the
    // hex is drawn as a fan from a full-tint center — see TriangleSoupBuilder.AddFan.)
    private void FillFeatherPerimeter(HexCoord coord, VisibilityTier tier, Color tint, Color[] into)
    {
        int self = TierReveal(tier);
        for (int v = 0; v < 6; v++)
        {
            int nbrA = TierReveal(FogTierOf(coord.Neighbor(EdgeToNeighborDirection[(v + 5) % 6])));
            int nbrB = TierReveal(FogTierOf(coord.Neighbor(EdgeToNeighborDirection[v])));
            int maxNbr = Mathf.Max(nbrA, nbrB);
            float aMult = maxNbr > self ? FogFeatherEdge : 1f;
            into[v] = new Color(tint.R, tint.G, tint.B, tint.A * aMult);
        }
    }

    private static int TierReveal(VisibilityTier tier) => (int)tier; // Fog=0 < Stale=1 < Visible=2

    private static bool TideForecastsEqual(List<TideStep> a, List<TideStep> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i])) return false; // TideStep is a value record struct
        }
        return true;
    }

    /// <summary>
    /// Draw a submerging tide-telegraph tile on <see cref="_tideForecastLayer"/>
    /// as a single alpha-pulsing "reveal" group (min↔max alpha, looping). The
    /// group is the water-fill hex plus, per edge: a cover quad over the OLD foam
    /// on each sea-facing edge (so the current coastline vanishes at peak) and a
    /// NEW foam strip on each land-facing edge (the coastline that forms once the
    /// tile is sea). At trough the group is invisible — the real land + its baked
    /// coast show ("before"); at peak the tile is open water with the new coast
    /// ("after"). (Demote-only shore mountains use
    /// <see cref="DrawMountainErosionTelegraph"/> instead.)
    /// </summary>
    private void DrawTideTelegraphTile(
        HexCoord coord, Color overlayColor, float minAlpha, float maxAlpha)
    {
        if (_tideForecastLayer == null) return;
        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
        Vector2[] verts = HexVertices();

        var reveal = new Node2D { Position = center, Modulate = new Color(1f, 1f, 1f, minAlpha) };
        reveal.AddChild(new Polygon2D { Color = overlayColor, Polygon = verts });

        {
            float inset = HexSize * ShoreFoamInset;
            var foamOuter = new Color(1f, 1f, 1f, 1f);
            var foamInner = new Color(1f, 1f, 1f, 0f);
            for (int edge = 0; edge < 6; edge++)
            {
                Vector2 a = verts[edge];
                Vector2 b = verts[(edge + 1) % 6];
                bool facesSea = _state.Grid.Get(coord.Neighbor(EdgeToNeighborDirection[edge])) == null;
                if (facesSea)
                {
                    // Cover the OLD foam strip baked just OUTSIDE this edge so the
                    // current shoreline disappears as the tile floods.
                    Vector2 outward = ((a + b) * 0.5f).Normalized() * (inset * 1.35f);
                    reveal.AddChild(new Polygon2D
                    {
                        Color = overlayColor,
                        Polygon = new[] { a, b, b + outward, a + outward },
                    });
                }
                else
                {
                    // Draw the NEW foam strip that forms INSIDE this edge once the
                    // tile is sea and the surviving land neighbour becomes coast —
                    // white, fading inward (mirrors AddShoreFoamStrips' gradient).
                    Vector2 aIn = a - a.Normalized() * inset;
                    Vector2 bIn = b - b.Normalized() * inset;
                    reveal.AddChild(new Polygon2D
                    {
                        Color = ShoreFoamColor,
                        Polygon = new[] { a, b, bIn, aIn },
                        VertexColors = new[] { foamOuter, foamOuter, foamInner, foamInner },
                    });
                }
            }
            // Cover the OLD corner foam disks too (AddCornerFoamDisk drops one at
            // each vertex where BOTH adjacent edges face sea), else white dots
            // linger at the corners through the fade.
            for (int v = 0; v < 6; v++)
            {
                if (_state.Grid.Get(coord.Neighbor(EdgeToNeighborDirection[v])) != null) continue;
                if (_state.Grid.Get(coord.Neighbor(EdgeToNeighborDirection[(v + 5) % 6])) != null) continue;
                reveal.AddChild(MakeFoamCoverDisk(verts[v], inset * 1.3f, overlayColor));
            }
        }

        _tideForecastLayer.AddChild(reveal);
        Tween fade = reveal.CreateTween();
        fade.SetLoops();
        fade.TweenProperty(reveal, "modulate:a", maxAlpha, TideForecastPulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        fade.TweenProperty(reveal, "modulate:a", minAlpha, TideForecastPulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
    }

    /// <summary>
    /// A small filled convex disk (decagon) of <paramref name="color"/> centred at
    /// <paramref name="at"/> — used to cover a corner foam disk during the tide
    /// telegraph fade (see <see cref="DrawTideTelegraphTile"/>).
    /// </summary>
    private static Polygon2D MakeFoamCoverDisk(Vector2 at, float radius, Color color)
    {
        const int segments = 10;
        var pts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float ang = i * Mathf.Tau / segments;
            pts[i] = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
        }
        return new Polygon2D { Color = color, Polygon = pts };
    }

    /// <summary>
    /// Telegraph a demote-only shore mountain: cover the baked mountain
    /// ring band (the outer→inner channel <see cref="DrawMountains"/> draws) with
    /// the tile's land color and pulse that cover's alpha 0→1 in sync, so the
    /// ring's alpha appears to animate down to flat "demoted to lowland" at peak
    /// and back to the intact mountain at trough.
    /// </summary>
    private void DrawMountainErosionTelegraph(HexCoord coord)
    {
        if (_tideForecastLayer == null) return;
        HexTile? tile = _state.Grid.Get(coord);
        if (tile == null) return;
        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
        Vector2[] verts = HexVertices();
        Color land = PlayerPalette.ColorFor(tile.Owner);

        var reveal = new Node2D { Position = center, Modulate = new Color(1f, 1f, 1f, 0f) };
        for (int edge = 0; edge < 6; edge++)
        {
            int next = (edge + 1) % 6;
            reveal.AddChild(new Polygon2D
            {
                Color = land,
                Polygon = new[]
                {
                    verts[edge] * MountainBorderOuter,
                    verts[next] * MountainBorderOuter,
                    verts[next] * MountainBorderInner,
                    verts[edge] * MountainBorderInner,
                },
            });
        }
        _tideForecastLayer.AddChild(reveal);
        Tween fade = reveal.CreateTween();
        fade.SetLoops();
        fade.TweenProperty(reveal, "modulate:a", 1f, TideForecastPulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        fade.TweenProperty(reveal, "modulate:a", 0f, TideForecastPulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
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
            $"backdrop={(_selectionBackdrop != null ? $"attached(a={SelectionBackdropColor.A})" : "cleared")}");
    }

    public override void _Process(double delta)
    {
        // Long-frame probe: catches the hitch frame as a whole, including
        // Godot's redraw/flush of newly created nodes that happens after
        // our capture-path C# returns (which the inline timers miss).
        LogLongFrame(delta);

        AdvancePan(delta);

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

        // The backdrop is a tile-sized translucent-black hexagon at the
        // unit's center: it darkens the tile enough for the white rings
        // to read, while letting the territory fill and any co-located
        // capital/tower/tree/grave show through. CreateHexVisual
        // returns a Polygon2D, which is itself a Node2D — store it in
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
    /// Flash the "tap this unit to pick it up" cue on <paramref name="coord"/>,
    /// or clear it when null. The cue is a white hex with a
    /// black border whose alpha pulses — the same CTA idiom as the HUD's
    /// tutorial buttons, so it reads as "do this here" and never as the
    /// green "move TO here" rings. Excludes the cued unit from the
    /// scale-pulse set so the only motion is the alpha flash; restores the
    /// previously-cued unit to the pulse if it's still actionable.
    /// </summary>
    public void ShowSelectUnitCue(HexCoord? coord)
    {
        if (Equals(_selectCueUnit, coord)) return;

        HexCoord? previous = _selectCueUnit;
        _selectCueUnit = coord;

        if (previous.HasValue && IsActionableUnit(previous.Value))
        {
            _pulsingUnits.Add(previous.Value);
        }

        if (coord.HasValue) ApplySelectCueVisual();
        else ClearSelectCueVisual();

        Log.Debug(Log.LogCategory.Render,
            $"ShowSelectUnitCue: prev={previous?.ToString() ?? "none"} next={coord?.ToString() ?? "none"} " +
            $"node={(_selectCueNode != null ? "flashing" : "cleared")}");
    }

    /// <summary>
    /// Build (or rebuild) the flashing select-unit cue node for
    /// <c>_selectCueUnit</c> and start its alpha-pulse tween. Tears down
    /// any prior node/tween first. No-op if no cue is active. Called from
    /// <see cref="ShowSelectUnitCue"/> and re-invoked by
    /// <see cref="RefreshOccupantVisuals"/> after the units layer rebuild
    /// frees the old node (parallel to <see cref="ApplySelectionAffordance"/>).
    /// </summary>
    private void ApplySelectCueVisual()
    {
        ClearSelectCueVisual();
        if (!_selectCueUnit.HasValue) return;
        HexCoord coord = _selectCueUnit.Value;

        // Out of the scale-pulse set so the alpha flash is the only motion.
        _pulsingUnits.Remove(coord);

        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
        Vector2[] verts = HexVertices();
        var cue = new Node2D { Position = center };
        // White fill: this is the part that flashes (modulate alpha tween).
        var fill = new Polygon2D
        {
            Color = SelectCueFillColor,
            Polygon = verts,
            Modulate = new Color(1f, 1f, 1f, SelectCueFillMaxAlpha),
        };
        cue.AddChild(fill);
        // Black border: steady (sibling of the fill, not a child) so the tile
        // frame stays crisp even at the fill's translucent trough.
        cue.AddChild(BuildClosedOutline(verts, SelectCueBorderWidth, SelectCueBorderColor));
        _unitsLayer?.AddChild(cue);

        if (_unitsLayer != null
            && _unitVisuals.TryGetValue(coord, out Node2D? visual) && visual != null)
        {
            // Beneath the unit visual (later children draw on top) so the
            // unit reads as sitting on the flashing CTA tile.
            _unitsLayer.MoveChild(cue, visual.GetIndex());
            visual.Scale = Vector2.One;
        }
        _selectCueNode = cue;

        // Flash only the fill's alpha — max (translucent) ↔ min — so the unit
        // never washes out. Same sine cadence as HudView.StartCtaPulse.
        Tween tween = fill.CreateTween();
        tween.SetLoops();
        tween.TweenProperty(fill, "modulate:a", SelectCueFillMinAlpha, SelectCuePulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(fill, "modulate:a", SelectCueFillMaxAlpha, SelectCuePulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        _selectCueTween = tween;
    }

    /// <summary>
    /// Kill the select-unit cue's pulse tween and free its node. Defensive
    /// against a node already freed by a layer rebuild (guarded null/valid
    /// checks).
    /// </summary>
    private void ClearSelectCueVisual()
    {
        if (_selectCueTween != null && _selectCueTween.IsValid()) _selectCueTween.Kill();
        _selectCueTween = null;
        if (_selectCueNode != null)
        {
            _selectCueNode.QueueFree();
            _selectCueNode = null;
        }
    }

    /// <summary>
    /// Show (or, with <paramref name="coord"/> null, clear) the terrain-intro
    /// focus pulse (issue #53): a pulsing white hex with a yellow border that
    /// draws the eye to the tile the first-encounter hint is teaching. Tears
    /// down any prior pulse first, so it's safe to call per intro step and to
    /// clear on dismiss. No-op cue in silent/headless play.
    /// </summary>
    public void ShowTerrainFocusPulse(HexCoord? coord)
    {
        if (_focusPulseTween != null && _focusPulseTween.IsValid()) _focusPulseTween.Kill();
        _focusPulseTween = null;
        if (_focusPulseNode != null && IsInstanceValid(_focusPulseNode)) _focusPulseNode.QueueFree();
        _focusPulseNode = null;

        if (!coord.HasValue || _focusPulseLayer == null)
        {
            Log.Debug(Log.LogCategory.Render, "ShowTerrainFocusPulse: cleared");
            return;
        }

        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord.Value, HexSize);
        Vector2[] verts = HexVertices();
        var pulse = new Node2D { Position = center };
        // White fill flashes; yellow border pulses in scale with the container.
        var fill = new Polygon2D
        {
            Color = FocusPulseFillColor,
            Polygon = verts,
            Modulate = new Color(1f, 1f, 1f, FocusPulseFillMaxAlpha),
        };
        pulse.AddChild(fill);
        pulse.AddChild(BuildClosedOutline(verts, FocusPulseBorderWidth, FocusPulseBorderColor));
        _focusPulseLayer.AddChild(pulse);
        _focusPulseNode = pulse;

        // Scale + fill-alpha pulse together for a clear "look here" beat, same
        // sine cadence as the select-unit cue. Verts are centered on the node's
        // origin (the hex center), so scaling breathes symmetrically.
        Tween tween = pulse.CreateTween();
        tween.SetLoops();
        tween.TweenProperty(pulse, "scale",
                new Vector2(FocusPulseMaxScale, FocusPulseMaxScale), SelectCuePulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        tween.Parallel().TweenProperty(fill, "modulate:a", FocusPulseFillMinAlpha, SelectCuePulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        tween.TweenProperty(pulse, "scale", Vector2.One, SelectCuePulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        tween.Parallel().TweenProperty(fill, "modulate:a", FocusPulseFillMaxAlpha, SelectCuePulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        _focusPulseTween = tween;

        Log.Debug(Log.LogCategory.Render, $"ShowTerrainFocusPulse: pulsing at {coord.Value}");
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
            // Detach immediately, then free: a bare QueueFree keeps the old
            // node RENDERING until end of frame, so a clear-and-rebuild
            // (highlight, targets) overlaps old and new strokes for one
            // frame — a visible brightness pop on every redraw.
            layer.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// Rebuild every occupant visual (units + capitals) using the CTA
    /// coloring rules: the current player's actionable things get a
    /// white interior, everything else gets black. All shapes have a
    /// black border. Pass <paramref name="currentPlayer"/> = null to
    /// render everything non-CTA (e.g., while no turn is active).
    /// Capitals in <paramref name="visitedCapitals"/> are excluded from
    /// the actionable treatment — once the player has selected a
    /// territory this turn, its capital stops calling for attention.
    /// </summary>
    public void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury,
        IReadOnlySet<HexCoord> visitedCapitals)
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
        // Same story for the flashing select-unit cue node — it's a child of
        // the units layer that's about to be cleared. Drop the stale handle;
        // ApplySelectCueVisual below rebuilds it if a cue is still active.
        _selectCueNode = null;
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
                if (visitedCapitals.Contains(territory.Capital!.Value)) continue;
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
            // Fog Of War: occupants render only on tiles in current sight. Stale
            // and never-seen tiles show no occupant at all.
            if (_fog != null && FogTierOf(tile.Coord) != VisibilityTier.Visible) continue;

            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);

            if (tile.Occupant is Unit unit)
            {
                bool actionable = currentPlayer.HasValue
                    && unit.Owner == currentPlayer.Value
                    && !unit.HasMovedThisTurn;
                bool selected = _selectedUnit.HasValue && _selectedUnit.Value == tile.Coord;
                Node2D visual = CreateUnitVisual(actionable, unit.Level, viking: unit.Owner.IsNone);
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

        // Re-arm the flashing select-unit cue (its node was a child of the
        // just-rebuilt units layer). No-op if no cue is active.
        if (_selectCueUnit.HasValue) ApplySelectCueVisual();

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
    private static void StartShrinkAndFreeAnimation(
        Node2D visual, double durationSeconds = ShrinkDurationSeconds)
    {
        Tween tween = visual.CreateTween();
        tween.TweenProperty(visual, "scale", Vector2.Zero, durationSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.In);
        tween.Parallel().TweenProperty(visual, "modulate", new Color(1f, 1f, 1f, 0f), durationSeconds)
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
    // Rising Tides submerge effect: a land hex sinking + fading
    // under the sea, with an expanding water-tinted ripple ring on top.
    private const double SubmergeSinkDuration = 0.6;
    private const double SubmergeRippleDuration = 0.75;
    private static readonly Random DestructionRng = new Random();

    /// <summary>
    /// One-shot capture/chop visual. Spawns a tile-shaped white flash, an
    /// expanding shockwave ring, and a radial burst of shard polygons
    /// colored to match what was destroyed (unit owner color, stone for
    /// towers, green for trees). Graves are silent — burying isn't a
    /// destruction the player needs to see. All transient nodes free
    /// themselves when their tweens finish.
    /// </summary>
    /// <summary>True while an AI runs under Instant speed or during an instant
    /// replay; gates all per-action sound/anim and bankruptcy/game-won cues.
    /// Never set on a live human turn.</summary>
    private bool _silentMode;
    public void SetSilentMode(bool silent)
    {
        _silentMode = silent;
    }

    public void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed)
    {
        // Silent-mode gating is controller-side (GameOperations.IsSilent);
        // only the genuinely view-only gates remain (VFX toggle, graves are
        // never shown, no deaths layer yet).
        if (!UserSettings.VfxEnabled) return;
        if (destroyed is Grave) return;
        if (_deathsLayer == null) return;

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

        SpawnDestruction(coord, shardColor, shardCount);
        ApplyGlyphUpright();
    }

    // The grey rock color used for a demoting mountain's destruction shards —
    // matches the Tower shard tint (both are stone), pairing with the reused
    // TowerDestroyed sound.
    private static readonly Color MountainRockColor = new Color(0.72f, 0.72f, 0.76f, 1f);

    /// <summary>
    /// Rising Tides: a shore mountain demotes (loses its mountain
    /// status) before it can sink. Reuse the standard destruction burst with
    /// grey rock shards — paired with the <c>TowerDestroyed</c> sound — so the
    /// crumble reads clearly. Gated like every other cue by the VFX toggle and
    /// silent mode (Instant AI / instant replay).
    /// </summary>
    public void PlayMountainDemoteEffect(HexCoord coord)
    {
        if (!UserSettings.VfxEnabled) return;
        if (_silentMode) return;
        if (_deathsLayer == null) return;
        SpawnDestruction(coord, MountainRockColor, TowerShardCount);
        ApplyGlyphUpright();
    }

    // Shared flash + shockwave + shard burst on _deathsLayer, used by both the
    // occupant-destruction effect and the Rising Tides mountain-demote effect.
    private void SpawnDestruction(HexCoord coord, Color shardColor, int shardCount)
    {
        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
        SpawnDestructionFlash(center);
        SpawnDestructionShockwave(center, shardColor);
        for (int i = 0; i < shardCount; i++)
        {
            SpawnDestructionShard(center, shardColor, i, shardCount);
        }
    }

    /// <summary>
    /// Rising Tides: a shore tile sinking under the sea. The land
    /// hex (in <paramref name="landColor"/>, the owner's fill) scales inward and
    /// fades to nothing, with one expanding water-tinted ripple ring on top.
    /// Gated by the VFX toggle and silent mode like every other cue.
    /// </summary>
    private void PlaySubmergeEffect(HexCoord coord, Color landColor)
    {
        if (!UserSettings.VfxEnabled) return;
        if (_silentMode) return;
        if (_deathsLayer == null) return;

        Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);

        // Sink-fade: the owner's color shrinks toward the tile center and fades,
        // reading as the land slipping under the surface.
        var sink = CreateHexVisual(center,
            new Color(landColor.R, landColor.G, landColor.B, 0.9f));
        _deathsLayer.AddChild(sink);
        Tween sinkTween = sink.CreateTween();
        sinkTween.TweenProperty(sink, "scale", new Vector2(0.5f, 0.5f), SubmergeSinkDuration)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        sinkTween.Parallel()
            .TweenProperty(sink, "modulate", new Color(1f, 1f, 1f, 0f), SubmergeSinkDuration)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        sinkTween.Finished += sink.QueueFree;

        // Ripple: an expanding foam-tinted ring spreading from the sunk tile.
        SpawnSubmergeRipple(center, WaterColor.Lerp(ShoreFoamColor, 0.6f));
    }

    private void SpawnSubmergeRipple(Vector2 center, Color color)
    {
        const int segments = 36;
        float startRadius = HexSize * 0.2f;
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Tau * i / segments;
            points[i] = new Vector2(startRadius * Mathf.Cos(a), startRadius * Mathf.Sin(a));
        }
        var ring = new Line2D
        {
            Position = center,
            Points = points,
            Width = 6f,
            DefaultColor = color,
        };
        _deathsLayer!.AddChild(ring);

        const float endScale = 1.6f / 0.2f; // 0.2 → 1.6 hex radii
        Tween tween = ring.CreateTween();
        tween.TweenProperty(ring, "scale", new Vector2(endScale, endScale), SubmergeRippleDuration)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.Parallel()
            .TweenProperty(ring, "modulate", new Color(1f, 1f, 1f, 0f), SubmergeRippleDuration)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
        tween.Finished += ring.QueueFree;
    }

    /// <summary>
    /// Single sound-dispatch entry point. The <paramref name="at"/> coord is
    /// reserved for future spatial audio (AudioBus plays non-spatial 2D today).
    /// Silent mode gates every cue with no exceptions.
    /// </summary>
    public void PlaySound(SoundEffect kind, HexCoord? at = null)
    {
        // Silent-mode gating is controller-side (GameOperations.IsSilent);
        // the view plays whatever cue it's handed.
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
            case SoundEffect.TileSubmerged: AudioBus.Instance.PlayTileSubmerged(); break;
            case SoundEffect.VikingArrival: AudioBus.Instance.PlayVikingArrival(); break;
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

        // Shard geometry: small irregular triangle, sized so shards read
        // against tile fills.
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
    // toolbar swatch and on the tile itself. The trunked conifer reads
    // well at small scales and is consistent with the existing icon language.
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
    // Outermost ring is thicker than the inner rings so the unit silhouette
    // reads clearly on top of busy terrain (mountain glyphs). Inner
    // rings stay thin to keep the level-count legible.
    private static readonly float[] UnitRingWidthFactors = { 0.10f, 0.05f, 0.045f };
    private const float UnitDotRadius = 0.08f;
    private const int UnitRingSegments = 28;

    private Node2D CreateUnitVisual(bool actionable, UnitLevel level, bool viking = false)
    {
        // Viking Raiders: raiders carry the fixed-palette painted-shield
        // glyph instead of the concentric rings — identical on land and at
        // sea, and never actionability-colored (a viking is never the
        // current player's unit).
        if (viking) return CreateVikingShieldVisual(level);

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

    // Viking "painted shield" glyph (VIKING_UNIT_GLYPH.md +
    // VIKING_RANK1_CHANGE.md): a cream shield seen from above, rank shown
    // by a doubling ladder of painted ink segments — half-painted (Recruit),
    // quartered (Soldier), eight segments (Captain). Fixed ink/cream
    // palette, never player-colored; flat fills only; same size and anchor
    // as an ordinary unit (outer radius = the ordinary glyph's outer ring).
    // Spec coordinates are in glyph units where that outer radius is 16, so
    // 1 glyph unit = HexSize * UnitRingRadii[0] / 16.
    private static readonly Color VikingInk = new Color("111111");
    private static readonly Color VikingCream = new Color("efe6d4");
    private const float ShieldRadiusUnits = 16f;
    private const float ShieldRimWidthUnits = 3.5f;
    private const float ShieldBossRadiusUnits = 4.5f;
    private const float ShieldBossHighlightRadiusUnits = 2f;
    private const int ShieldSectorSteps = 12;

    private Node2D CreateVikingShieldVisual(UnitLevel level)
    {
        float unit = HexSize * UnitRingRadii[0] / ShieldRadiusUnits;
        float radius = ShieldRadiusUnits * unit;
        // Recruit=1 half-painted, Soldier=2 quartered, Captain=3 eight-segment.
        // Commander can't occur (vikings cap at Captain); map it defensively
        // to the richest shield rather than crash a render pass.
        int rank = level switch
        {
            UnitLevel.Recruit => 1,
            UnitLevel.Soldier => 2,
            _ => 3,
        };

        var node = new Node2D();
        // 1. Shield base.
        node.AddChild(CreateFilledDisc(radius, VikingCream));
        // 2. Rank wedges (0° = +x, increasing clockwise in y-down space).
        if (rank == 1)
        {
            // Half-painted: the east (x ≥ 0) half ink, sweeping from
            // straight up through east to straight down.
            node.AddChild(CreateSectorPolygon(radius, -90f, 90f, VikingInk));
        }
        else if (rank == 2)
        {
            node.AddChild(CreateSectorPolygon(radius, 0f, 90f, VikingInk));
            node.AddChild(CreateSectorPolygon(radius, 180f, 270f, VikingInk));
        }
        else
        {
            for (int fromDeg = 0; fromDeg < 360; fromDeg += 90)
            {
                node.AddChild(CreateSectorPolygon(radius, fromDeg, fromDeg + 45f, VikingInk));
            }
        }
        // 3. Rim (stroke centered on the shield edge).
        node.AddChild(CreateCircleOutline(radius, VikingInk, ShieldRimWidthUnits * unit));
        // 4. Boss.
        node.AddChild(CreateFilledDisc(ShieldBossRadiusUnits * unit, VikingInk));
        // 5. Boss highlight — every rank (the painted background keeps the
        //    boss reading as a boss, not a pupil).
        node.AddChild(CreateFilledDisc(ShieldBossHighlightRadiusUnits * unit, VikingCream));
        return node;
    }

    /// <summary>Filled circular sector (pie wedge) from the node's center
    /// out to <paramref name="radius"/>, spanning the given angles
    /// (degrees, 0° = +x, clockwise in y-down space).</summary>
    private static Polygon2D CreateSectorPolygon(
        float radius, float fromDeg, float toDeg, Color color)
    {
        var points = new Vector2[ShieldSectorSteps + 2];
        points[0] = Vector2.Zero;
        for (int i = 0; i <= ShieldSectorSteps; i++)
        {
            float deg = fromDeg + (toDeg - fromDeg) * i / ShieldSectorSteps;
            float rad = Mathf.DegToRad(deg);
            points[i + 1] = new Vector2(radius * Mathf.Cos(rad), radius * Mathf.Sin(rad));
        }
        return new Polygon2D
        {
            Color = color,
            Polygon = points,
        };
    }

    // Viking Raiders: what the sea layer currently shows, per coord, so
    // ShowSeaVikings diffs incrementally across the frequent RefreshViews
    // calls of a turn (the sets only change on viking-phase beats) and can
    // animate the shield→grave transition when a raider perishes.
    private readonly Dictionary<HexCoord, (SeaViking Viking, Node2D Visual)> _seaVikingVisuals = new();
    private readonly Dictionary<HexCoord, Node2D> _seaGraveVisuals = new();

    // Sea graves wash away noticeably slower than the land 0.25s shrink —
    // they disappear at the top of a busy viking turn, and at the land pace
    // the wash-away doesn't read.
    private const double SeaGraveWashDurationSeconds = 0.9;

    /// <summary>
    /// Viking Raiders: render the raiders waiting at sea — the same painted
    /// shield a landed viking carries, directly on the water — plus a grave
    /// marker wherever a raider perished this round. A shield whose coord
    /// now holds a sea grave gets the land bankruptcy choreography: the
    /// shield shrinks out on the deaths layer while the grave grows in
    /// beneath (staggered, like a corpse). Graves shrink out the same way
    /// when their list empties (the next viking turn began) — like a land
    /// grave being promoted, except no tree follows. Empty lists clear the
    /// layer.
    /// </summary>
    public void ShowSeaVikings(
        System.Collections.Generic.IReadOnlyList<SeaViking> atSea,
        System.Collections.Generic.IReadOnlyList<HexCoord> seaGraves)
    {
        if (_seaVikingsLayer == null) return;

        var desired = new Dictionary<HexCoord, SeaViking>(atSea.Count);
        foreach (SeaViking v in atSea) desired[v.Coord] = v;
        var desiredGraves = new HashSet<HexCoord>(seaGraves);

        // Shields that left the sea. One whose coord now shows a grave
        // perished there — shrink it out like a bankrupt land unit.
        var justPerished = new HashSet<HexCoord>();
        var staleShields = new List<HexCoord>();
        foreach (KeyValuePair<HexCoord, (SeaViking Viking, Node2D Visual)> kvp in _seaVikingVisuals)
        {
            if (!desired.TryGetValue(kvp.Key, out SeaViking now) || now != kvp.Value.Viking)
            {
                staleShields.Add(kvp.Key);
            }
        }
        foreach (HexCoord c in staleShields)
        {
            Node2D shield = _seaVikingVisuals[c].Visual;
            if (!_silentMode && desiredGraves.Contains(c) && !_seaGraveVisuals.ContainsKey(c))
            {
                justPerished.Add(c);
                // Animate IN PLACE (still a sea-layer child): the deaths
                // layer is ClearLayer'd by every capture rebuild, and the
                // next viking beat's capture would cut this tween off
                // before it reads. Nothing clears the sea layer mid-phase.
                StartShrinkAndFreeAnimation(shield);
            }
            else
            {
                _seaVikingsLayer.RemoveChild(shield);
                shield.QueueFree();
            }
            _seaVikingVisuals.Remove(c);
        }

        // Graves that washed away: shrink out like a land grave being
        // promoted at start-of-turn — except no tree ever follows, and at a
        // slower, sea-specific pace so the wash-away actually reads amid
        // the viking turn's beat traffic. In place (sea-layer child) for
        // the same cut-off reason as the perish shrink above.
        var staleGraves = new List<HexCoord>();
        foreach (HexCoord c in _seaGraveVisuals.Keys)
        {
            if (!desiredGraves.Contains(c)) staleGraves.Add(c);
        }
        foreach (HexCoord c in staleGraves)
        {
            Node2D grave = _seaGraveVisuals[c];
            if (_silentMode)
            {
                _seaVikingsLayer.RemoveChild(grave);
                grave.QueueFree();
            }
            else
            {
                StartShrinkAndFreeAnimation(grave, SeaGraveWashDurationSeconds);
            }
            _seaGraveVisuals.Remove(c);
        }

        // New graves grow in — staggered after the shrink when the shield
        // just perished there, exactly like a land grave under a corpse.
        // Added before new shields so a later wave spawning on a grave
        // coord draws its shield on top.
        foreach (HexCoord c in desiredGraves)
        {
            if (_seaGraveVisuals.ContainsKey(c)) continue;
            Node2D grave = CreateGraveVisual();
            grave.Position = FirstHexCenterOffset + HexPixel.ToPixel(c, HexSize);
            _seaVikingsLayer.AddChild(grave);
            if (!_silentMode)
            {
                StartGraveGrowAnimation(grave, afterDeath: justPerished.Contains(c));
            }
            _seaGraveVisuals[c] = grave;
        }

        // New shields — every new sea shield IS a fresh spawn (raiders only
        // ever enter the sea via a wave), so play the "ripple rise" entrance
        // (VIKING_SPAWN_ANIMATION.md): the unit surfaces from the deep with
        // an overshooting scale-in while white rings ripple outward, the
        // whole wave rising in unison. Suppressed for one pass after a full
        // scene rebuild (_animateSeaSpawns) — re-added existing raiders must
        // not re-rise — and entirely under silent mode (snap to full
        // scale/alpha).
        bool animateSpawns = _animateSeaSpawns && !_silentMode;
        foreach (KeyValuePair<HexCoord, SeaViking> kvp in desired)
        {
            if (_seaVikingVisuals.ContainsKey(kvp.Key)) continue;
            Node2D shield = CreateVikingShieldVisual(kvp.Value.Level);
            shield.Position = FirstHexCenterOffset + HexPixel.ToPixel(kvp.Key, HexSize);
            _seaVikingsLayer.AddChild(shield);
            _seaVikingVisuals[kvp.Key] = (kvp.Value, shield);
            if (animateSpawns)
            {
                StartSeaSpawnAnimation(shield);
            }
        }
        _animateSeaSpawns = true;
    }

    // "Ripple rise" tuning: VIKING_SPAWN_ANIMATION.md's spec timings scaled
    // by a single slow-down factor (1.0 = spec pacing). The whole wave
    // surfaces at once — no per-unit stagger.
    private const double SeaSpawnSlowdown = 2.5;
    private const double SeaSpawnScaleInSeconds = 0.9 * SeaSpawnSlowdown;
    private const double SeaSpawnFadeInSeconds = 0.55 * SeaSpawnSlowdown;

    // One-pass suppression after a full scene rebuild: BuildStateVisuals
    // clears the sea dicts, so the next ShowSeaVikings re-adds every
    // existing raider — those must not replay their entrance.
    private bool _animateSeaSpawns = true;

    private void StartSeaSpawnAnimation(Node2D shield)
    {
        shield.Scale = Vector2.Zero;
        shield.Modulate = new Color(1f, 1f, 1f, 0f);
        Tween tween = shield.CreateTween();
        tween.TweenProperty(shield, "scale", Vector2.One, SeaSpawnScaleInSeconds)
            .SetTrans(Tween.TransitionType.Back)
            .SetEase(Tween.EaseType.Out);
        tween.Parallel().TweenProperty(shield, "modulate:a", 1f, SeaSpawnFadeInSeconds)
            .SetEase(Tween.EaseType.Out);

        // Ripple rings are decorative — gated like other VFX.
        if (UserSettings.VfxEnabled && _seaVikingsLayer != null)
        {
            var ripple = new SeaSpawnRippleRings
            {
                UnitRadius = HexSize * UnitRingRadii[0],
                Position = shield.Position,
            };
            _seaVikingsLayer.AddChild(ripple);
            // Beneath every shield/grave in the layer.
            _seaVikingsLayer.MoveChild(ripple, 0);
        }
    }

    /// <summary>
    /// The spawn ripple: two flat white circle outlines expanding outward
    /// from the spawn point and fading (VIKING_SPAWN_ANIMATION.md, timings ×
    /// <see cref="SeaSpawnSlowdown"/>) — ring 2 starts a beat after ring 1,
    /// each radius R·0.25 → ~R·1.9 ease-out with a linear alpha fade.
    /// Constant 2.5-unit stroke via <c>_Draw</c> (scaling a Line2D would
    /// fatten the stroke). Set <see cref="UnitRadius"/> before adding to
    /// the tree; tweens start in <c>_Ready</c>; frees itself when done.
    /// </summary>
    private sealed partial class SeaSpawnRippleRings : Node2D
    {
        public float UnitRadius { get; set; } = 16f;

        private const double RingSeconds = 1.5 * SeaSpawnSlowdown;
        private const double Ring2DelaySeconds = 0.4 * SeaSpawnSlowdown;
        private const float StrokeWidth = 2.5f;

        private float _radius1, _alpha1, _radius2, _alpha2;

        public override void _Ready()
        {
            Tween ring1 = CreateTween();
            ring1.TweenMethod(Callable.From((float r) => { _radius1 = r; QueueRedraw(); }),
                    UnitRadius * 0.25f, UnitRadius * 1.9f, RingSeconds)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            ring1.Parallel().TweenMethod(Callable.From((float a) => _alpha1 = a),
                0.8f, 0f, RingSeconds);

            Tween ring2 = CreateTween();
            ring2.TweenInterval(Ring2DelaySeconds);
            ring2.TweenMethod(Callable.From((float r) => { _radius2 = r; QueueRedraw(); }),
                    UnitRadius * 0.25f, UnitRadius * 1.75f, RingSeconds)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            ring2.Parallel().TweenMethod(Callable.From((float a) => _alpha2 = a),
                0.7f, 0f, RingSeconds);
            // Ring 2 finishes last; the whole effect frees with it.
            ring2.Finished += QueueFree;
        }

        public override void _Draw()
        {
            if (_alpha1 > 0f)
            {
                DrawArc(Vector2.Zero, _radius1, 0f, Mathf.Tau, 64,
                    new Color(1f, 1f, 1f, _alpha1), StrokeWidth, antialiased: true);
            }
            if (_alpha2 > 0f)
            {
                DrawArc(Vector2.Zero, _radius2, 0f, Mathf.Tau, 64,
                    new Color(1f, 1f, 1f, _alpha2), StrokeWidth, antialiased: true);
            }
        }
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
                StopPan();
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
                StopPan();
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
        bool inBounds = HitTestMath.InOffsetBounds(col, row, Cols, Rows);
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
        if (!HitTestMath.InOffsetBounds(col, row, Cols, Rows)) return;
        if (!force && coord.Equals(_lastPaintedCoord)) return;
        _lastPaintedCoord = coord;
        PaintCellEntered?.Invoke(coord);
    }

    /// <summary>Visible center of the play area in viewport space — accounts
    /// for the HUD's reserved insets at the top and bottom.</summary>
    private Vector2 VisualCenter()
    {
        Vector2 vp = GetViewportRect().Size;
        (float x, float y) = PanMath.VisualCenter(vp.X, vp.Y, _topInset, _bottomInset);
        return new Vector2(x, y);
    }

    /// <summary>Clamp the proposed Position so the map can't be dragged off-
    /// screen. If the map is smaller than the available area on an axis,
    /// that axis is locked to its centered value. The grid's effective
    /// pixel extent is PixelSize × _zoom because we apply zoom via
    /// Node2D.Scale.</summary>
    private Vector2 ClampPan(Vector2 desired)
    {
        Vector2 vp = GetViewportRect().Size;
        // On-screen bounding box of the (scaled + rotated) full nominal grid
        // (Cols×Rows), relative to this node's origin. Pan-clamping frames the
        // whole grid — NOT the content box — so a sparsely-painted editor map or
        // an off-center level can still pan across the full board. Initial
        // centering is content-aware separately in RecenterMap. At angle 0 this
        // reduces to (0,0,w·zoom,h·zoom), the legacy landscape clamp.
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.RotatedBoardBox(PixelSize.X, PixelSize.Y, _zoom, _mapAngleRad);
        // Symmetric scrollable pad applied in viewport space — a rotated
        // symmetric pad is still symmetric, so PanMath widens the rotated AABB
        // directly. This lets edge hexes pan clear of the floating HUD
        // chips/buttons that overlay the viewport corners. Sized in board-local
        // pixels and scaled by zoom to match the rest of the box.
        float pad = ScrollPaddingPx * _zoom;
        (float x, float y) = PanMath.Clamp(
            desired.X, desired.Y, vp.X, vp.Y, _topInset, _bottomInset,
            minX, minY, maxX, maxY, pad);
        return new Vector2(x, y);
    }

    public void CenterOnTerritory(Territory territory)
    {
        if (!territory.HasCapital) return;
        CenterOnCoord(territory.Capital!.Value);
    }

    public void CenterOnCoord(HexCoord coord)
    {
        // The tile's pixel position is in unscaled local space; map it to a
        // world offset (zoom + rotation) before subtracting from VisualCenter.
        Vector2 localCenter = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);
        Vector2 target = ClampPan(VisualCenter() - ToWorldOffset(localCenter, _zoom));

        // Instant AI batch / instant replay (or a target that's already here):
        // snap so fast play never lags behind the board.
        if (_silentMode || (target - Position).Length() < PanSnapEpsilonPx)
        {
            _panActive = false;
            Position = target;
            return;
        }

        // Start / retarget the ease. Retargeting from the *current* Position
        // (not the old _panTo) is what makes rapid successive selections
        // graceful — no queue buildup, no snap-back.
        _panFrom = Position;
        _panTo = target;
        _panElapsed = 0;
        _panActive = true;
        Log.Debug(Log.LogCategory.Render,
            $"CenterOnCoord pan from={_panFrom} to={_panTo} dur={PanAnimDurationSec:0.00}s");
    }

    /// <summary>Abandon any in-flight camera pan so a manual drag / keyboard /
    /// zoom gesture takes over immediately (the animation would otherwise
    /// overwrite Position next frame in <see cref="_Process"/>).</summary>
    private void StopPan() => _panActive = false;

    /// <summary>Advance the CenterOnTerritory ease one frame. No-op unless a
    /// pan is active. Both endpoints sit inside the axis-aligned pan-clamp
    /// rectangle (convex), so every eased Position stays validly clamped.</summary>
    private void AdvancePan(double delta)
    {
        if (!_panActive) return;
        _panElapsed += delta;
        if (_panElapsed >= PanAnimDurationSec)
        {
            Position = _panTo;
            _panActive = false;
            Log.Debug(Log.LogCategory.Render, $"CenterOnTerritory pan done at={Position}");
            return;
        }
        float s = EasingMath.SmoothStep((float)(_panElapsed / PanAnimDurationSec));
        Position = new Vector2(
            EasingMath.Lerp(_panFrom.X, _panTo.X, s),
            EasingMath.Lerp(_panFrom.Y, _panTo.Y, s));
    }

    /// <summary>Frame the camera at <paramref name="zoom"/> with the view
    /// centered <paramref name="contentCenterOffset"/> away from the content
    /// box's center (content/local coords, +x right / +y down). Clamps the
    /// zoom to the allowed range and re-syncs the discrete level index, like
    /// a user gesture would. Callers use this to open a scene with a
    /// hand-tuned framing instead of the RecenterMap fit default (e.g. the
    /// tutorial's landscape camera).</summary>
    public void SetCamera(float zoom, Vector2 contentCenterOffset)
    {
        _zoom = Mathf.Clamp(zoom, _zoomMin, 1f);
        Scale = new Vector2(_zoom, _zoom);
        _zoomLevelIndex = ZoomMath.ClosestLevelIndex(_zoomLevels, _zoom);
        (float ccx, float ccy) = MapPlacement.BoxCenter(
            _contentBox.minX, _contentBox.minY, _contentBox.maxX, _contentBox.maxY);
        var contentCenter = new Vector2(ccx, ccy) + contentCenterOffset;
        Position = ClampPan(VisualCenter() - ToWorldOffset(contentCenter, _zoom));
        LogCameraState("set");
    }

    /// <summary>Frame the full <em>nominal grid rectangle</em> (Cols×Rows
    /// <see cref="PixelSize"/>) centered — deliberately NOT the content/land
    /// box. Used by the main-menu map thumbnail: the land bounding box varies
    /// per seed, but the grid rectangle is seed-independent, so this keeps the
    /// previewed board at a fixed scale and position when the seed is re-rolled
    /// (only the tiles inside change). Rotation-aware via
    /// <see cref="VisualCenter"/> / <see cref="ToWorldOffset"/>.
    /// <paramref name="overscan"/> &gt; 1 over-scales so the jagged perimeter is
    /// clipped to clean edges (used by the menu thumbnail).</summary>
    public void FrameWholeGrid(float overscan = 1f)
    {
        _zoom = _zoomFit * overscan;
        Scale = new Vector2(_zoom, _zoom);
        _zoomLevelIndex = ZoomMath.ClosestLevelIndex(_zoomLevels, _zoom);
        (float gcx, float gcy) = MapPlacement.BoxCenter(0f, 0f, PixelSize.X, PixelSize.Y);
        var gridCenter = new Vector2(gcx, gcy);
        Position = ClampPan(VisualCenter() - ToWorldOffset(gridCenter, _zoom));
        LogCameraState("frame-grid");
    }

    /// <summary>Center the (possibly rotated) content in the play area. Uses the
    /// content box center (not the nominal grid center) so a level whose tiles
    /// sit off-center in a padded grid still frames centered.</summary>
    private void RecenterMap()
    {
        (float ccx, float ccy) = MapPlacement.BoxCenter(
            _contentBox.minX, _contentBox.minY, _contentBox.maxX, _contentBox.maxY);
        var contentCenter = new Vector2(ccx, ccy);
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
    private Vector2 ToWorldOffset(Vector2 localOffset, float zoom)
    {
        (float x, float y) = MapPlacement.ToWorldOffset(localOffset.X, localOffset.Y, zoom, _mapAngleRad);
        return new Vector2(x, y);
    }

    /// <summary>Recompute zoom range and discrete levels for the current
    /// viewport size and snap _zoom into range. Called on _Ready and
    /// whenever the OS window resizes.</summary>
    private void RecomputeZoomLevels()
    {
        Vector2 vp = GetViewportRect().Size;
        // Fit-to-view against the ROTATED extent (width/height swap at ±90°).
        (float minX, float minY, float maxX, float maxY) =
            MapPlacement.RotatedBoardBox(PixelSize.X, PixelSize.Y, 1f, _mapAngleRad);
        _zoomFit = ZoomMath.ComputeZoomMin(
            vp.X, vp.Y, _topInset + _bottomInset, maxX - minX, maxY - minY);
        _zoomMin = ZoomMath.ComputeZoomMin(
            vp.X, vp.Y, _topInset + _bottomInset, maxX - minX, maxY - minY,
            ZoomOutGrace);
        _zoomLevels = ZoomMath.BuildLevels(_zoomMin, ZoomLevelCount);

        _zoom = Mathf.Clamp(_zoom, _zoomMin, 1f);
        _zoomLevelIndex = ZoomMath.ClosestLevelIndex(_zoomLevels, _zoom);
        Scale = new Vector2(_zoom, _zoom);
        Log.Debug(Log.LogCategory.Render,
            $"RecomputeZoomLevels: vp={vp.X:0}x{vp.Y:0} insets=({_topInset:0},{_bottomInset:0}) " +
            $"fit={_zoomFit:0.000} zoomMin={_zoomMin:0.000} " +
            $"levels=[{string.Join(",", System.Array.ConvertAll(_zoomLevels, v => v.ToString("0.000")))}]");
    }

    private void OnViewportResized()
    {
        ulong frame = Engine.GetProcessFrames();
        ulong t0 = Time.GetTicksMsec();
        Vector2 vp = GetViewportRect().Size;
        Log.Debug(Log.LogCategory.Render,
            $"HexMapView: resize@frame={frame} t={t0}ms vp={vp.X}x{vp.Y}.");

        // The viewport changed under any in-flight pan, so its target is
        // stale — abandon it and let the re-clamp / recenter below settle.
        StopPan();
        bool flipped = ResolveRotation();
        RecomputeZoomLevels();
        if (flipped)
        {
            // Orientation changed: re-upright the glyphs and recenter the
            // board (the old pan is meaningless under the new rotation).
            ApplyGlyphUpright();
            RecenterMap();
            // The mountain channel's bevel shading is baked relative to the board,
            // so rebake it to keep the light coming from the same screen direction
            // (top-left) under the new rotation.
            DrawMountains();
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
        StopPan();
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

        // A zoom gesture reframes the board — abandon any in-flight pan so it
        // doesn't overwrite the zoom-anchored Position next frame.
        StopPan();
        // ToLocal uses the current Position+Scale, so localUnderAnchor is
        // in the unscaled local frame. After we change Scale, we want
        // anchorVp == Position + localUnderAnchor * newZoom.
        Vector2 localUnderAnchor = ToLocal(anchorVp);
        Scale = new Vector2(newZoom, newZoom);
        _zoom = newZoom;
        Position = ClampPan(anchorVp - ToWorldOffset(localUnderAnchor, newZoom));
        _zoomLevelIndex = ZoomMath.ClosestLevelIndex(_zoomLevels, _zoom);
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
        int from = ZoomMath.ClosestLevelIndex(_zoomLevels, _zoom);
        int next = ZoomMath.StepLevel(_zoomLevels, _zoom, delta);
        // Skip a no-op step (already exactly on the target stop); the
        // IsEqualApprox guard + ApplyZoom dispatch stay view-side.
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
            Color c = PlayerPalette.DarkColorFor(EffectiveOwner(tile));
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

                // Compare PAINTED owners so a stale tile's remembered boundary
                // shows (and a capture out of the human's sight doesn't leak a
                // shifted border). Identity outside Fog Of War.
                bool isBoundary = neighbor == null
                    || EffectiveOwner(neighbor) != EffectiveOwner(tile);
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
    private const float GoldBorderOuter = 1.0f;
    private const float GoldBorderInner = 0.74f;

    // Mountain ring channel: a hex-ring band, a touch thicker than the
    // gold one, shaded as a raised "plateau" — a bright lit
    // top rim over a near-black outer drop-shadow skirt — so the tile reads as a
    // lifted slab rather than a flat band. See DrawMountains.
    private const float MountainBorderOuter = 1.0f;
    private const float MountainBorderInner = 0.68f;
    // Light from the top-left (screen +y is down, so up-left is (-1,-1)); baked
    // screen-fixed so it holds under portrait rotation (see DrawMountains).
    private static readonly Vector2 MountainLightDir = new Vector2(-1f, -1f).Normalized();
    private const float MountainSkirt = 0.03f;     // outer drop-shadow ring (near-black)
    private const float MountainTopBase = 0.40f;   // lit top-rim brightness (inner edge)
    private const float MountainTopSwing = 0.24f;  // top rim brightens toward light, darkens away

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


    /// <summary>
    /// Draw a differentially-shaded hex-ring channel inside every
    /// <see cref="HexTile.IsMountain"/> tile. The same batched TriangleSoup technique as
    /// <see cref="DrawGoldBorders"/>, but shaded as a raised plateau: a near-black
    /// outer drop-shadow skirt under a bright inner top rim that brightens toward
    /// the top-left light, so the tile reads as a lifted slab. Gold and mountain
    /// are mutually exclusive, so a tile shows at most one of the two rings; both
    /// coexist with a tree / grave / unit / tower drawn on top.
    /// </summary>
    private void DrawMountains()
    {
        if (_mountainBordersLayer == null) return;
        Vector2[] verts = HexVertices();
        var outer = new Vector2[6];
        var inner = new Vector2[6];
        // Per-corner grey shades — identical for every tile (geometry is the same,
        // only the center offset differs), so compute once. Outer corners are the
        // dark skirt; inner corners are the lit top rim, brightened toward the light.
        var outerShade = new Color[6];
        var innerShade = new Color[6];
        // The whole node is rotated by _mapAngleRad, so counter-rotate the light
        // into local space to keep it appearing from the same screen direction
        // (top-left) in both landscape and portrait. (a.Rotated(θ))·L == a·(L.Rotated(−θ)).
        Vector2 lightLocal = MountainLightDir.Rotated(-_mapAngleRad);
        for (int i = 0; i < 6; i++)
        {
            outer[i] = verts[i] * MountainBorderOuter;
            inner[i] = verts[i] * MountainBorderInner;
            float angular = verts[i].Normalized().Dot(lightLocal);  // [-1, 1]
            outerShade[i] = Grey(MountainSkirt);
            innerShade[i] = Grey(MountainTopBase + MountainTopSwing * angular);
        }

        var builder = new TriangleSoupBuilder();
        int built = 0;
        foreach (HexTile tile in Grid.Tiles)
        {
            if (!tile.IsMountain) continue;
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(tile.Coord, HexSize);
            for (int edge = 0; edge < 6; edge++)
            {
                int next = (edge + 1) % 6;
                var quad = new[] { outer[edge], outer[next], inner[next], inner[edge] };
                var quadShade = new[] { outerShade[edge], outerShade[next], innerShade[next], innerShade[edge] };
                builder.AddPolygon(center, quad, Colors.White, quadShade);
            }
            built++;
        }
        _mountainBordersLayer.SetTriangles(
            builder.Points.ToArray(), builder.Colors.ToArray(), builder.Indices.ToArray());
        Log.Debug(Log.LogCategory.Render,
            $"DrawMountains: built {built} mountain channels (plateau, light TL)");
    }

    // Opaque grey of the given brightness, clamped to [0, 1].
    private static Color Grey(float b)
    {
        float v = Mathf.Clamp(b, 0f, 1f);
        return new Color(v, v, v, 1f);
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

        // Like AddPolygon but triangulated as a fan from an explicit CENTER
        // vertex (centerColor) out to each perimeter vertex (perimeterColors).
        // Every triangle shares the center, so adjacent triangles share a
        // center→vertex edge with matching endpoint colors — the per-vertex
        // alpha gradient stays continuous with no spanning "spike" triangles
        // (which ear-clipping produces when interior and edge alphas differ).
        public void AddFan(Vector2 offset, Vector2[] perimeter, Color centerColor, Color[] perimeterColors)
        {
            int c = Points.Count;
            Points.Add(offset);           // perimeter is local to the hex center (origin)
            Colors.Add(centerColor);
            for (int i = 0; i < perimeter.Length; i++)
            {
                Points.Add(offset + perimeter[i]);
                Colors.Add(perimeterColors[i]);
            }
            for (int i = 0; i < perimeter.Length; i++)
            {
                Indices.Add(c);
                Indices.Add(c + 1 + i);
                Indices.Add(c + 1 + ((i + 1) % perimeter.Length));
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
    // Rising Tides: the selection outline around a tile that's forecast to
    // SUBMERGE this turn pulses inversely to its tide telegraph — fully drawn
    // at the "land" trough, faded to this alpha at the flooded peak — so the
    // ring recedes as the tile floods instead of sitting static over water.
    private const float DoomedHighlightMinAlpha = 0.0f;

    private void RedrawHighlight()
    {
        if (_highlightLayer == null) return;

        ClearLayer(_highlightLayer);

        if (_highlightedTerritory == null) return;

        Vector2[] verts = HexVertices();
        var inside = new HashSet<HexCoord>(_highlightedTerritory.Coords);
        float haloWidth = HexSize * SelectionHaloWidthFactor;
        float coreWidth = HexSize * SelectionCoreWidthFactor;

        // Rising Tides: tiles forecast to SUBMERGE this turn (not demote-only
        // mountains, which stay land) get their perimeter strokes grouped under
        // a per-tile node that pulses inversely to the tide telegraph, so the
        // ring recedes as the tile floods rather than sitting static. Empty in
        // every other mode, so the fast path below is unchanged.
        var doomed = new HashSet<HexCoord>();
        foreach (TideStep step in _state.PendingTide)
        {
            if (!step.DemoteOnly) doomed.Add(step.Coord);
        }

        foreach (HexCoord coord in _highlightedTerritory.Coords)
        {
            Vector2 center = FirstHexCenterOffset + HexPixel.ToPixel(coord, HexSize);

            // A doomed tile's edges go into their own group so one pulse tween
            // covers the whole ring; every other tile strokes straight onto the
            // layer (static). The group is added only if it actually has edges.
            bool isDoomed = doomed.Contains(coord);
            Node2D? group = isDoomed ? new Node2D() : null;
            Node2D parent = group ?? _highlightLayer;
            int edgesDrawn = 0;

            for (int edge = 0; edge < 6; edge++)
            {
                int dir = EdgeToNeighborDirection[edge];
                HexCoord neighborCoord = coord.Neighbor(dir);

                if (inside.Contains(neighborCoord)) continue;

                Vector2 a = center + verts[edge];
                Vector2 b = center + verts[(edge + 1) % 6];

                parent.AddChild(new Line2D
                {
                    Points = new[] { a, b },
                    Width = haloWidth,
                    DefaultColor = SelectionHaloColor,
                    Antialiased = true,
                    BeginCapMode = Line2D.LineCapMode.Round,
                    EndCapMode = Line2D.LineCapMode.Round,
                });
                parent.AddChild(new Line2D
                {
                    Points = new[] { a, b },
                    Width = coreWidth,
                    DefaultColor = SelectionCoreColor,
                    Antialiased = true,
                    BeginCapMode = Line2D.LineCapMode.Round,
                    EndCapMode = Line2D.LineCapMode.Round,
                });
                edgesDrawn++;
            }

            if (group != null)
            {
                if (edgesDrawn > 0)
                {
                    _highlightLayer.AddChild(group);
                    PulseDoomedHighlightGroup(group);
                    Log.Debug(Log.LogCategory.Render,
                        $"[highlight] doomed-tile pulse @{coord} ({edgesDrawn} edge(s))");
                }
                else
                {
                    group.Free();
                }
            }
        }
    }

    /// <summary>
    /// Loop a doomed tile's selection-outline group inversely to its tide
    /// telegraph (<see cref="DrawTideTelegraphTile"/>): fully drawn at the land
    /// trough, fading to <see cref="DoomedHighlightMinAlpha"/> at the flooded
    /// peak. Same Sine half-period, started from the visible/land state, so the
    /// ring stays in phase with the telegraph from turn-start focus.
    /// </summary>
    private void PulseDoomedHighlightGroup(Node2D group)
    {
        group.Modulate = new Color(1f, 1f, 1f, 1f);
        Tween pulse = group.CreateTween();
        pulse.SetLoops();
        pulse.TweenProperty(group, "modulate:a", DoomedHighlightMinAlpha, TideForecastPulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(group, "modulate:a", 1f, TideForecastPulseHalfPeriod)
            .SetTrans(Tween.TransitionType.Sine);
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

        // Shared equilateral-triangle + exclamation geometry (origin-relative;
        // the Node2D is positioned at badgePos). Matches the HUD bankrupt-toast
        // badge so the two read identically.
        (Vector2[] triangle, Vector2[] bar, Vector2 dotCenter, float dotRadius) =
            HudIcons.WarningBadgeGeometry(Vector2.Zero, HexSize * 0.45f);

        var badge = new Node2D { Position = badgePos };
        badge.AddChild(new Polygon2D { Polygon = triangle, Color = fill });
        // Border: closed Line2D around the triangle (Line2D has no
        // auto-close, so repeat the first vertex).
        badge.AddChild(new Line2D
        {
            Points = new[] { triangle[0], triangle[1], triangle[2], triangle[0] },
            Width = 2f,
            DefaultColor = accent,
        });

        // Exclamation glyph: vertical bar + dot below, both accent color.
        badge.AddChild(new Polygon2D { Polygon = bar, Color = accent });
        Polygon2D dot = CreateFilledDisc(dotRadius, accent);
        dot.Position = dotCenter;
        badge.AddChild(dot);

        _warningBadgesLayer!.AddChild(badge);
    }
}
