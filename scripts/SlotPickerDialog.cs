using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Reusable modal that displays a list of <see cref="SaveSlotInfo"/>
/// rows as clickable buttons. Used by the three scenes that pick a
/// save/map/tutorial slot to load (main menu, map editor, tutorial
/// builder). Consolidates the previously-triplicated <c>BuildLoadDialog</c>
/// + <c>OnLoadDialogInput</c> + <c>ShowLoadError</c> + <c>FormatTimestamp</c>
/// boilerplate so changes to the modal's chrome land in one place.
///
/// The host scene constructs one instance, calls <see cref="Attach"/>
/// once (which adds it under the scene root), then calls
/// <see cref="ShowSlots"/> each time the user opens the loader. The slot
/// list, empty-state text, label formatter, and per-slot handler are
/// passed in per-open so each scene's distinct load logic stays local
/// to that scene.
///
/// Built on a <see cref="CanvasLayer"/> + dim backdrop + centered
/// <see cref="PanelContainer"/> (same pattern as <see cref="SettingsPanel"/>)
/// so the modal picks up the project theme's slate panel style instead
/// of Godot 4's default <see cref="Window"/> chrome (which silently
/// ignores embedded_border overrides; see the "Visual / UI theme"
/// section of ARCHITECTURE.md).
/// </summary>
public sealed partial class SlotPickerDialog : CanvasLayer
{
    private readonly string _title;
    private VBoxContainer _list = null!;
    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;

    private Label _errorTitleLabel = null!;
    private Label _errorBodyLabel = null!;
    private PanelContainer _errorPanel = null!;
    private ColorRect _errorBackdrop = null!;
    private readonly string _errorTitle;

    /// <summary>
    /// Construct the picker. <paramref name="title"/> is shown in the
    /// modal's panel header; <paramref name="errorTitle"/> heads the
    /// inline error panel used by <see cref="ShowError"/>. The tutorial
    /// builder needs <paramref name="disableHorizontalScroll"/> = true
    /// so long slot names don't introduce a horizontal scrollbar that
    /// the other scenes don't show.
    /// </summary>
    public SlotPickerDialog(string title, string errorTitle, bool disableHorizontalScroll = false)
    {
        _title = title;
        _errorTitle = errorTitle;
        Layer = 100;
        Visible = false;
        // Always — Main's in-game Load Game flow opens this while
        // GetTree().Paused is true; default Inherit would freeze the
        // dialog. Safe for the unpaused main-menu / map-editor hosts.
        ProcessMode = ProcessModeEnum.Always;
        _disableHorizontalScroll = disableHorizontalScroll;
    }

    private readonly bool _disableHorizontalScroll;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        _backdrop.GuiInput += OnBackdropInput;
        AddChild(_backdrop);

        _panel = ModalChrome.BuildCenteredPanel(panelW: 560, panelH: 480);
        AddChild(_panel);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(vbox);

        vbox.AddChild(ModalChrome.BuildSerifTitle(_title));

        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        if (_disableHorizontalScroll)
        {
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        }
        vbox.AddChild(scroll);

        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_list);

        BuildErrorOverlay(viewport);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Escape) return;
        if (_errorPanel.Visible)
        {
            HideError();
        }
        else
        {
            Hide();
        }
        GetViewport().SetInputAsHandled();
    }

    /// <summary>
    /// Add the picker under <paramref name="parent"/>. Call once during
    /// the scene's <c>_Ready</c>.
    /// </summary>
    public void Attach(Node parent)
    {
        parent.AddChild(this);
    }

    /// <summary>
    /// Rebuild the slot list and pop up the modal. The list is
    /// cleared and rebuilt every call so newly-saved slots surface
    /// on each open. Pass an empty <paramref name="slots"/> to show
    /// only the <paramref name="emptyMessage"/>.
    /// </summary>
    public void ShowSlots(
        IReadOnlyList<SaveSlotInfo> slots,
        string emptyMessage,
        Func<SaveSlotInfo, string> labelFor,
        Action<string> onPicked)
    {
        foreach (Node child in _list.GetChildren())
        {
            child.QueueFree();
        }
        if (slots.Count == 0)
        {
            var emptyLabel = new Label { Text = emptyMessage };
            emptyLabel.AddThemeFontSizeOverride("font_size", 18);
            _list.AddChild(emptyLabel);
        }
        foreach (SaveSlotInfo info in slots)
        {
            string capturedName = info.SlotName;
            var btn = new Button
            {
                Text = labelFor(info),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
            };
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.Pressed += () => onPicked(capturedName);
            AudioBus.AttachClick(btn);
            _list.AddChild(btn);
        }
        Visible = true;
    }

    /// <summary>
    /// Display a "Load failed" error inside the picker. Falls back to
    /// <see cref="GD.PushError"/> if the dialog isn't in the tree yet.
    /// </summary>
    public void ShowError(string message)
    {
        if (!IsInsideTree())
        {
            GD.PushError(message);
            return;
        }
        _errorTitleLabel.Text = _errorTitle;
        _errorBodyLabel.Text = message;
        Visible = true;
        _errorBackdrop.Visible = true;
        _errorPanel.Visible = true;
    }

    private void HideError()
    {
        _errorPanel.Visible = false;
        _errorBackdrop.Visible = false;
    }

    private void BuildErrorOverlay(Vector2 viewport)
    {
        _errorBackdrop = ModalChrome.BuildBackdrop(viewport);
        _errorBackdrop.Visible = false;
        AddChild(_errorBackdrop);

        _errorPanel = ModalChrome.BuildCenteredPanel(panelW: 420, panelH: 200);
        _errorPanel.Visible = false;
        AddChild(_errorPanel);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 14);
        _errorPanel.AddChild(vbox);

        _errorTitleLabel = new Label
        {
            Text = _errorTitle,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _errorTitleLabel.AddThemeFontSizeOverride("font_size", 22);
        _errorTitleLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(_errorTitleLabel);

        _errorBodyLabel = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _errorBodyLabel.AddThemeFontSizeOverride("font_size", 16);
        vbox.AddChild(_errorBodyLabel);

        var actions = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        var okButton = new Button { Text = "OK" };
        okButton.AddThemeFontSizeOverride("font_size", 16);
        okButton.Pressed += HideError;
        AudioBus.AttachClick(okButton);
        actions.AddChild(okButton);
        vbox.AddChild(actions);
    }

    private void OnBackdropInput(InputEvent @event)
    {
        // Backdrop click — close the picker (don't fall through to the
        // map underneath). Modal contract.
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            _backdrop.AcceptEvent();
            Hide();
        }
    }

    // CanvasLayer.Hide() just toggles Visible — we shadow it so callers
    // (and the backdrop / Escape paths) also clear any error overlay
    // that might be stacked on top.
    private new void Hide()
    {
        Visible = false;
        HideError();
    }

    /// <summary>
    /// Format a save timestamp consistently across all three load
    /// dialogs. Local time, minute precision.
    /// </summary>
    public static string FormatTimestamp(long unixSeconds)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
        return dt.ToString("yyyy-MM-dd HH:mm");
    }

}
