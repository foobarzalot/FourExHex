using System;
using Godot;

/// <summary>
/// Hex-shaped clickable swatch for the map editor's tile palette. Renders
/// a single pointy-top hex polygon at its fill color, with an outline that
/// thickens and turns white when <see cref="IsSelected"/> is true so the
/// active palette entry is obvious at a glance.
///
/// Pure view-layer Control: no game-state coupling, fires
/// <see cref="Pressed"/> on left-click and otherwise just paints itself.
/// Hit-testing uses the bounding rect (Control default), which is good
/// enough at the 36x40 size we use in the HUD — the inside of the rect
/// never extends much past the hex itself.
/// </summary>
public enum HexPaletteIcon
{
    None,
    Tree,
    Capital,
    Tower,
    Hand,
}

public partial class HexPaletteButton : Control
{
    public event Action<HexPaletteButton>? Pressed;

    private Color _fillColor;
    private readonly HexPaletteIcon _icon;
    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            QueueRedraw();
        }
    }

    /// <summary>
    /// The hex fill color. Settable so a single button can repaint as it
    /// cycles through colors (the map editor's collapsed land swatch).
    /// </summary>
    public Color FillColor
    {
        get => _fillColor;
        set
        {
            if (_fillColor == value) return;
            _fillColor = value;
            QueueRedraw();
        }
    }

    public HexPaletteButton(Color fillColor, HexPaletteIcon icon = HexPaletteIcon.None)
    {
        _fillColor = fillColor;
        _icon = icon;
        CustomMinimumSize = new Vector2(52, 56);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouse) return;
        if (!mouse.Pressed || mouse.ButtonIndex != MouseButton.Left) return;
        Pressed?.Invoke(this);
        AcceptEvent();
    }

    public override void _Draw()
    {
        // Pointy-top hex centered in our rect. Circumradius is the largest
        // value that keeps both the horizontal width (radius * sqrt(3))
        // and the vertical height (radius * 2) inside our box, with a
        // small inset so the selected outline isn't clipped.
        Vector2 center = Size * 0.5f;
        float maxByWidth = Size.X / Mathf.Sqrt(3f);
        float maxByHeight = Size.Y * 0.5f;
        float radius = Mathf.Min(maxByWidth, maxByHeight) * 0.92f;

        var verts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            // Start at top vertex (90°) and step CW around the hex by 60°.
            float angleRad = Mathf.Pi * 0.5f - i * Mathf.Pi / 3f;
            verts[i] = center + new Vector2(Mathf.Cos(angleRad), -Mathf.Sin(angleRad)) * radius;
        }

        DrawColoredPolygon(verts, _fillColor);

        switch (_icon)
        {
            case HexPaletteIcon.Tree: HudIcons.DrawTree(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Capital: HudIcons.DrawCapital(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Tower: HudIcons.DrawTower(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Hand: HudIcons.DrawHand(this, center, radius, Colors.White); break;
        }

        Color outlineColor = _isSelected ? new Color(1f, 1f, 1f) : new Color(0f, 0f, 0f);
        float outlineWidth = _isSelected ? 3f : 1f;
        for (int i = 0; i < 6; i++)
        {
            DrawLine(verts[i], verts[(i + 1) % 6], outlineColor, outlineWidth);
        }
    }
}
