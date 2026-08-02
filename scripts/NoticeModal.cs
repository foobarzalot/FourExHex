// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using Godot;

/// <summary>
/// Reusable single-button message modal in the ModalChrome dialog family
/// (dim backdrop + centered slate panel, serif title, gold rule) — the
/// proper home for standalone notices (import outcomes, hints) that are
/// not tied to an open picker or name-entry modal. OK, Escape, Enter, or
/// a backdrop click dismiss and raise <see cref="Closed"/>. Title and
/// message are per-<see cref="Show"/>, so one instance serves a whole
/// scene's notices.
/// </summary>
public sealed partial class NoticeModal : CanvasLayer
{
    public event Action? Closed;

    public bool IsOpen { get; private set; }

    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;

    public override void _Ready()
    {
        // Same layer as the rest of the modal family.
        Layer = 100;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        ColorRect backdrop = ModalChrome.BuildBackdrop(viewport);
        backdrop.GuiInput += OnBackdropInput;
        AddChild(backdrop);

        PanelContainer panel = ModalChrome.BuildCenteredPanel();
        AddChild(panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(420, 0),
        };
        vbox.AddThemeConstantOverride("separation", 18);
        panel.AddChild(vbox);

        _titleLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _titleLabel.AddThemeFontOverride("font", _serifFont);
        _titleLabel.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(_titleLabel);

        vbox.AddChild(ModalChrome.GoldRule());

        _bodyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            // Cap the width so long messages wrap into a readable column
            // instead of stretching the panel across the screen.
            CustomMinimumSize = new Vector2(420, 0),
        };
        _bodyLabel.AddThemeFontSizeOverride("font_size", 22);
        _bodyLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(_bodyLabel);

        var buttonRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(buttonRow);

        var okButton = new Button
        {
            Text = Strings.Get(StringKeys.ButtonOk),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        okButton.AddThemeFontSizeOverride("font_size", 24);
        okButton.Pressed += Close;
        AudioBus.AttachClick(okButton);
        buttonRow.AddChild(okButton);
    }

    public void Show(string title, string message)
    {
        _titleLabel.Text = title;
        _bodyLabel.Text = message;
        IsOpen = true;
        Visible = true;
        Log.Debug(Log.LogCategory.Input, $"NoticeModal.Show '{title}'");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Log.Debug(Log.LogCategory.Input, "NoticeModal.Close");
        Closed?.Invoke();
    }

    private void OnBackdropInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            Close();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode is Key.Escape or Key.Enter or Key.KpEnter)
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }
}
