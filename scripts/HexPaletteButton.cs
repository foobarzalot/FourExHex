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
    Gold,
    Mountain,
}

public partial class HexPaletteButton : Control
{
    public event Action<HexPaletteButton>? Pressed;

    private Color _fillColor;
    private readonly HexPaletteIcon _icon;
    private readonly bool _squared;
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

    private bool _isHuman;

    /// <summary>When true, the swatch draws a small white pip (issue #70) so the
    /// map editor can show at a glance which player colors are human-controlled.
    /// Only meaningful on the non-squared land swatches.</summary>
    public bool IsHuman
    {
        get => _isHuman;
        set
        {
            if (_isHuman == value) return;
            _isHuman = value;
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

    public HexPaletteButton(Color fillColor, HexPaletteIcon icon = HexPaletteIcon.None, bool squared = false)
    {
        _fillColor = fillColor;
        _icon = icon;
        _squared = squared;
        // All HexPaletteButton variants render at the HudIconButton scale
        // (68×68) so the hex inside (squared = inscribed in the slate
        // backdrop; non-squared = bare hex polygon) lands at the same
        // visual size whether the button is a tool (water) or a land swatch.
        CustomMinimumSize = new Vector2(68, 68);
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
        if (_squared)
        {
            DrawSquared();
            return;
        }

        // Pointy-top hex centered in our rect. Multiplier 0.808 = 0.85 *
        // 0.95 — tuned so the bare hex matches the inscribed hex of the
        // squared (water / land cycle) variant. Without this the same
        // 68×68 button would render a noticeably larger hex in non-
        // squared mode (height-limited at 0.92 of half-height).
        Vector2 center = Size * 0.5f;
        float maxByWidth = Size.X / Mathf.Sqrt(3f);
        float maxByHeight = Size.Y * 0.5f;
        float radius = Mathf.Min(maxByWidth, maxByHeight) * 0.808f;

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
            case HexPaletteIcon.Gold: HudIcons.DrawGold(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Mountain: HudIcons.DrawMountain(this, center, radius, Colors.White); break;
        }

        Color outlineColor = _isSelected ? new Color(1f, 1f, 1f) : new Color(0f, 0f, 0f);
        float outlineWidth = _isSelected ? 3f : 1f;
        for (int i = 0; i < 6; i++)
        {
            DrawLine(verts[i], verts[(i + 1) % 6], outlineColor, outlineWidth);
        }

        // Human marker (issue #70): a small white pip with a dark ring near the
        // top of the hex, so a human-controlled color reads at a glance.
        if (_isHuman)
        {
            float pipR = radius * 0.24f;
            Vector2 pip = center + new Vector2(0f, -radius * 0.52f);
            DrawCircle(pip, pipR, new Color(1f, 1f, 1f));
            DrawArc(pip, pipR, 0f, Mathf.Tau, 18, new Color(0f, 0f, 0f), 1.5f);
        }
    }

    /// <summary>Squared variant: rounded-square dark-slate backdrop with
    /// a black border (matches HudIconButton's chrome — the die button
    /// is the canonical example). Used for the editor's tool buttons
    /// (pan / water / tree / capital / tower) so they read as one
    /// family with the die.</summary>
    private void DrawSquared()
    {
        // Backdrop — same shape + colors as HudIconButton's base stylebox.
        var backdrop = new StyleBoxFlat
        {
            BgColor = UiPalette.BgPanel,
            BorderColor = new Color(0f, 0f, 0f, 1f),
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        };
        DrawStyleBox(backdrop, new Rect2(Vector2.Zero, Size));

        Vector2 center = Size * 0.5f;
        // Icon glyph radius — same proportion HudIconButton glyphs use
        // (~85% of the half-edge so a 68×68 button gets ~29 px radius).
        float radius = Mathf.Min(Size.X, Size.Y) * 0.5f * 0.85f;

        switch (_icon)
        {
            case HexPaletteIcon.Tree:    HudIcons.DrawTree(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Capital: HudIcons.DrawCapital(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Tower:   HudIcons.DrawTower(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Hand:    HudIcons.DrawHand(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Gold:    HudIcons.DrawGold(this, center, radius, Colors.White); break;
            case HexPaletteIcon.Mountain: HudIcons.DrawMountain(this, center, radius, Colors.White); break;
            case HexPaletteIcon.None:
                // Icon-less squared variant (water paint, land cycle):
                // inscribe a pointy-top hex polygon in the FillColor so
                // the button retains the "swatch" identity it had in hex
                // mode, just inside the rounded-square backdrop.
                float hexR = radius * 0.95f;
                var hexVerts = new Vector2[6];
                for (int i = 0; i < 6; i++)
                {
                    float angleRad = Mathf.Pi * 0.5f - i * Mathf.Pi / 3f;
                    hexVerts[i] = center + new Vector2(Mathf.Cos(angleRad), -Mathf.Sin(angleRad)) * hexR;
                }
                DrawColoredPolygon(hexVerts, _fillColor);
                break;
        }

        if (_isSelected)
        {
            // Cool-blue selection ring (D1 §6) layered on top of the
            // black border so the active brush is unmistakable.
            var ringStyle = new StyleBoxFlat
            {
                BgColor = new Color(0f, 0f, 0f, 0f),
                BorderColor = UiPalette.SelectionRing,
                BorderWidthLeft = 3, BorderWidthRight = 3,
                BorderWidthTop = 3, BorderWidthBottom = 3,
                CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
            };
            DrawStyleBox(ringStyle, new Rect2(Vector2.Zero, Size));
        }
    }
}
