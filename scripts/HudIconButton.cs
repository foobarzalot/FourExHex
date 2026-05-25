using Godot;

public enum HudIcon
{
    Recruit,
    Tower,
    UndoLast,
    UndoAll,
    RedoLast,
    RedoAll,
    EndTurn,
    NextTerritory,
    Options,
    Die,
    AddText,
}

/// <summary>
/// Iconographic HUD button. Subclasses Godot's <see cref="Button"/> so the
/// engine handles hit-testing, hover/press visuals, disabled stylebox,
/// tooltip popup, and the CTA stylebox + opacity tween that
/// <see cref="HudView.ApplyCtaStyle"/> applies. <see cref="_Draw"/> paints
/// a programmatic glyph on top of the rendered button chrome.
///
/// Use <see cref="Selected"/> to mark a button as "this mode is currently
/// active" (e.g. Buy Recruit after click, while waiting for a tile target).
/// Mirrors the white-outline cue used by the map editor's HexPaletteButton.
/// </summary>
public partial class HudIconButton : Button
{
    private readonly HudIcon _icon;
    private bool _selected;
    private bool _ctaActive;
    private UnitLevel _buyLevel = UnitLevel.Recruit;

    /// <summary>
    /// Which unit level the Buy button is currently targeting. Drives
    /// the ring count drawn for <see cref="HudIcon.Recruit"/> so the
    /// glyph tracks <see cref="SessionState.BuyModeLevel"/> as the
    /// player escalates a buy (Recruit → Soldier → Captain → Commander via
    /// adjacent-unit merges).
    /// </summary>
    public UnitLevel BuyLevel
    {
        get => _buyLevel;
        set
        {
            if (_buyLevel == value) return;
            _buyLevel = value;
            QueueRedraw();
        }
    }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Set by <see cref="HudView.ApplyCtaStyle"/> when the white CTA
    /// stylebox is applied. Stroke-only glyphs (Recruit, undo/redo arrows,
    /// EndTurn triangle) need to flip from white-on-dark to black-on-white
    /// or they disappear into the CTA bg.
    /// </summary>
    public bool CtaActive
    {
        get => _ctaActive;
        set
        {
            if (_ctaActive == value) return;
            _ctaActive = value;
            QueueRedraw();
        }
    }

    public HudIconButton(HudIcon icon)
    {
        _icon = icon;
        Text = "";
        CustomMinimumSize = new Vector2(44, 44);
        FocusMode = Control.FocusModeEnum.None;
        TooltipText = DefaultTooltip(icon);
    }

    /// <summary>
    /// Fire a brief warm-gold flash highlight, then fade back to neutral
    /// over 450ms. Modulate components > 1 brighten the underlying
    /// theme stylebox + glyph (rather than just tinting), which is what
    /// makes the flash actually visible on the dark slate bg the buttons
    /// otherwise paint with. Use on buttons whose effect doesn't
    /// otherwise produce visible feedback — e.g. the map editor's
    /// Generate die, whose click silently re-rolls the seed field and
    /// would otherwise leave the player guessing whether the press
    /// registered.
    /// </summary>
    public void FlashPress()
    {
        Tween tween = CreateTween();
        Modulate = new Color(2.2f, 1.8f, 1.0f, 1f);
        tween.TweenProperty(this, "modulate", new Color(1f, 1f, 1f, 1f), 0.45)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Canonical "<label> — <hotkey>" tooltip for a given icon. Shared
    /// across the play HUD and the map editor's HUD so both display the
    /// same wording for the same icon (e.g. Undo All — Shift+Z) without
    /// duplicating strings. Dynamic-state buttons (Buy / Build) override
    /// this in <see cref="HudView.Refresh"/> when the cost / mode changes.
    /// </summary>
    public static string DefaultTooltip(HudIcon icon) => icon switch
    {
        HudIcon.Recruit => "Buy Recruit (10g) — U",
        HudIcon.Tower => "Build Tower (15g) — T",
        HudIcon.UndoLast => "Undo Last — Z",
        HudIcon.UndoAll => "Undo All — Shift+Z",
        HudIcon.RedoLast => "Redo Last — Y",
        HudIcon.RedoAll => "Redo All — Shift+Y",
        HudIcon.EndTurn => "End Turn — Enter",
        HudIcon.NextTerritory => "Next Territory — Tab",
        HudIcon.Options => "Options — Esc",
        HudIcon.Die => "Generate map from seed",
        HudIcon.AddText => "Insert a tutorial-only narration beat here",
        _ => "",
    };

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.5f;
        // The HUD bar is near-black and the default Button stylebox is
        // dark too, so stroke-only glyphs (Recruit, undo/redo arrows,
        // EndTurn triangle) must paint in a *light* color or they
        // disappear into the button. When the CTA stylebox is active
        // the bg flips to white and we flip the stroke to black for
        // the same reason. Multi-color glyphs (Tower, Gear) already
        // silhouette via their fills.
        Color modulate = Disabled ? new Color(1f, 1f, 1f, 0.45f) : Colors.White;
        Color baseStroke = _ctaActive ? new Color(0f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
        if (Disabled) baseStroke.A = 0.45f;
        Color stroke = baseStroke;

        switch (_icon)
        {
            case HudIcon.Recruit: HudIcons.DrawUnit(this, center, r, _buyLevel, stroke); break;
            case HudIcon.Tower: HudIcons.DrawTower(this, center, r, modulate); break;
            case HudIcon.UndoLast: HudIcons.DrawCurvedArrow(this, center, r, stroke, facing: +1, doubled: false); break;
            case HudIcon.UndoAll: HudIcons.DrawCurvedArrow(this, center, r, stroke, facing: +1, doubled: true); break;
            case HudIcon.RedoLast: HudIcons.DrawCurvedArrow(this, center, r, stroke, facing: -1, doubled: false); break;
            case HudIcon.RedoAll: HudIcons.DrawCurvedArrow(this, center, r, stroke, facing: -1, doubled: true); break;
            case HudIcon.EndTurn: HudIcons.DrawEndTurnTriangle(this, center, r, stroke); break;
            case HudIcon.NextTerritory: HudIcons.DrawNextTerritory(this, center, r, modulate); break;
            case HudIcon.Options: HudIcons.DrawGear(this, center, r, modulate); break;
            case HudIcon.Die: HudIcons.DrawDie(this, center, r, stroke); break;
            case HudIcon.AddText: HudIcons.DrawSpeechBubble(this, center, r, stroke); break;
        }

        if (_selected)
        {
            // White outline ring around the whole button, matching the
            // selected-palette cue in the map editor. Drawn last so it
            // sits on top of the glyph.
            var rect = new Rect2(Vector2.One, Size - new Vector2(2f, 2f));
            DrawRect(rect, new Color(1f, 1f, 1f, 1f), filled: false, width: 2f);
        }
    }
}
