using System;
using Godot;

/// <summary>
/// The campaign screen (issue #2): a center-anchored panel hosting a
/// fixed header (back button, total «won» / 256 stat, thin progress bar)
/// over one vertically scrolling surface with all four difficulty tiers —
/// each a full-width tier header bar plus a 64-cell honeycomb of hexagon
/// levels. Orientation decides the honeycomb width (8 columns portrait,
/// 16 landscape — at 16 each row is one 0x10 block, self-indexing);
/// <see cref="MainMenuScene"/> rebuilds the panel when a resize flips
/// orientation, same pattern as the play-config panel.
///
/// Status visual language (design handoff, restyled to the dark theme):
/// won = solid green fill; lost = red outline + red number; untried =
/// muted outline + muted number; "next up" (lowest unbeaten level,
/// exactly one) = thick bright outline. Tapping a hex raises
/// <see cref="LevelTapped"/>; navigation back raises <see cref="BackPressed"/>.
/// </summary>
public partial class CampaignPanel : Panel
{
    public event Action? BackPressed;
    public event Action<int>? LevelTapped;

    // Status palette — UiPalette-adjacent values tuned for the dark
    // panels (the wireframe's paper-white scheme inverted onto BgElev).
    private static readonly Color WonFill = new Color("2f7d52");
    private static readonly Color CellFill = UiPalette.BgElev;
    private static readonly Color LostOutline = UiPalette.Accent;
    private static readonly Color UntriedOutline = UiPalette.Line;
    private static readonly Color UntriedInk = UiPalette.InkMute;

    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Base hex cell geometry. 56px wide comfortably clears the 44px
    // minimum touch target; height is the regular pointy-top ratio
    // (w / sin60). Scaled down per instance (`_hexW`/`_hexH`/`_hexGap`)
    // so the tier block fits the viewport width without clipping.
    private const float BaseHexW = 56f;
    private const float BaseHexH = 64f;
    private const float BaseHexGap = 4f;

    // Horizontal inset and header band height (px, unscaled — see below).
    private const float Pad = 28f;
    private const float HeaderHeight = 100f;

    private readonly int _columns;
    private readonly float _hexW;
    private readonly float _hexH;
    private readonly float _hexGap;
    private Label _totalStat = null!;
    private ColorRect _progressFill = null!;
    private readonly Label[] _tierStats = new Label[CampaignProgress.TierCount];
    private readonly TierGrid[] _tierGrids = new TierGrid[CampaignProgress.TierCount];

    public CampaignPanel(ScreenOrientation orientation, Vector2 viewport)
    {
        bool portrait = orientation == ScreenOrientation.Portrait;
        _columns = portrait ? 8 : 16;

        // Derive the hex size from the PORTRAIT constraint (8 columns in
        // the screen's narrower dimension), regardless of the current
        // orientation, and never upscale. That makes the hexes identical
        // portrait vs landscape — and since a landscape row is 16 wide in
        // the wider dimension (≈2× the narrow one), an 8-column-fit hex
        // always fits the 16 columns there too, so nothing clips.
        const float scrollbarAllowance = 18f;
        float narrowSide = Mathf.Min(viewport.X, viewport.Y);
        float available = narrowSide - Pad * 2f - scrollbarAllowance;
        (float portraitBlockW, float _) = CampaignGridMath.BlockSize(
            CampaignProgress.TierSize, 8, BaseHexW, BaseHexH, BaseHexGap);
        float fit = Mathf.Clamp(available / portraitBlockW, 0.1f, 1f);
        _hexW = BaseHexW * fit;
        _hexH = BaseHexH * fit;
        _hexGap = BaseHexGap * fit;

        // Fill the whole viewport. The campaign ladder is a SCROLLING
        // surface, not a fixed dialog: unlike the landing / play-config
        // panels (which FitPanels shrinks to fit), this one fills the
        // viewport and scrolls the overflow — so it is deliberately NOT in
        // the ScaleToFit set. Anchors let Godot re-solve width/height on
        // every resize; an orientation flip rebuilds it (8 ↔ 16 columns).
        AnchorLeft = 0f; AnchorTop = 0f; AnchorRight = 1f; AnchorBottom = 1f;

        BuildHeader();
        BuildScrollingTiers();

        Log.Debug(Log.LogCategory.Campaign,
            $"CampaignPanel: built ({(portrait ? "portrait" : "landscape")}, {_columns} columns, " +
            $"fit {fit:0.00}, hex {_hexW:0}×{_hexH:0}, viewport-filling)");
    }

    private void BuildHeader()
    {
        var backButton = new Button { Text = "← Campaign", Flat = true };
        backButton.AddThemeFontOverride("font", SerifFont);
        backButton.AddThemeFontSizeOverride("font_size", 34);
        backButton.AnchorLeft = 0f; backButton.AnchorTop = 0f;
        backButton.OffsetLeft = Pad - 8f; backButton.OffsetTop = 22f;
        backButton.OffsetRight = Pad - 8f + 280f; backButton.OffsetBottom = 22f + 48f;
        backButton.Alignment = HorizontalAlignment.Left;
        backButton.Pressed += () => BackPressed?.Invoke();
        AudioBus.AttachClick(backButton);
        AddChild(backButton);

        // Top-right, pinned to the right edge so it tracks viewport width.
        _totalStat = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = -Pad - 260f, OffsetRight = -Pad,
            OffsetTop = 22f, OffsetBottom = 22f + 48f,
        };
        _totalStat.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        _totalStat.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_totalStat);

        // Thin full-width progress bar: bordered track spanning the inset
        // width, with a fill whose right anchor = wins/256 (set in Refresh,
        // so it scales with viewport width for free).
        var track = new Panel
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = Pad, OffsetRight = -Pad, OffsetTop = 78f, OffsetBottom = 88f,
        };
        var trackStyle = new StyleBoxFlat
        {
            BgColor = UiPalette.BgElev,
            BorderColor = UiPalette.Line,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
        };
        trackStyle.SetBorderWidthAll(1);
        track.AddThemeStyleboxOverride("panel", trackStyle);
        AddChild(track);

        _progressFill = new ColorRect
        {
            Color = WonFill,
            AnchorLeft = 0f, AnchorRight = 0f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 1f, OffsetTop = 1f, OffsetBottom = -1f, OffsetRight = 0f,
        };
        track.AddChild(_progressFill);
    }

    private void BuildScrollingTiers()
    {
        // Fills the viewport below the header and scrolls vertically. The
        // tier blocks render at full hex size, so the four of them overflow
        // and the ScrollContainer pans (touch-drag on device).
        var scroll = new ScrollContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = Pad, OffsetRight = -Pad,
            OffsetTop = HeaderHeight, OffsetBottom = -Pad,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // Ignore so a touch-drag starting in the column's gaps reaches
            // the ScrollContainer instead of being swallowed.
            MouseFilter = MouseFilterEnum.Ignore,
        };
        column.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(column);

        for (int tier = 0; tier < CampaignProgress.TierCount; tier++)
        {
            column.AddChild(BuildTierHeader(tier));
            var grid = new TierGrid(this, tier, _columns);
            _tierGrids[tier] = grid;
            column.AddChild(grid);
        }
    }

    private Control BuildTierHeader(int tier)
    {
        var bar = new Panel
        {
            CustomMinimumSize = new Vector2(0f, 44f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            // Ignore so a touch-drag starting on a tier header scrolls the
            // list (a Panel defaults to Stop and would eat the gesture).
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat
        {
            BgColor = UiPalette.BgRow,
            BorderColor = UiPalette.Line,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        style.SetBorderWidthAll(1);
        bar.AddThemeStyleboxOverride("panel", style);

        int first = tier * CampaignProgress.TierSize;
        int last = first + CampaignProgress.TierSize - 1;
        // Anchor top+bottom to the bar with pure offsets (no Position/Size:
        // those would bake offsets against the bar's unresolved 0-height at
        // build time and the text would sit off-center once the bar lays out).
        var name = new Label
        {
            Text = $"{CampaignProgress.DifficultyForLevel(first)} · " +
                $"{CampaignProgress.LabelFor(first)} – {CampaignProgress.LabelFor(last)}",
            VerticalAlignment = VerticalAlignment.Center,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 14f, OffsetRight = 314f,
            OffsetTop = 0f, OffsetBottom = 0f,
        };
        name.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        name.AddThemeFontSizeOverride("font_size", 20);
        bar.AddChild(name);

        var stat = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            AnchorLeft = 1f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = -174f, OffsetRight = -14f,
        };
        stat.AddThemeColorOverride("font_color", UiPalette.Ink);
        stat.AddThemeFontSizeOverride("font_size", 20);
        bar.AddChild(stat);
        _tierStats[tier] = stat;

        return bar;
    }

    /// <summary>Re-read <see cref="CampaignStore.Progress"/> and repaint
    /// everything — header stat, progress bar, tier stats, hex grids.
    /// Called on every open and after any status change.</summary>
    public void Refresh()
    {
        CampaignProgress progress = CampaignStore.Progress;
        int won = progress.WonCount;
        _totalStat.Text = $"{won} / {CampaignProgress.LevelCount} won";
        // Fill's right anchor = wins/256, so it scales with the track's
        // (viewport-driven) width without recomputing a pixel width.
        _progressFill.AnchorRight = (float)won / CampaignProgress.LevelCount;
        for (int tier = 0; tier < CampaignProgress.TierCount; tier++)
        {
            _tierStats[tier].Text =
                $"{progress.TierWonCount(tier)} / {CampaignProgress.TierSize} ✓";
            _tierGrids[tier].QueueRedraw();
        }
        Log.Debug(Log.LogCategory.Campaign,
            $"CampaignPanel: refreshed — {won}/{CampaignProgress.LevelCount} won, next up " +
            $"{(progress.NextUp is int n ? CampaignProgress.LabelFor(n) : "none")}");
    }

    /// <summary>
    /// One tier's 64-hex honeycomb as a single custom-drawn Control —
    /// far lighter than 64 Button nodes, and the 8↔16 column reflow is
    /// just a rebuild. Drawing and hit-testing share
    /// <see cref="CampaignGridMath"/>, so taps resolve by true hex shape
    /// (the interlocked rows overlap vertically).
    /// </summary>
    private partial class TierGrid : Control
    {
        // A press that moves more than this (px) is a scroll drag, not a
        // level tap — lets touch-drag scroll even when it starts on a hex.
        private const float TapSlopPx = 12f;

        private readonly CampaignPanel _panel;
        private readonly int _tier;
        private readonly int _columns;
        private Vector2 _pressLocal;
        private Vector2 _pressGlobal;
        private bool _pressed;

        // Scaled hex geometry, read from the owning panel.
        private float HexW => _panel._hexW;
        private float HexH => _panel._hexH;
        private float HexGap => _panel._hexGap;

        public TierGrid(CampaignPanel panel, int tier, int columns)
        {
            _panel = panel;
            _tier = tier;
            _columns = columns;
            (float w, float h) = CampaignGridMath.BlockSize(
                CampaignProgress.TierSize, columns, HexW, HexH, HexGap);
            CustomMinimumSize = new Vector2(w, h);
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            // Pass (not Stop): the parent ScrollContainer must still see the
            // touch so a drag that starts on a hex scrolls. We never
            // AcceptEvent, and only treat a near-stationary press as a tap.
            MouseFilter = MouseFilterEnum.Pass;
        }

        public override void _Draw()
        {
            CampaignProgress progress = CampaignStore.Progress;
            Font font = GetThemeDefaultFont();
            int fontSize = Mathf.RoundToInt(18f * HexW / BaseHexW);

            for (int i = 0; i < CampaignProgress.TierSize; i++)
            {
                int level = _tier * CampaignProgress.TierSize + i;
                (float cx, float cy) = CampaignGridMath.CellCenter(
                    i, _columns, HexW, HexH, HexGap);

                // No "next up" treatment: the design handoff had a thick
                // outline on the lowest unbeaten level, but it masked the
                // lost state of that very hex and read as confusing —
                // status alone drives the styling now.
                (Color fill, Color outline, Color ink, float outlineWidth) =
                    progress.StatusOf(level) switch
                {
                    CampaignLevelStatus.Won => (WonFill, WonFill, UiPalette.Ink, 1.5f),
                    CampaignLevelStatus.Lost => (CellFill, LostOutline, LostOutline, 1.5f),
                    _ => (CellFill, UntriedOutline, UntriedInk, 1.5f),
                };

                Vector2[] points = HexPoints(cx, cy);
                DrawColoredPolygon(points, fill);
                // Closed outline: DrawPolyline needs the first point again.
                var loop = new Vector2[points.Length + 1];
                points.CopyTo(loop, 0);
                loop[points.Length] = points[0];
                DrawPolyline(loop, outline, outlineWidth, antialiased: true);

                string label = CampaignProgress.LabelFor(level);
                float baseline = cy - font.GetHeight(fontSize) / 2f + font.GetAscent(fontSize);
                DrawString(font, new Vector2(cx - HexW / 2f, baseline), label,
                    HorizontalAlignment.Center, HexW, fontSize, ink);
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            // Tap = press + release whose GLOBAL position barely moved. We
            // compare global (not local) positions because a scroll drags
            // the content under the finger, so the local point stays put
            // even as the screen point travels — a local test would read a
            // scroll as a tap. We never AcceptEvent, so a drag still reaches
            // the ScrollContainer.
            if (@event is not InputEventMouseButton mb
                || mb.ButtonIndex != MouseButton.Left)
            {
                return;
            }
            if (mb.Pressed)
            {
                _pressLocal = mb.Position;
                _pressGlobal = mb.GlobalPosition;
                _pressed = true;
                return;
            }
            if (!_pressed) return;
            _pressed = false;
            if (mb.GlobalPosition.DistanceTo(_pressGlobal) > TapSlopPx) return; // scroll, not tap

            int? cell = CampaignGridMath.HitTest(
                _pressLocal.X, _pressLocal.Y,
                CampaignProgress.TierSize, _columns, HexW, HexH, HexGap);
            if (cell == null) return;
            int level = _tier * CampaignProgress.TierSize + cell.Value;
            Log.Debug(Log.LogCategory.Campaign,
                $"CampaignPanel: tapped level {CampaignProgress.LabelFor(level)} " +
                $"({CampaignStore.Progress.StatusOf(level)})");
            _panel.LevelTapped?.Invoke(level);
        }

        /// <summary>Pointy-top hexagon vertices around a center — the
        /// same shape <see cref="CampaignGridMath.HitTest"/> tests.</summary>
        private Vector2[] HexPoints(float cx, float cy) => new[]
        {
            new Vector2(cx, cy - HexH / 2f),
            new Vector2(cx + HexW / 2f, cy - HexH / 4f),
            new Vector2(cx + HexW / 2f, cy + HexH / 4f),
            new Vector2(cx, cy + HexH / 2f),
            new Vector2(cx - HexW / 2f, cy + HexH / 4f),
            new Vector2(cx - HexW / 2f, cy - HexH / 4f),
        };
    }
}
