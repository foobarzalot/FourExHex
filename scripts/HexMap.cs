using Godot;

public partial class HexMap : Node2D
{
    [Export] public int Cols { get; set; } = 18;
    [Export] public int Rows { get; set; } = 13;
    [Export] public float HexSize { get; set; } = 48f;

    // Pixel dimensions of the full grid, useful for centering in the viewport.
    public Vector2 PixelSize => new Vector2(
        (Cols + 0.5f) * Mathf.Sqrt(3f) * HexSize,
        (1.5f * Rows + 0.5f) * HexSize);

    // Offset from this node's Position to where the first hex's CENTER should be,
    // so that the grid's bounding box starts at (0, 0) in local space.
    public Vector2 FirstHexCenterOffset => new Vector2(
        0.5f * Mathf.Sqrt(3f) * HexSize,
        HexSize);

    private static readonly Color[] Palette =
    {
        new Color("e74c3c"), // red
        new Color("3498db"), // blue
        new Color("2ecc71"), // green
        new Color("f1c40f"), // yellow
        new Color("9b59b6"), // purple
        new Color("1abc9c"), // teal
    };

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                Color color = Palette[rng.RandiRange(0, Palette.Length - 1)];
                AddChild(CreateHex(col, row, color));
            }
        }
    }

    private Node2D CreateHex(int col, int row, Color color)
    {
        // Pointy-top hex layout with odd-row horizontal offset.
        float width = Mathf.Sqrt(3f) * HexSize;
        float height = 2f * HexSize;
        float x = FirstHexCenterOffset.X + (col + 0.5f * (row & 1)) * width;
        float y = FirstHexCenterOffset.Y + row * height * 0.75f;

        var verts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Pi / 180f * (60f * i - 30f);
            verts[i] = new Vector2(HexSize * Mathf.Cos(angle), HexSize * Mathf.Sin(angle));
        }

        var fill = new Polygon2D
        {
            Position = new Vector2(x, y),
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
