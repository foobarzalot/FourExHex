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
    /// </summary>
    public event Action<HexTile?>? TileClicked;

    [Export] public int Cols { get; set; } = 18;
    [Export] public int Rows { get; set; } = 13;
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

    // Tile coord -> the territory that contains it. Rebuilt whenever
    // Territories is recomputed. Used for O(1) TerritoryAt lookups.
    private readonly Dictionary<HexCoord, Territory> _tileToTerritory = new();

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
    // fills -> borders -> capitals -> trees -> graves -> units -> deaths
    // -> targets -> highlight). Trees draw under units so the rare
    // unit-on-tree-tile transient (e.g. mid-chop) reads correctly.
    // Deaths draws above units so a freshly-spawned grave grow-in reads
    // underneath the still-shrinking corpse it replaces.
    private Node2D? _towerCoverageLayer;
    private Node2D? _bordersLayer;
    private Node2D? _capitalsLayer;
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
        // Tiles already exist in _state.Grid (populated by the controller
        // before AddChild). Create one Polygon2D fill per tile and link
        // it back to the tile so future recolors stay in sync via the
        // HexTile.Color setter.
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            Vector2 center = FirstHexCenterOffset + tile.Coord.ToPixel(HexSize);
            tile.Visual = CreateHexVisual(center, tile.Color);
            AddChild(tile.Visual);
        }

        GD.Print($"HexMapView: rendering {_state.Grid.Count} tiles across {_state.Territories.Count} territories.");

        RebuildTileToTerritoryIndex();

        // The tower-coverage tint sits above tile fills but below
        // borders, so the lift is subtle: the underlying territory
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
        RebuildTileToTerritoryIndex();
        ClearLayer(_bordersLayer);
        ClearLayer(_targetsLayer);
        DrawTerritoryBorders();

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
    /// green rings. Pass an empty list to clear.
    /// </summary>
    public void ShowMoveTargets(IEnumerable<HexCoord> coords)
    {
        ClearLayer(_targetsLayer);
        if (_targetsLayer == null) return;

        const float radius = 0.55f;
        const int segments = 20;
        var points = new Vector2[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = new Vector2(radius * HexSize * Mathf.Cos(angle), radius * HexSize * Mathf.Sin(angle));
        }

        foreach (HexCoord coord in coords)
        {
            // Points are around (0,0); ring's Position is the tile center
            // so a scale on the Line2D node pulses around the ring's
            // geometric center (same trick as units/capitals).
            var ring = new Line2D
            {
                Position = FirstHexCenterOffset + coord.ToPixel(HexSize),
                Points = points,
                Width = 4f,
                DefaultColor = new Color(0.2f, 1f, 0.3f, 0.9f),
            };
            _targetsLayer.AddChild(ring);
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
            preview.Position = FirstHexCenterOffset + coord.ToPixel(HexSize);
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
                Position = FirstHexCenterOffset + coord.ToPixel(HexSize),
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
    /// Look up the territory containing <paramref name="coord"/>, or null
    /// if the coord is outside the grid.
    /// </summary>
    public Territory? TerritoryAt(HexCoord coord) =>
        _tileToTerritory.TryGetValue(coord, out Territory? t) ? t : null;

    /// <summary>
    /// Rebuild every occupant visual (units + capitals) using the CTA
    /// coloring rules: the current player's actionable things get a
    /// white interior, everything else gets black. All shapes have a
    /// black border. Pass <paramref name="currentPlayerColor"/> = null to
    /// render everything non-CTA (e.g., while no turn is active).
    /// </summary>
    public void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury)
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
                _deathsLayer?.AddChild(corpse);
                StartShrinkAndFreeAnimation(corpse);
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
        if (currentPlayerColor.HasValue)
        {
            foreach (Territory territory in Territories)
            {
                if (territory.Owner != currentPlayerColor.Value) continue;
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
            if (graveVisual != null && _animateNewTrees && newTile?.Occupant is Tree)
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

            Vector2 center = FirstHexCenterOffset + tile.Coord.ToPixel(HexSize);

            if (tile.Occupant is Unit unit)
            {
                bool selected = _selectedUnit.HasValue && _selectedUnit.Value == tile.Coord;
                Node2D visual = CreateUnitVisual(selected, unit.Level);
                visual.Position = center;
                _unitsLayer?.AddChild(visual);
                _unitVisuals[tile.Coord] = visual;

                bool actionable = currentPlayerColor.HasValue
                    && unit.Owner == currentPlayerColor.Value
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
                    if (_animateNewGraves)
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
                    if (_animateNewTrees)
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
        // Sized like the grave (~0.275 * HexSize) so it reads clearly
        // against the tile and distinguishes from unit discs.
        float r = HexSize * 0.3f;
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

    private void RebuildTileToTerritoryIndex()
    {
        _tileToTerritory.Clear();
        foreach (KeyValuePair<HexCoord, Territory> kvp in Territories.BuildTileIndex())
        {
            _tileToTerritory[kvp.Key] = kvp.Value;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse) return;
        if (!mouse.Pressed || mouse.ButtonIndex != MouseButton.Left) return;

        // Convert viewport position into our local space, then remove the
        // centering offset so the result is in axial-origin coordinates.
        Vector2 local = ToLocal(mouse.Position) - FirstHexCenterOffset;
        HexCoord coord = HexCoord.FromPixel(local, HexSize);

        if (!Grid.Contains(coord))
        {
            TileClicked?.Invoke(null);
            return;
        }

        TileClicked?.Invoke(Grid.Get(coord));
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
        Vector2[] verts = HexVertices();

        var fill = new Polygon2D
        {
            Position = position,
            Color = color,
            Polygon = verts,
        };

        var outlinePoints = new Vector2[7];
        for (int i = 0; i < 6; i++) outlinePoints[i] = verts[i];
        outlinePoints[6] = verts[0];

        var outline = new Line2D
        {
            Points = outlinePoints,
            Width = 1.5f,
            DefaultColor = new Color(0f, 0f, 0f, 0.4f),
        };
        fill.AddChild(outline);

        return fill;
    }

    private void DrawTerritoryBorders()
    {
        if (_bordersLayer == null) return;
        Vector2[] verts = HexVertices();

        foreach (HexTile tile in Grid.Tiles)
        {
            Vector2 center = FirstHexCenterOffset + tile.Coord.ToPixel(HexSize);

            for (int edge = 0; edge < 6; edge++)
            {
                int dir = EdgeToNeighborDirection[edge];
                HexCoord neighborCoord = tile.Coord.Neighbor(dir);
                HexTile? neighbor = Grid.Get(neighborCoord);

                bool isBoundary = neighbor == null || neighbor.Color != tile.Color;
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
            Vector2 center = FirstHexCenterOffset + coord.ToPixel(HexSize);

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
