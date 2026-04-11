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

    // Occupant icon fills: white = "this is actionable for you right now",
    // black = "dormant / not actionable". The border is always black.
    private static readonly Color CtaFill = new Color(1f, 1f, 1f, 1f);
    private static readonly Color NonCtaFill = new Color(0f, 0f, 0f, 1f);

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
    // fills -> borders -> capitals -> units -> targets -> highlight).
    private Node2D? _bordersLayer;
    private Node2D? _capitalsLayer;
    private Node2D? _unitsLayer;
    private Node2D? _targetsLayer;
    private Node2D? _highlightLayer;
    private readonly Dictionary<HexCoord, Node2D> _unitVisuals = new();

    // The territory currently drawn as highlighted. Pure view state — the
    // single source of truth lives in SessionState, but we cache it here
    // so RedrawHighlight doesn't need to take a parameter.
    private Territory? _highlightedTerritory;

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

        _bordersLayer = new Node2D { Name = "BordersLayer" };
        AddChild(_bordersLayer);
        _capitalsLayer = new Node2D { Name = "CapitalsLayer" };
        AddChild(_capitalsLayer);
        _unitsLayer = new Node2D { Name = "UnitsLayer" };
        AddChild(_unitsLayer);
        _targetsLayer = new Node2D { Name = "TargetsLayer" };
        AddChild(_targetsLayer);
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

        foreach (HexCoord coord in coords)
        {
            Vector2 center = FirstHexCenterOffset + coord.ToPixel(HexSize);

            float radius = HexSize * 0.55f;
            const int segments = 20;
            var points = new Vector2[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Tau * i / segments;
                points[i] = center + new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
            }

            var ring = new Line2D
            {
                Points = points,
                Width = 4f,
                DefaultColor = new Color(0.2f, 1f, 0.3f, 0.9f),
            };
            _targetsLayer.AddChild(ring);
        }
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
        ClearLayer(_unitsLayer);
        ClearLayer(_capitalsLayer);
        _unitVisuals.Clear();

        var actionableCapitals = new HashSet<HexCoord>();
        if (currentPlayerColor.HasValue)
        {
            foreach (Territory territory in Territories)
            {
                if (territory.Owner != currentPlayerColor.Value) continue;
                if (!territory.HasCapital) continue;
                if (PurchaseRules.CanAffordPeasant(territory, treasury))
                {
                    actionableCapitals.Add(territory.Capital!.Value);
                }
            }
        }

        foreach (HexTile tile in Grid.Tiles)
        {
            if (tile.Occupant == null) continue;

            Vector2 center = FirstHexCenterOffset + tile.Coord.ToPixel(HexSize);

            if (tile.Occupant is Unit unit)
            {
                bool cta = currentPlayerColor.HasValue
                    && unit.Owner == currentPlayerColor.Value
                    && !unit.HasMovedThisTurn;
                Color interior = cta ? CtaFill : NonCtaFill;
                Node2D visual = CreateUnitVisual(interior, unit.Level);
                visual.Position = center;
                _unitsLayer?.AddChild(visual);
                _unitVisuals[tile.Coord] = visual;
            }
            else if (tile.Occupant is Capital)
            {
                bool cta = actionableCapitals.Contains(tile.Coord);
                Color interior = cta ? CtaFill : NonCtaFill;
                Node2D visual = CreateCapitalVisual(interior);
                visual.Position = center;
                _capitalsLayer?.AddChild(visual);
            }
            else if (tile.Occupant is Grave)
            {
                Node2D visual = CreateGraveVisual();
                visual.Position = center;
                _unitsLayer?.AddChild(visual);
            }
        }
    }

    private Node2D CreateUnitVisual(Color interiorColor, UnitLevel level)
    {
        float radius = HexSize * 0.22f;
        const int segments = 16;
        var verts = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            verts[i] = new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
        }

        var body = new Polygon2D
        {
            Color = interiorColor,
            Polygon = verts,
        };

        var outlinePoints = new Vector2[segments + 1];
        for (int i = 0; i < segments; i++) outlinePoints[i] = verts[i];
        outlinePoints[segments] = verts[0];

        var outline = new Line2D
        {
            Points = outlinePoints,
            Width = 2f,
            DefaultColor = new Color(0f, 0f, 0f, 1f),
        };
        body.AddChild(outline);

        // Level decoration: spear for spearman, sword for knight, chevron
        // for baron. Peasant has no decoration. Drawn in the opposite of
        // the interior color so the icon shows on both CTA (white) and
        // non-CTA (black) discs.
        Color iconColor = new Color(1f - interiorColor.R, 1f - interiorColor.G, 1f - interiorColor.B);
        Node? icon = level switch
        {
            UnitLevel.Spearman => CreateSpearIcon(radius, iconColor),
            UnitLevel.Knight => CreateSwordIcon(radius, iconColor),
            UnitLevel.Baron => CreateChevronIcon(radius, iconColor),
            _ => null,
        };
        if (icon != null)
        {
            body.AddChild(icon);
        }

        return body;
    }

    private static Node2D CreateSpearIcon(float r, Color color)
    {
        // Vertical shaft from top to bottom of the disc.
        return new Line2D
        {
            Points = new[] { new Vector2(0f, -r * 0.7f), new Vector2(0f, r * 0.7f) },
            Width = 3f,
            DefaultColor = color,
        };
    }

    private static Node2D CreateSwordIcon(float r, Color color)
    {
        // Vertical shaft + short horizontal crossguard near the top.
        var node = new Node2D();
        node.AddChild(new Line2D
        {
            Points = new[] { new Vector2(0f, -r * 0.7f), new Vector2(0f, r * 0.7f) },
            Width = 3f,
            DefaultColor = color,
        });
        node.AddChild(new Line2D
        {
            Points = new[] { new Vector2(-r * 0.5f, -r * 0.35f), new Vector2(r * 0.5f, -r * 0.35f) },
            Width = 3f,
            DefaultColor = color,
        });
        return node;
    }

    private Node2D CreateGraveVisual()
    {
        // Symmetrical grey plus sign (cross) — symmetric in both axes.
        // Visually distinct from units (circles) and capitals (diamonds).
        // Drawn 25% larger than the unit disc for legibility.
        float r = HexSize * 0.275f;
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

    private static Node2D CreateChevronIcon(float r, Color color)
    {
        // Inverted V pointing up, like a rank stripe.
        return new Line2D
        {
            Points = new[]
            {
                new Vector2(-r * 0.6f, r * 0.3f),
                new Vector2(0f, -r * 0.5f),
                new Vector2(r * 0.6f, r * 0.3f),
            },
            Width = 3f,
            DefaultColor = color,
        };
    }

    private Node2D CreateCapitalVisual(Color interiorColor)
    {
        float r = HexSize * 0.35f;
        var verts = new[]
        {
            new Vector2(0f, -r),
            new Vector2(r, 0f),
            new Vector2(0f, r),
            new Vector2(-r, 0f),
        };

        var diamond = new Polygon2D
        {
            Color = interiorColor,
            Polygon = verts,
        };

        var outline = new Line2D
        {
            Points = new[] { verts[0], verts[1], verts[2], verts[3], verts[0] },
            Width = 2f,
            DefaultColor = new Color(0f, 0f, 0f, 1f),
        };
        diamond.AddChild(outline);

        return diamond;
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
