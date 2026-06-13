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

    // Hex cell geometry. 56px wide comfortably clears the 44px minimum
    // touch target; height is the regular pointy-top ratio (w / sin60).
    private const float HexW = 56f;
    private const float HexH = 64f;
    private const float HexGap = 4f;

    public Vector2 DesignSize { get; }

    private readonly int _columns;
    private Label _totalStat = null!;
    private ColorRect _progressFill = null!;
    private float _progressTrackWidth;
    private readonly Label[] _tierStats = new Label[CampaignProgress.TierCount];
    private readonly TierGrid[] _tierGrids = new TierGrid[CampaignProgress.TierCount];

    public CampaignPanel(ScreenOrientation orientation)
    {
        bool portrait = orientation == ScreenOrientation.Portrait;
        _columns = portrait ? 8 : 16;
        (float blockW, float _) = CampaignGridMath.BlockSize(
            CampaignProgress.TierSize, _columns, HexW, HexH, HexGap);

        const float pad = 28f;
        float panelW = blockW + pad * 2f;
        float panelH = portrait ? 1160f : 860f;
        DesignSize = new Vector2(panelW, panelH);

        AnchorLeft = 0.5f; AnchorRight = 0.5f; AnchorTop = 0.5f; AnchorBottom = 0.5f;
        OffsetLeft = -panelW * 0.5f; OffsetRight = panelW * 0.5f;
        OffsetTop = -panelH * 0.5f; OffsetBottom = panelH * 0.5f;
        GrowHorizontal = GrowDirection.Both;
        GrowVertical = GrowDirection.Both;

        BuildHeader(panelW, pad);
        BuildScrollingTiers(panelW, panelH, pad);

        Log.Debug(Log.LogCategory.Campaign,
            $"CampaignPanel: built ({(portrait ? "portrait" : "landscape")}, {_columns} columns, " +
            $"design {panelW}x{panelH})");
    }

    private void BuildHeader(float panelW, float pad)
    {
        var backButton = new Button { Text = "← Campaign", Flat = true };
        backButton.AddThemeFontOverride("font", SerifFont);
        backButton.AddThemeFontSizeOverride("font_size", 34);
        backButton.Position = new Vector2(pad - 8f, 22f);
        backButton.Size = new Vector2(280f, 48f);
        backButton.Alignment = HorizontalAlignment.Left;
        backButton.Pressed += () => BackPressed?.Invoke();
        AudioBus.AttachClick(backButton);
        AddChild(backButton);

        _totalStat = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector2(panelW - pad - 240f, 22f),
            Size = new Vector2(240f, 48f),
        };
        _totalStat.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        _totalStat.AddThemeFontSizeOverride("font_size", 22);
        AddChild(_totalStat);

        // Thin total-progress bar: bordered track + proportional fill.
        _progressTrackWidth = panelW - pad * 2f - 2f;
        var track = new Panel
        {
            Position = new Vector2(pad, 78f),
            Size = new Vector2(panelW - pad * 2f, 10f),
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
            Position = new Vector2(1f, 1f),
            Size = new Vector2(0f, 8f),
        };
        track.AddChild(_progressFill);
    }

    private void BuildScrollingTiers(float panelW, float panelH, float pad)
    {
        var scroll = new ScrollContainer
        {
            Position = new Vector2(pad, 100f),
            Size = new Vector2(panelW - pad * 2f, panelH - 100f - pad),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);

        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
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
        _progressFill.Size = new Vector2(
            _progressTrackWidth * won / CampaignProgress.LevelCount, 8f);
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
        private readonly CampaignPanel _panel;
        private readonly int _tier;
        private readonly int _columns;

        public TierGrid(CampaignPanel panel, int tier, int columns)
        {
            _panel = panel;
            _tier = tier;
            _columns = columns;
            (float w, float h) = CampaignGridMath.BlockSize(
                CampaignProgress.TierSize, columns, HexW, HexH, HexGap);
            CustomMinimumSize = new Vector2(w, h);
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
            MouseFilter = MouseFilterEnum.Stop;
        }

        public override void _Draw()
        {
            CampaignProgress progress = CampaignStore.Progress;
            Font font = GetThemeDefaultFont();
            const int fontSize = 18;

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
            if (@event is not InputEventMouseButton mb
                || mb.ButtonIndex != MouseButton.Left
                || !mb.Pressed)
            {
                return;
            }
            int? cell = CampaignGridMath.HitTest(
                mb.Position.X, mb.Position.Y,
                CampaignProgress.TierSize, _columns, HexW, HexH, HexGap);
            if (cell == null) return;
            int level = _tier * CampaignProgress.TierSize + cell.Value;
            AcceptEvent();
            Log.Debug(Log.LogCategory.Campaign,
                $"CampaignPanel: tapped level {CampaignProgress.LabelFor(level)} " +
                $"({CampaignStore.Progress.StatusOf(level)})");
            _panel.LevelTapped?.Invoke(level);
        }

        /// <summary>Pointy-top hexagon vertices around a center — the
        /// same shape <see cref="CampaignGridMath.HitTest"/> tests.</summary>
        private static Vector2[] HexPoints(float cx, float cy) => new[]
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
