// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Scene-independent achievement toast surface — an autoload
/// <see cref="CanvasLayer"/>, so a queue still draining when the game
/// scene is torn down (exit-to-menu, next campaign level, restart) keeps
/// delivering its toasts over whatever scene comes next, first-party
/// style. Drawn above the modal layers (100/101). The FIFO policy is
/// <see cref="AchievementToastQueue"/> (Controller, unit-tested); this
/// node owns only the chrome and the tween. Producers reach it via the
/// static <see cref="Show"/> (the game scene's <c>HudView</c> forwards
/// its <c>IHudView.ShowAchievementBanner</c> here).
/// </summary>
public partial class AchievementToastLayer : CanvasLayer
{
    private const float BannerW = 560f;
    private const float BannerH = 64f;
    private const float BannerMarginTop = 16f;
    private const float SideMargin = UiMetrics.ViewportMarginPx;
    private const double FadeInSeconds = 0.2;
    private const double FadeOutSeconds = 0.4;
    private const double HoldSeconds = 2.5;
    // A toast with more waiting behind it holds for less — a burst of
    // simultaneous unlocks reads brisk; the last one gets the full hold.
    private const double QueuedHoldSeconds = 1.25;

    private static readonly Font GeistFont =
        GD.Load<FontFile>("res://fonts/Geist-VariableFont.ttf");

    private static AchievementToastLayer? _instance;

    private readonly AchievementToastQueue _toasts = new();
    private Panel _banner = null!;
    private Label _label = null!;
    private Tween? _tween;

    /// <summary>Queue a toast on the singleton. Safe to call from any
    /// scene; a no-op before the autoload enters the tree (only the
    /// game-end award path calls this, long after boot).</summary>
    public static void Show(string text) => _instance?.EnqueueToast(text);

    public override void _Ready()
    {
        _instance = this;
        Layer = 120;
        BuildBanner();
        RecordingMode.Changed += OnRecordingModeChanged;
        SafeArea.Changed += OnSafeAreaChanged;
        GetViewport().SizeChanged += PositionBanner;
    }

    public override void _ExitTree()
    {
        RecordingMode.Changed -= OnRecordingModeChanged;
        SafeArea.Changed -= OnSafeAreaChanged;
        GetViewport().SizeChanged -= PositionBanner;
        if (_instance == this) _instance = null;
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => PositionBanner();

    /// <summary>Recording chrome hides every toast; pending unlocks are
    /// dropped rather than replayed after recording ends (they remain in
    /// the record and panel).</summary>
    private void OnRecordingModeChanged()
    {
        if (!RecordingMode.Active) return;
        _tween?.Kill();
        _banner.Visible = false;
        _toasts.Clear();
    }

    private void EnqueueToast(string text)
    {
        if (RecordingMode.Active)
        {
            Log.Debug(Log.LogCategory.Achieve,
                $"[banner] recording mode — suppressed: {text}");
            return;
        }
        if (_toasts.Enqueue(text) is string showNow)
        {
            PlayToast(showNow);
        }
        else
        {
            Log.Debug(Log.LogCategory.Achieve,
                $"[banner] queued ({_toasts.PendingCount} pending): {text}");
        }
    }

    /// <summary>Fade in, hold, fade out, then drain the next queued toast.</summary>
    private void PlayToast(string text)
    {
        _label.Text = text;
        PositionBanner();
        _tween?.Kill();
        _banner.Modulate = new Color(1f, 1f, 1f, 0f);
        _banner.Visible = true;
        double hold = _toasts.PendingCount > 0 ? QueuedHoldSeconds : HoldSeconds;
        _tween = _banner.CreateTween();
        _tween.TweenProperty(_banner, "modulate:a", 1f, FadeInSeconds)
            .SetTrans(Tween.TransitionType.Sine);
        _tween.TweenInterval(hold);
        _tween.TweenProperty(_banner, "modulate:a", 0f, FadeOutSeconds)
            .SetTrans(Tween.TransitionType.Sine);
        _tween.TweenCallback(Callable.From(() =>
        {
            _banner.Visible = false;
            if (_toasts.OnToastFinished() is string next)
            {
                PlayToast(next);
            }
        }));
        Log.Debug(Log.LogCategory.Achieve, $"[banner] shown: {text}");
    }

    private void BuildBanner()
    {
        _banner = new Panel
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _banner.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(UiPalette.BgPanel, 0.94f),
            BorderColor = UiPalette.Gold,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
        });
        AddChild(_banner);

        // Trophy glyph pinned left, text centered in the remaining box —
        // the label spans the full width so the copy stays optically
        // centered in the banner rather than shunted by the glyph.
        var trophy = new Label
        {
            Text = "🏆",
            AnchorLeft = 0f, AnchorRight = 0f,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 14f, OffsetRight = 54f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        trophy.AddThemeFontSizeOverride("font_size", 28);
        trophy.AddThemeColorOverride("font_color", UiPalette.Gold);
        _banner.AddChild(trophy);

        _label = new Label
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = 58f, OffsetRight = -16f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontOverride("font", GeistFont);
        _label.AddThemeFontSizeOverride("font_size", 24);
        _label.AddThemeColorOverride("font_color", UiPalette.Gold);
        _banner.AddChild(_label);

        PositionBanner();
    }

    /// <summary>
    /// Width-cap + place in the topmost toast slot: below the game HUD's
    /// top chrome (portrait top bar / landscape rails — the clearances
    /// also read fine over the menu, where there is no chrome to clear).
    /// Re-run on resize, safe-area change, and per toast (orientation may
    /// change mid-drain).
    /// </summary>
    private void PositionBanner()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        ScreenOrientation orientation = ScreenLayout.Resolve(viewport.X, viewport.Y);
        // In landscape both side rails claim notch + rail + gutter each;
        // portrait has no rails (mirrors HudView.LandscapeRailClearance).
        float railClearance = orientation == ScreenOrientation.Landscape
            ? (Mathf.Max(SafeArea.Current.Left, SafeArea.Current.Right)
               + HudBars.RailWidth + UiMetrics.GutterPx) * 2f
            : 0f;
        float width = HudPanelMath.ClampWidth(
            BannerW, viewport.X - railClearance, SideMargin);
        _banner.OffsetLeft = -width * 0.5f;
        _banner.OffsetRight = width * 0.5f;

        float topClearance = orientation == ScreenOrientation.Portrait ? 150f : 80f;
        float top = SafeArea.Current.Top + topClearance + BannerMarginTop;
        _banner.OffsetTop = top;
        _banner.OffsetBottom = top + BannerH;
    }
}
