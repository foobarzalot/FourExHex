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

    public event Action? Opened;
    public event Action? Closed;

    /// <summary>
    /// Fires immediately before <see cref="Hide"/> when the user
    /// dismisses the modal with the Escape key (not when a button is
    /// clicked). Lets a host distinguish "user backed out" from "user
    /// picked an option" — useful for pause coordinators that need to
    /// unpause on Escape but stay paused while a button-driven
    /// sub-screen takes over.
    /// </summary>
    public event Action? EscapeClosed;

    public bool IsOpen { get; private set; }

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

        // Decorative gold rule under the title — matches the menu panels.
        var goldRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        vbox.AddChild(goldRule);

        _buttonBox = new VBoxContainer();
        _buttonBox.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(_buttonBox);
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

        bool wasOpen = IsOpen;
        IsOpen = true;
        Visible = true;
        if (!wasOpen) Opened?.Invoke();
    }

    public new void Hide()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Closed?.Invoke();
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
}
