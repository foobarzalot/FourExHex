using Godot;

/// <summary>
/// Floating, cursor-anchored tooltip used by the map editor. Shows the
/// hovered hex's lex index plus its (col, row) offset coords. Behaves
/// like a classic OS tooltip: appears after the cursor has dwelled on
/// a hex for <see cref="DwellMsec"/> ms, hides immediately on any
/// motion.
///
/// "Lex index" = <c>row * cols + col</c>, the row-major numbering matching
/// <see cref="HexCoord.CompareTo"/>'s lex-min ordering — a single int that
/// uniquely identifies a cell on a known-size map.
/// </summary>
public partial class HexHoverTooltip : CanvasLayer
{
    private const int FontSize = 32;
    private const float CursorOffsetX = 16f;
    private const float CursorOffsetY = 16f;

    /// <summary>
    /// Milliseconds the cursor must sit still on a hex before the
    /// tooltip appears.
    /// </summary>
    private const ulong DwellMsec = 500UL;

    private Label _label = null!;
    private Control _sensor = null!;

    // Coord under the cursor as of the most recent NotifyHover call,
    // and the cols value to use when formatting the lex index. Null
    // means "cursor is off-grid; nothing to show".
    private HexCoord? _pendingCoord;
    private int _pendingCols;
    // Tick (ms since engine start) of the most recent motion event.
    // The tooltip becomes eligible to show once
    // (now - _lastMotionMsec) >= DwellMsec.
    private ulong _lastMotionMsec;
    private bool _shown;

    public override void _Ready()
    {
        // Sit in the default canvas (layer 0) so RecordPane's right-strip
        // and bottom-timeline Controls — also at layer 0 (Control direct
        // child of Node2D) — block the sensor and trigger MouseExited.
        // CanvasLayer chrome (HudView, TutorialBuilderTopBar — both at
        // the default Layer = 1) still naturally overrides this layer 0,
        // so HUD-strip blocking continues to work.
        Layer = 0;

        _label = new Label
        {
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", FontSize);

        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.78f),
            ContentMarginLeft = 8f,
            ContentMarginRight = 8f,
            ContentMarginTop = 4f,
            ContentMarginBottom = 4f,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };
        _label.AddThemeStyleboxOverride("normal", bg);
        AddChild(_label);

        // Viewport-sized sensor that fires MouseExited when chrome
        // (with MouseFilter=Stop, in CanvasLayers added later in the
        // scene tree — i.e., on top of this CanvasLayer) captures
        // the cursor. The sensor's own MouseFilter=Pass lets motion
        // events propagate to HexMapView's _UnhandledInput. This is
        // the natural-Godot-input-chain way to hide the tooltip when
        // the cursor moves over chrome — no polling, no per-chrome
        // wiring, no GuiGetHoveredControl querying.
        _sensor = new Control
        {
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _sensor.MouseExited += OnSensorExited;
        AddChild(_sensor);
    }

    private void OnSensorExited()
    {
        // Cursor left our viewport-sized sensor — chrome (or off-screen)
        // is now over the cursor. Hide the tooltip and reset dwell state
        // so a fresh dwell starts when the cursor returns to a tile.
        if (_shown)
        {
            _label.Visible = false;
            _shown = false;
        }
        _pendingCoord = null;
    }

    public override void _Process(double delta)
    {
        // Always check whether any other (Stop-filter) Control sits
        // under the cursor — if so, hide the tooltip. The sensor's
        // MouseExited signal alone misses transitions to chrome on a
        // higher CanvasLayer (the editor toolbar at default Layer 1
        // doesn't reliably wake the sensor at Layer 0), so we poll the
        // viewport's "currently-hovered Control" each tick.
        Control? hovered = GetViewport().GuiGetHoveredControl();
        bool overChrome = hovered != null && hovered != _sensor;
        if (overChrome)
        {
            if (_shown)
            {
                _label.Visible = false;
                _shown = false;
            }
            _pendingCoord = null;
            return;
        }

        if (_shown) return;
        if (_pendingCoord is null) return;
        if (Time.GetTicksMsec() - _lastMotionMsec < DwellMsec) return;

        (int col, int row) = _pendingCoord.Value.ToOffset();
        int lex = row * _pendingCols + col;
        _label.Text = $"#{lex}  (col {col}, row {row})";
        Vector2 mouse = GetViewport().GetMousePosition();
        _label.Position = mouse + new Vector2(CursorOffsetX, CursorOffsetY);
        _label.Visible = true;
        _shown = true;
    }

    /// <summary>
    /// Report the current hover state. Call on every mouse-motion event:
    /// hides any visible tooltip immediately and restarts the dwell
    /// timer with the new coord. Pass null when the cursor is off-grid.
    /// <paramref name="cols"/> is the map width used to compute the
    /// row-major lex index when the dwell elapses.
    /// </summary>
    public void NotifyHover(HexCoord? coord, int cols)
    {
        if (_shown)
        {
            _label.Visible = false;
            _shown = false;
        }
        _pendingCoord = coord;
        _pendingCols = cols;
        _lastMotionMsec = Time.GetTicksMsec();
    }
}
