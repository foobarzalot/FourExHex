using System.Collections.Generic;
using Godot;

public partial class HexMap : Node2D
{
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

    // A child Node2D that sits above the map/borders and holds the bright
    // outline drawn around the currently selected territory.
    private Node2D? _highlightLayer;

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

        Territories = TerritoryFinder.FindAll(Grid);
        GD.Print($"HexMap: {Grid.Count} tiles partitioned into {Territories.Count} territories.");

        RebuildTileToTerritoryIndex();
        DrawTerritoryBorders();
        DrawCapitals();

        // Highlight layer is added last so it draws on top of tiles + borders.
        _highlightLayer = new Node2D { Name = "HighlightLayer" };
        AddChild(_highlightLayer);
    }

    private void DrawCapitals()
    {
        foreach (Territory territory in Territories)
        {
            if (!territory.HasCapital) continue;

            Vector2 center = FirstHexCenterOffset + territory.Capital!.Value.ToPixel(HexSize);

            // Simple glyph: a white outlined diamond (rotated square) inside
            // the hex, sized to about 1/3 of the hex radius.
            float r = HexSize * 0.35f;
            var diamond = new Polygon2D
            {
                Position = center,
                Color = new Color(1f, 1f, 1f, 1f),
                Polygon = new[]
                {
                    new Vector2(0f, -r),
                    new Vector2(r, 0f),
                    new Vector2(0f, r),
                    new Vector2(-r, 0f),
                },
            };

            var outline = new Line2D
            {
                Points = new[]
                {
                    new Vector2(0f, -r),
                    new Vector2(r, 0f),
                    new Vector2(0f, r),
                    new Vector2(-r, 0f),
                    new Vector2(0f, -r),
                },
                Width = 2f,
                DefaultColor = new Color(0f, 0f, 0f, 1f),
            };
            diamond.AddChild(outline);

            AddChild(diamond);
        }
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
            SelectTerritory(null);
            return;
        }

        if (_tileToTerritory.TryGetValue(coord, out Territory? territory))
        {
            SelectTerritory(territory);
        }
    }

    private void SelectTerritory(Territory? territory)
    {
        _selected = territory;
        RedrawHighlight();
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
                AddChild(line);
            }
        }
    }
}
