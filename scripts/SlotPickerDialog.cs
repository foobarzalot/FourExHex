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
/// once (which adds both the picker and its error dialog as siblings
/// under the scene root), then calls <see cref="ShowSlots"/> each time
/// the user opens the loader. The slot list, empty-state text, label
/// formatter, and per-slot handler are passed in per-open so each
/// scene's distinct load logic stays local to that scene.
/// </summary>
public partial class SlotPickerDialog : Window
{
    private readonly VBoxContainer _list;
    private readonly AcceptDialog _errorDialog;

    /// <summary>
    /// Construct the picker. <paramref name="title"/> is the window
    /// chrome's title; <paramref name="errorTitle"/> is the heading on
    /// the sibling <see cref="AcceptDialog"/> used by
    /// <see cref="ShowError"/>. The tutorial builder needs
    /// <paramref name="disableHorizontalScroll"/> = true so long
    /// slot names don't introduce a horizontal scrollbar that the
    /// other scenes don't show.
    /// </summary>
    public SlotPickerDialog(string title, string errorTitle, bool disableHorizontalScroll = false)
    {
        Title = title;
        Size = new Vector2I(560, 480);
        Visible = false;
        Exclusive = true;
        CloseRequested += () => Hide();
        // Escape closes. Godot's Window doesn't dismiss on Escape by
        // default, and the VBox/buttons inside swallow the key before
        // _UnhandledInput sees it — subscribe to the dialog's own
        // input stream so this works in every host scene.
        WindowInput += OnInput;

        var scroll = new ScrollContainer
        {
            AnchorLeft = 0f,
            AnchorTop = 0f,
            AnchorRight = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 16f,
            OffsetTop = 16f,
            OffsetRight = -16f,
            OffsetBottom = -16f,
        };
        if (disableHorizontalScroll)
        {
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        }
        AddChild(scroll);

        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);

        _errorDialog = new AcceptDialog
        {
            Title = errorTitle,
            OkButtonText = "OK",
            // Always — Main's in-game Load Game flow opens this while
            // GetTree().Paused is true; default Inherit would freeze
            // the dialog. Safe for the unpaused main-menu / map-editor
            // hosts (Always is a superset of their normal behavior).
            ProcessMode = ProcessModeEnum.Always,
        };
        ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>
    /// Add the picker and its sibling error dialog as children of
    /// <paramref name="parent"/>. Call once during the scene's
    /// <c>_Ready</c>.
    /// </summary>
    public void Attach(Node parent)
    {
        parent.AddChild(this);
        parent.AddChild(_errorDialog);
        AudioBus.AttachClick(_errorDialog.GetOkButton());
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
        PopupCentered();
    }

    /// <summary>
    /// Display a "Load failed" error on top of the picker (or
    /// stand-alone if the picker was already dismissed). Falls back
    /// to <see cref="GD.PushError"/> if the dialog isn't in the
    /// tree yet.
    /// </summary>
    public void ShowError(string message)
    {
        if (!_errorDialog.IsInsideTree())
        {
            GD.PushError(message);
            return;
        }
        _errorDialog.DialogText = message;
        _errorDialog.PopupCentered();
    }

    private void OnInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        Hide();
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
