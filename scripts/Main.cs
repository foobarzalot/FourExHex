using Godot;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        var map = new HexMap();
        AddChild(map);

        // Center the map in the viewport.
        Vector2 viewport = GetViewportRect().Size;
        map.Position = (viewport - map.PixelSize) * 0.5f;
    }
}
