using System;
using System.Collections.Generic;
using Godot;

public partial class HexMap : Node2D
{
    /// <summary>
    /// Raised whenever the player left-clicks on the map. The argument is the
    /// tile they clicked, or null if the click was outside the grid. HexMap
    /// does NOT apply any "whose turn is it" or "placement mode" policy —
    /// it just reports the raw click; callers decide what to do with it.
    /// </summary>
    public event Action<HexTile?>? TileClicked;

    /// <summary>
    /// Raised whenever the selection actually changes (via code or input).
    /// The argument is the new selection, or null if selection was cleared.
    /// </summary>
    public event Action<Territory?>? SelectionChanged;

    [Export] public int Cols { get; set; } = 18;
    [Export] public int Rows { get; set; } = 13;
    [Export] public float HexSize { get; set; } = 48f;

    private static readonly Color[] Palette =
    {
        new Color("e53935"), // red
        new Color("1e88e5"), // blue
        new Color("43a047"), // green
        new Color("fdd835"), // yellow
        new Color("8e24aa"), // purple
        new Color("fb8c00"), // orange
    };

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

    public HexGrid Grid { get; } = new HexGrid();

    public IReadOnlyList<Territory> Territories { get; private set; } = new List<Territory>();

    // Tile coord -> the territory that contains it. Rebuilt whenever
    // Territories is recomputed. Used for O(1) selection lookups.
    private readonly Dictionary<HexCoord, Territory> _tileToTerritory = new();

    // Layered overlay children (added in this order so draw order is
    // fills -> borders -> capitals -> units -> targets -> highlight).
    private Node2D? _bordersLayer;
    private Node2D? _capitalsLayer;
    private Node2D? _unitsLayer;
    private Node2D? _targetsLayer;
    private Node2D? _highlightLayer;
    private readonly Dictionary<HexCoord, Node2D> _unitVisuals = new();

    // Null when nothing is selected.
    private Territory? _selected;

    /// <summary>Pixel bounding box of the rendered grid, for centering.</summary>
    public Vector2 PixelSize => new Vector2(
        (Cols + 0.5f) * Mathf.Sqrt(3f) * HexSize,
        (1.5f * Rows + 0.5f) * HexSize);

    // The first hex (axial 0,0) is drawn at this offset from the HexMap origin
    // so that the grid's visual bounding box starts at (0,0) in local space.
    private Vector2 FirstHexCenterOffset => new Vector2(
        0.5f * Mathf.Sqrt(3f) * HexSize,
        HexSize);

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                HexCoord coord = HexCoord.FromOffset(col, row);
                Color color = Palette[rng.RandiRange(0, Palette.Length - 1)];
                var tile = new HexTile(coord, color);

                Vector2 center = FirstHexCenterOffset + coord.ToPixel(HexSize);
                tile.Visual = CreateHexVisual(center, color);
                AddChild(tile.Visual);

                Grid.Add(tile);
            }
        }

        // Flood-fill + capital placement (which adds Capital occupants to
        // the grid at each multi-hex territory's chosen tile).
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(Grid);
        Territories = CapitalReconciler.Reconcile(raw, new List<Territory>(), Grid);
        GD.Print($"HexMap: {Grid.Count} tiles partitioned into {Territories.Count} territories.");

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
        // Occupant visuals are drawn by Main via RefreshOccupantVisuals
        // once it knows the current player and treasury. We don't draw
        // them here because they'd all be non-CTA by default and Main
        // would immediately overwrite them.
    }

    /// <summary>
    /// Restore full game state from <paramref name="snapshot"/>, rebuild
    /// visuals to match, and clear any selection. Used by undo.
    /// </summary>
    public void RestoreFromSnapshot(GameStateSnapshot snapshot, Treasury treasury)
    {
        Territories = snapshot.ApplyTo(Grid, treasury);
        RebuildTileToTerritoryIndex();

        ClearLayer(_bordersLayer);
        ClearLayer(_targetsLayer);
        DrawTerritoryBorders();

        // Occupant visuals will be redrawn by the caller via
        // RefreshOccupantVisuals (it needs the current player color).
        SelectTerritory(null);
    }

    /// <summary>
    /// Re-run flood-fill after the grid's tile colors have changed (e.g.,
    /// after a capture), rebuild the tile-to-territory index, and redraw
    /// the borders/capitals. Returns the previous territory list so the
    /// caller can reconcile the treasury.
    /// </summary>
    public IReadOnlyList<Territory> RecomputeTerritoriesAfterCapture()
    {
        IReadOnlyList<Territory> previous = Territories;
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(Grid);
        // Reconcile capitals: keep inherited, demote losers in merges,
        // place fresh ones for split pieces without an old capital. May
        // stomp units if a split piece has no empty tile.
        Territories = CapitalReconciler.Reconcile(raw, previous, Grid);
        RebuildTileToTerritoryIndex();

        _selected = null;
        ClearLayer(_highlightLayer);
        ClearLayer(_targetsLayer);
        ClearLayer(_bordersLayer);
        DrawTerritoryBorders();

        // Occupant visuals will be redrawn by the caller via
        // RefreshOccupantVisuals (it needs the current player color).
        return previous;
    }

    /// <summary>
    /// Reset HasMovedThisTurn on every unit owned by <paramref name="player"/>.
    /// Called at the start of that player's turn.
    /// </summary>
    public void ResetMovementFor(Player player)
    {
        foreach (HexTile tile in Grid.Tiles)
        {
            if (tile.Unit != null && tile.Unit.Owner == player.Color)
            {
                tile.Unit.HasMovedThisTurn = false;
            }
        }
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
    /// Look up the territory containing <paramref name="coord"/>, or null if
    /// the coord is outside the grid.
    /// </summary>
    public Territory? TerritoryAt(HexCoord coord) =>
        _tileToTerritory.TryGetValue(coord, out Territory? t) ? t : null;

    /// <summary>
    /// Rebuild every occupant visual (units + capitals) using the CTA
    /// coloring rules: the current player's actionable things get a
    /// player-color interior, everything else gets a black interior. All
    /// shapes have a black border. Pass <paramref name="currentPlayerColor"/>
    /// = null to render everything non-CTA (e.g., while no turn is active).
    /// </summary>
    public void RefreshOccupantVisuals(Color? currentPlayerColor, Treasury treasury)
    {
        // Wipe the layers and rebuild from scratch. Cheap for 234 tiles.
        ClearLayer(_unitsLayer);
        ClearLayer(_capitalsLayer);
        _unitVisuals.Clear();

        // Precompute the set of capital coords belonging to the current
        // player that can afford a peasant, for the per-tile draw pass.
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
                Node2D visual = CreateUnitVisual(interior);
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
        }
    }

    private Node2D CreateUnitVisual(Color interiorColor)
    {
        // Filled circle with a black border. Interior is either the player
        // color (CTA: "this unit can still move") or black (non-CTA).
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

        return body;
    }

    private Node2D CreateCapitalVisual(Color interiorColor)
    {
        // Diamond glyph with a black border. Interior is either the player
        // color (CTA: treasury has enough for a peasant) or black (non-CTA).
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
            Points = new[]
            {
                verts[0], verts[1], verts[2], verts[3], verts[0],
            },
            Width = 2f,
            DefaultColor = new Color(0f, 0f, 0f, 1f),
        };
        diamond.AddChild(outline);

        return diamond;
    }

    private void RebuildTileToTerritoryIndex()
    {
        _tileToTerritory.Clear();
        foreach (Territory territory in Territories)
        {
            foreach (HexCoord coord in territory.Coords)
            {
                _tileToTerritory[coord] = territory;
            }
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

    /// <summary>
    /// Make <paramref name="territory"/> the currently selected territory
    /// (or pass null to clear). Updates the highlight outline and raises
    /// <see cref="SelectionChanged"/>.
    /// </summary>
    public void SelectTerritory(Territory? territory)
    {
        _selected = territory;
        RedrawHighlight();
        SelectionChanged?.Invoke(territory);
    }

    private void RedrawHighlight()
    {
        if (_highlightLayer == null) return;

        foreach (Node child in _highlightLayer.GetChildren())
        {
            child.QueueFree();
        }

        if (_selected == null) return;

        Vector2[] verts = HexVertices();
        var inside = new HashSet<HexCoord>(_selected.Coords);

        foreach (HexCoord coord in _selected.Coords)
        {
            Vector2 center = FirstHexCenterOffset + coord.ToPixel(HexSize);

            for (int edge = 0; edge < 6; edge++)
            {
                int dir = EdgeToNeighborDirection[edge];
                HexCoord neighborCoord = coord.Neighbor(dir);

                // The perimeter of the selected territory is the edges where
                // the neighbor is not itself in the selected territory.
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

                // Draw a thick border when the neighbor doesn't exist (grid
                // edge) or belongs to a different color (territory edge).
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
}
