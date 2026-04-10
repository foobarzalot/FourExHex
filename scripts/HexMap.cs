using Godot;

public partial class HexMap : Node2D
{
    [Export] public int Cols { get; set; } = 18;
    [Export] public int Rows { get; set; } = 13;
    [Export] public float HexSize { get; set; } = 48f;

    private static readonly Color[] Palette =
    {
        new Color("e74c3c"), // red
        new Color("3498db"), // blue
        new Color("2ecc71"), // green
        new Color("f1c40f"), // yellow
        new Color("9b59b6"), // purple
        new Color("1abc9c"), // teal
    };

    public HexGrid Grid { get; } = new HexGrid();

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
    }

    private Polygon2D CreateHexVisual(Vector2 position, Color color)
    {
        var verts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Pi / 180f * (60f * i - 30f);
            verts[i] = new Vector2(HexSize * Mathf.Cos(angle), HexSize * Mathf.Sin(angle));
        }

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
}
