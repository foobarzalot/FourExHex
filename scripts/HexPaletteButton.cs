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

    private readonly Color _fillColor;
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

    public HexPaletteButton(Color fillColor, HexPaletteIcon icon = HexPaletteIcon.None)
    {
        _fillColor = fillColor;
        _icon = icon;
        CustomMinimumSize = new Vector2(36, 40);
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
            case HexPaletteIcon.Tree: DrawTreeIcon(center, radius); break;
            case HexPaletteIcon.Capital: DrawCapitalIcon(center, radius); break;
            case HexPaletteIcon.Tower: DrawTowerIcon(center, radius); break;
            case HexPaletteIcon.Hand: DrawHandIcon(center, radius); break;
        }

        Color outlineColor = _isSelected ? new Color(1f, 1f, 1f) : new Color(0f, 0f, 0f);
        float outlineWidth = _isSelected ? 3f : 1f;
        for (int i = 0; i < 6; i++)
        {
            DrawLine(verts[i], verts[(i + 1) % 6], outlineColor, outlineWidth);
        }
    }

    private void DrawTreeIcon(Vector2 center, float hexRadius)
    {
        // Stylized conifer scaled to fit inside the hex. Mirrors the colors
        // and proportions of HexMapView.CreateTreeVisual so the button's
        // tree reads as the same thing the user will see on the map.
        float r = hexRadius * 0.65f;
        var canopy = new Vector2[]
        {
            center + new Vector2(0f, -r),
            center + new Vector2(r * 0.85f, r * 0.4f),
            center + new Vector2(-r * 0.85f, r * 0.4f),
        };
        DrawColoredPolygon(canopy, new Color(0.16f, 0.48f, 0.18f, 1f));
        for (int i = 0; i < 3; i++)
        {
            DrawLine(canopy[i], canopy[(i + 1) % 3], new Color(0f, 0f, 0f, 1f), 1.5f);
        }

        float tw = r * 0.18f;
        float ttop = r * 0.4f;
        float tbot = r * 0.75f;
        var trunk = new Vector2[]
        {
            center + new Vector2(-tw, ttop),
            center + new Vector2(tw, ttop),
            center + new Vector2(tw, tbot),
            center + new Vector2(-tw, tbot),
        };
        DrawColoredPolygon(trunk, new Color(0.36f, 0.22f, 0.1f, 1f));
        // Black outline so the trunk separates from the earth-brown
        // swatch background (the trunk fill is intentionally close to
        // it, mirroring the map's tree visual).
        for (int i = 0; i < 4; i++)
        {
            DrawLine(trunk[i], trunk[(i + 1) % 4], new Color(0f, 0f, 0f, 1f), 1.5f);
        }
    }

    private void DrawCapitalIcon(Vector2 center, float hexRadius)
    {
        // Five-point star matching HexMapView.CreateCapitalVisual's
        // shape. Gold fill (capital = royalty) reads cleanly against
        // the deep slate-violet swatch background; a black outline
        // crisps the silhouette regardless of background.
        float outer = hexRadius * 0.65f;
        float inner = outer * 0.4f;
        var verts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = -Mathf.Pi / 2f + i * Mathf.Pi / 5f;
            float r = (i % 2 == 0) ? outer : inner;
            verts[i] = center + new Vector2(r * Mathf.Cos(angle), r * Mathf.Sin(angle));
        }
        DrawColoredPolygon(verts, new Color(0.97f, 0.80f, 0.22f, 1f));
        for (int i = 0; i < 10; i++)
        {
            DrawLine(verts[i], verts[(i + 1) % 10], new Color(0f, 0f, 0f, 1f), 1.5f);
        }
    }

    private void DrawHandIcon(Vector2 center, float hexRadius)
    {
        // Stylized open palm: a rounded palm rectangle, four fingers
        // sprouting up, and a thumb angled out to the left. Filled in a
        // neutral skin-tone with a black outline so it reads against any
        // hex background. Composed of simple rectangles to keep the
        // shape recognizable at the 36×40 button size.
        Color fill = new Color(0.93f, 0.78f, 0.62f, 1f);
        Color outline = new Color(0f, 0f, 0f, 1f);
        float r = hexRadius * 0.55f;

        float palmHalfW = r * 0.55f;
        float palmTop = -r * 0.05f;
        float palmBot = r * 0.85f;
        Vector2[] palm =
        {
            center + new Vector2(-palmHalfW, palmTop),
            center + new Vector2(palmHalfW, palmTop),
            center + new Vector2(palmHalfW, palmBot),
            center + new Vector2(-palmHalfW, palmBot),
        };
        DrawColoredPolygon(palm, fill);
        for (int i = 0; i < 4; i++)
        {
            DrawLine(palm[i], palm[(i + 1) % 4], outline, 1.5f);
        }

        float fingerW = r * 0.22f;
        float fingerTop = -r * 0.95f;
        float fingerBot = palmTop + r * 0.02f;
        float[] fingerCenters =
        {
            -palmHalfW + fingerW * 0.6f,
            -fingerW * 0.6f,
            fingerW * 0.6f,
            palmHalfW - fingerW * 0.6f,
        };
        foreach (float fx in fingerCenters)
        {
            Vector2[] finger =
            {
                center + new Vector2(fx - fingerW * 0.5f, fingerTop),
                center + new Vector2(fx + fingerW * 0.5f, fingerTop),
                center + new Vector2(fx + fingerW * 0.5f, fingerBot),
                center + new Vector2(fx - fingerW * 0.5f, fingerBot),
            };
            DrawColoredPolygon(finger, fill);
            for (int i = 0; i < 4; i++)
            {
                DrawLine(finger[i], finger[(i + 1) % 4], outline, 1.5f);
            }
        }

        // Thumb: an angled rounded-rectangle that juts out the left side
        // of the palm at roughly 30° below horizontal. Built as a quad
        // by extruding a short segment in a perpendicular direction.
        Vector2 thumbBase = center + new Vector2(-palmHalfW, palmTop + r * 0.25f);
        Vector2 thumbTip = thumbBase + new Vector2(-r * 0.55f, -r * 0.32f);
        Vector2 thumbDir = (thumbTip - thumbBase).Normalized();
        Vector2 thumbPerp = new Vector2(-thumbDir.Y, thumbDir.X) * (r * 0.18f);
        Vector2[] thumb =
        {
            thumbBase + thumbPerp,
            thumbTip + thumbPerp,
            thumbTip - thumbPerp,
            thumbBase - thumbPerp,
        };
        DrawColoredPolygon(thumb, fill);
        for (int i = 0; i < 4; i++)
        {
            DrawLine(thumb[i], thumb[(i + 1) % 4], outline, 1.5f);
        }
    }

    private void DrawTowerIcon(Vector2 center, float hexRadius)
    {
        // Stylized crenellated rook, scaled down from
        // HexMapView.TowerShapeVertices's proportions to fit the button.
        float r = hexRadius * 0.55f;
        float halfW = r;
        float top = -r;
        float bot = r * 0.85f;
        float merlonH = r * 0.35f;
        float merlonW = halfW * 0.4f;
        var verts = new Vector2[]
        {
            center + new Vector2(-halfW, bot),
            center + new Vector2(-halfW, top + merlonH),
            center + new Vector2(-halfW, top),
            center + new Vector2(-halfW + merlonW, top),
            center + new Vector2(-halfW + merlonW, top + merlonH),
            center + new Vector2(-merlonW * 0.5f, top + merlonH),
            center + new Vector2(-merlonW * 0.5f, top),
            center + new Vector2(merlonW * 0.5f, top),
            center + new Vector2(merlonW * 0.5f, top + merlonH),
            center + new Vector2(halfW - merlonW, top + merlonH),
            center + new Vector2(halfW - merlonW, top),
            center + new Vector2(halfW, top),
            center + new Vector2(halfW, top + merlonH),
            center + new Vector2(halfW, bot),
        };
        DrawColoredPolygon(verts, new Color(0.72f, 0.72f, 0.76f, 1f));
        for (int i = 0; i < verts.Length; i++)
        {
            DrawLine(verts[i], verts[(i + 1) % verts.Length], new Color(0f, 0f, 0f, 1f), 1.5f);
        }
    }
}
