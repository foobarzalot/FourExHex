// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Reusable pause/exit modal shown on ESC across every scene. The host
/// scene populates it on every <see cref="Show"/> with a fresh option list,
/// so the same widget serves gameplay (Resume / Exit Game), map editor
/// (Resume / Exit), and tutorial builder (Resume / mode switches / Save /
/// Load / Exit).
///
/// Layout: a full-screen dim <see cref="ColorRect"/> backdrop
/// (MouseFilter=Stop, so clicks don't bleed through to the map) plus a
/// centered panel with a title <see cref="Label"/> and a vertical stack of
/// buttons. Resume is just the option whose callback is a no-op — the
/// modal always closes itself before invoking the callback.
///
/// ESC handling: while <see cref="IsOpen"/>, the modal's own
/// <see cref="_UnhandledInput"/> closes it on ESC. Host scenes should
/// short-circuit their own ESC handlers when <see cref="IsOpen"/> so the
/// modal doesn't get torn down and rebuilt on the same press.
/// </summary>
public sealed partial class EscMenu : CanvasLayer
{
    public sealed record Option(string Label, Action OnPressed, bool Disabled = false);

    /// <summary>
    /// Fires immediately before <see cref="Hide"/> when the user
    /// dismisses the modal with the Escape key (not when a button is
    /// clicked). Lets a host distinguish "user backed out" from "user
    /// picked an option" — useful for pause coordinators that need to
    /// unpause on Escape but stay paused while a button-driven
    /// sub-screen takes over.
    /// </summary>
    public event Action? EscapeClosed;

    /// <summary>
    /// When true, every key event is consumed in <see cref="_Input"/> while
    /// the modal is open (Escape still closes it) — matching the guided
    /// tour's key-swallowing, for hosts that don't pause the tree (the HUD
    /// Help menu). Default false: pause-driven hosts rely on
    /// <c>GetTree().Paused</c> and only Escape is intercepted.
    /// </summary>
    public bool SwallowAllKeysWhileOpen { get; set; }

    public bool IsOpen { get; private set; }

    /// <summary>Breathing room between the panel and the safe-area edge —
    /// the margin the rest of the centered-modal family fits against.</summary>
    private const float ViewportMargin = 24f;

    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private Label _titleLabel = null!;
    private VBoxContainer _buttonBox = null!;

    public override void _Ready()
    {
        Layer = 100;
        Visible = false;
        // Always — the modal must remain interactive both when the tree
        // is paused (Main's pause coordinator drives this) AND when it
        // isn't (map editor / tutorial builder use the same EscMenu
        // without ever pausing). WhenPaused breaks the unpaused hosts;
        // Pausable / Inherit breaks the paused host. Always covers both.
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        AddChild(_backdrop);

        // Content-sized centered panel — picks up the theme's slate Panel
        // stylebox; the vbox CustomMinimumSize below drives dimensions.
        _panel = ModalChrome.BuildCenteredPanel();
        AddChild(_panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(360, 0),
        };
        vbox.AddThemeConstantOverride("separation", 14);
        _panel.AddChild(vbox);

        _titleLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _titleLabel.AddThemeFontOverride("font", _serifFont);
        _titleLabel.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(_titleLabel);

        vbox.AddChild(ModalChrome.GoldRule());

        _buttonBox = new VBoxContainer();
        _buttonBox.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(_buttonBox);

        // Rotation resizes the viewport; a notch/status-bar toggle can shift
        // the safe rect without one. Either can change what fits.
        GetViewport().SizeChanged += FitPanel;
        SafeArea.Changed += OnSafeAreaChanged;
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= FitPanel;
        SafeArea.Changed -= OnSafeAreaChanged;
    }

    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    /// <summary>
    /// Replace the button list and show the modal. Safe to call when
    /// already open — the modal rebuilds its buttons in place.
    /// </summary>
    public void Show(string title, IReadOnlyList<Option> options)
    {
        _titleLabel.Text = title;

        foreach (Node child in _buttonBox.GetChildren())
        {
            // Detach before the queued free: QueueFree alone leaves the old
            // buttons in the tree until end of frame, so the fit pass below
            // would measure the outgoing list on top of the incoming one.
            _buttonBox.RemoveChild(child);
            child.QueueFree();
        }

        foreach (Option option in options)
        {
            var button = new Button
            {
                Text = option.Label,
                Disabled = option.Disabled,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            button.AddThemeFontSizeOverride("font_size", 22);
            Option captured = option;
            button.Pressed += () =>
            {
                Hide();
                captured.OnPressed();
            };
            AudioBus.AttachClick(button);
            _buttonBox.AddChild(button);
        }

        IsOpen = true;
        Visible = true;
        // The button list just changed, so the panel's design height is only
        // knowable after this frame's layout — fit a frame later.
        Callable.From(FitPanel).CallDeferred();
    }

    /// <summary>
    /// Shrink the centered panel (never grow it) so a long option list fits
    /// the safe viewport instead of running off the bottom edge — the same
    /// uniform shrink <see cref="SettingsPanel"/> uses. A button stack has
    /// nothing to reflow, so scaling is the whole fit.
    /// </summary>
    private void FitPanel()
    {
        if (!IsOpen || !IsInsideTree()) return;

        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;
        (float availW, float availH) = PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);

        // Transform-independent, so this measures the design size no matter
        // what scale a previous fit left behind.
        Vector2 design = _panel.GetCombinedMinimumSize();
        float scale = PanelFitMath.ScaleToFit(design.X, design.Y, availW, availH);
        _panel.PivotOffset = design * 0.5f;
        _panel.Scale = new Vector2(scale, scale);

        Log.Debug(Log.LogCategory.Render,
            $"EscMenu: fit viewport={vp.X:0}x{vp.Y:0} " +
            $"safe=(t{safe.Top:0},b{safe.Bottom:0},l{safe.Left:0},r{safe.Right:0}) " +
            $"avail={availW:0}x{availH:0} design={design.X:0}x{design.Y:0} scale={scale:0.00}");
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => FitPanel();

    public new void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
    }

    /// <summary>
    /// Close exactly as if the user pressed Escape on the open menu:
    /// fire <see cref="EscapeClosed"/> (so subscribers run their close
    /// bookkeeping — unpause, help-session recompute) then hide. Used
    /// by the Android system-back ladder. No-op when not open.
    /// </summary>
    public void CloseAsEscape()
    {
        if (!IsOpen) return;
        EscapeClosed?.Invoke();
        Hide();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        EscapeClosed?.Invoke();
        Hide();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Full key-swallow path (<see cref="SwallowAllKeysWhileOpen"/> only).
    /// Runs before any <c>_UnhandledInput</c>, so HUD hotkeys and map
    /// shortcuts can't fire underneath the modal — same contract as
    /// <c>HudTour._Input</c>.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || !SwallowAllKeysWhileOpen) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed) return;

        if (keyEvent.Keycode == Key.Escape && !keyEvent.Echo)
        {
            EscapeClosed?.Invoke();
            Hide();
        }
        GetViewport().SetInputAsHandled();
    }
}
