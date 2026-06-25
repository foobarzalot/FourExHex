using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Reusable modal that picks a save/map/tutorial slot to load. Used by four
/// hosts (main-menu Load Game, in-game Load Game, map-editor Load Map,
/// tutorial-builder Load Tutorial). Consolidates the previously-triplicated
/// <c>BuildLoadDialog</c> + <c>OnLoadDialogInput</c> + <c>ShowLoadError</c> +
/// <c>FormatTimestamp</c> boilerplate so chrome changes land in one place.
///
/// Two body layouts, chosen per-open:
/// <list type="bullet">
///   <item><b>Text-only</b> (editor / tutorial hosts) — a scrollable column of
///   click-to-load buttons. Unchanged behaviour.</item>
///   <item><b>Preview</b> (game-save hosts, when a <see cref="SaveStore"/> is
///   passed to <see cref="ShowSlots"/>) — a selectable slot list beside a single
///   large board <see cref="MapThumbnailView"/> that updates as you pick a slot,
///   plus Cancel / Load. Mirrors the New Game map-setup page (issue #55): a
///   distinct portrait (list-above-preview) vs landscape (list-rail | preview)
///   layout, rebuilt on orientation flip.</item>
/// </list>
///
/// Built on a <see cref="CanvasLayer"/> + dim backdrop + centered
/// <see cref="PanelContainer"/> (same pattern as <see cref="SettingsPanel"/>) so
/// the modal picks up the project theme's slate panel style. The panel scales to
/// fit a narrow safe viewport instead of clipping (same uniform shrink as
/// SettingsPanel / CreditsPanel).
/// </summary>
public sealed partial class SlotPickerDialog : CanvasLayer
{
    private readonly string _title;
    private ColorRect _backdrop = null!;
    private PanelContainer? _panel;
    private VBoxContainer _body = null!;

    private Label _errorTitleLabel = null!;
    private Label _errorBodyLabel = null!;
    private PanelContainer _errorPanel = null!;
    private ColorRect _errorBackdrop = null!;
    private readonly string _errorTitle;
    private readonly bool _disableHorizontalScroll;

    // Cached per-open parameters so an orientation flip can rebuild the body
    // without the host re-calling ShowSlots.
    private IReadOnlyList<SaveSlotInfo> _slots = Array.Empty<SaveSlotInfo>();
    private string _emptyMessage = "";
    private Func<SaveSlotInfo, string> _labelFor = _ => "";
    private Action<string> _onPicked = _ => { };
    private SaveStore? _thumbnailStore;
    private bool _previewUsesMaps;

    // Preview-mode state.
    private MapThumbnailView? _preview;
    private Button? _loadButton;
    private string? _selectedSlot;
    private int _previewToken;
    private ScreenOrientation _orientation = ScreenOrientation.Landscape;

    // Text-only (editor / tutorial) modal — a small fixed centered panel that
    // scales to fit. The preview (game-save) panel instead fills the safe
    // viewport up to a generous cap via LandscapeMenuChrome, matching the New
    // Game map-setup page's footprint (issue #55).
    private const float TextPanelW = 560f, TextPanelH = 480f;
    private const float ErrorPanelW = 420f, ErrorPanelH = 200f;
    private const float ViewportMargin = 24f;

    // Preview-panel fill caps — the same comfortable sizes the New Game page
    // uses (LandscapeMenuChrome 920×520 landscape; its 90° transpose portrait).
    private const float PreviewLandscapeMaxW = 920f, PreviewLandscapeMaxH = 520f;
    private const float PreviewPortraitMaxW = 520f, PreviewPortraitMaxH = 920f;

    /// <summary>
    /// Construct the picker. <paramref name="title"/> heads the modal;
    /// <paramref name="errorTitle"/> heads the inline error panel used by
    /// <see cref="ShowError"/>. The tutorial builder needs
    /// <paramref name="disableHorizontalScroll"/> = true so long slot names
    /// don't introduce a horizontal scrollbar.
    /// </summary>
    public SlotPickerDialog(string title, string errorTitle, bool disableHorizontalScroll = false)
    {
        _title = title;
        _errorTitle = errorTitle;
        Layer = 100;
        Visible = false;
        // Always — Main's in-game Load Game flow opens this while
        // GetTree().Paused is true; default Inherit would freeze the dialog.
        ProcessMode = ProcessModeEnum.Always;
        _disableHorizontalScroll = disableHorizontalScroll;
    }

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        _backdrop.GuiInput += OnBackdropInput;
        AddChild(_backdrop);

        // The main panel is (re)built per-open in BuildBody — its chrome differs
        // between text-only and preview modes. Error overlay is added now so it
        // stacks above whatever main panel BuildBody inserts beneath it.
        BuildErrorOverlay(viewport);

        GetViewport().SizeChanged += OnViewportResized;
        SafeArea.Changed += OnSafeAreaChanged;
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnSafeAreaChanged;
        if (GetViewport() != null) GetViewport().SizeChanged -= OnViewportResized;
    }

    private void OnSafeAreaChanged(LogicalSafeInsets _) => LayoutPanels();

    private void OnViewportResized()
    {
        // A preview-mode dialog has orientation-specific layouts; rebuild on flip.
        if (Visible && IsPreviewMode)
        {
            Vector2 vp = GetViewport().GetVisibleRect().Size;
            if (ScreenLayout.Resolve(vp.X, vp.Y) != _orientation) { BuildBody(); return; }
        }
        LayoutPanels();
    }

    private bool IsPreviewMode => _thumbnailStore != null && _slots.Count > 0;

    /// <summary>Size the active panels. The preview (game-save) panel fills the
    /// safe viewport up to a generous cap (LandscapeMenuChrome) so it reads as a
    /// full page like New Game's map setup, not a small modal. The text-only
    /// (editor / tutorial) panel and the error panel are small fixed-design boxes
    /// scaled down to fit a narrow viewport (SettingsPanel / CreditsPanel).</summary>
    private void LayoutPanels()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        LogicalSafeInsets safe = SafeArea.Current;

        if (_panel != null && IsPreviewMode)
        {
            _panel.Scale = Vector2.One; // fill-to-cap, never scaled
            bool portrait = _orientation == ScreenOrientation.Portrait;
            LandscapeMenuChrome.ApplyLayout(_panel, vp, safe,
                maxW: portrait ? PreviewPortraitMaxW : PreviewLandscapeMaxW,
                maxH: portrait ? PreviewPortraitMaxH : PreviewLandscapeMaxH);
        }
        else if (_panel != null)
        {
            ScaleFixedPanel(_panel, TextPanelW, TextPanelH, vp, safe);
        }
        ScaleFixedPanel(_errorPanel, ErrorPanelW, ErrorPanelH, vp, safe);
    }

    /// <summary>Pin a centered panel to its fixed design size and scale it down
    /// (never up) to fit the safe viewport, scaling about its centre so it stays
    /// centred under the 0.5 anchors.</summary>
    private static void ScaleFixedPanel(Control panel, float w, float h, Vector2 vp, LogicalSafeInsets safe)
    {
        float availW = vp.X - safe.Left - safe.Right - ViewportMargin * 2f;
        float availH = vp.Y - safe.Top - safe.Bottom - ViewportMargin * 2f;
        panel.OffsetLeft = -w * 0.5f;
        panel.OffsetRight = w * 0.5f;
        panel.OffsetTop = -h * 0.5f;
        panel.OffsetBottom = h * 0.5f;
        float scale = Mathf.Min(1f, Mathf.Min(availW / w, availH / h));
        panel.PivotOffset = new Vector2(w, h) * 0.5f;
        panel.Scale = new Vector2(scale, scale);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Escape) return;
        if (_errorPanel.Visible) HideError();
        else Hide();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Add the picker under <paramref name="parent"/>. Call once during
    /// the scene's <c>_Ready</c>.</summary>
    public void Attach(Node parent) => parent.AddChild(this);

    /// <summary>
    /// Show the modal with the given slots. Rebuilt every call so newly-saved
    /// slots surface on each open. Pass an empty <paramref name="slots"/> to show
    /// only the <paramref name="emptyMessage"/>.
    /// </summary>
    /// <param name="thumbnailStore">When non-null (the game-save hosts: main-menu
    /// and in-game Load Game), the body switches to the preview layout: a
    /// selectable slot list beside one large board thumbnail of the selected
    /// save. Null (the map-editor / tutorial-builder hosts) keeps the text-only
    /// click-to-load list — the preview is opt-in per-open (issue #55).</param>
    public void ShowSlots(
        IReadOnlyList<SaveSlotInfo> slots,
        string emptyMessage,
        Func<SaveSlotInfo, string> labelFor,
        Action<string> onPicked,
        SaveStore? thumbnailStore = null,
        bool previewMaps = false)
    {
        _slots = slots;
        _emptyMessage = emptyMessage;
        _labelFor = labelFor;
        _onPicked = onPicked;
        _thumbnailStore = thumbnailStore;
        // Maps live in a different directory than game saves; the preview must
        // load from the right one (issue #70).
        _previewUsesMaps = previewMaps;
        _selectedSlot = null;
        BuildBody();
        Visible = true;
    }

    /// <summary>Render the selected slot's preview from the correct store —
    /// the maps directory in map-picker mode, the saves directory otherwise.</summary>
    private void RequestPreview(string slotName)
    {
        if (_preview == null) return;
        if (_previewUsesMaps) _preview.RequestMap(slotName);
        else _preview.RequestSlot(slotName);
    }

    /// <summary>(Re)build the main panel + body for the current slots +
    /// orientation and lay it out. Reused on open and on an orientation flip.
    /// The panel chrome differs by mode: a fill-to-cap LandscapeMenuChrome
    /// surface for the preview, a small fixed modal for text-only.</summary>
    private void BuildBody()
    {
        if (_panel != null) { _panel.QueueFree(); _panel = null; }
        _preview = null;
        _loadButton = null;
        _previewToken++; // abandon any pending initial-preview schedule

        bool preview = IsPreviewMode;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        if (preview) _orientation = ScreenLayout.Resolve(vp.X, vp.Y);

        _panel = preview
            ? LandscapeMenuChrome.Build()
            : ModalChrome.BuildCenteredPanel(panelW: TextPanelW, panelH: TextPanelH);
        AddChild(_panel);
        // Keep the panel above the main backdrop but below the error overlay
        // (backdrop[0], panel[1], errorBackdrop, errorPanel).
        MoveChild(_panel, 1);

        _body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _body.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(_body);

        _body.AddChild(ModalChrome.BuildSerifTitle(_title));

        if (!preview)
        {
            BuildTextOnlyBody();
        }
        else
        {
            BuildPreviewBody(portrait: _orientation == ScreenOrientation.Portrait);
            // Render the (large) preview after one frame so it sizes against the
            // laid-out rect, not the 1600px fallback.
            _ = SchedulePreview(++_previewToken);
        }

        LayoutPanels();
    }

    // --- Text-only body (editor / tutorial hosts) ---

    private void BuildTextOnlyBody()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        if (_disableHorizontalScroll)
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _body.AddChild(scroll);

        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);

        if (_slots.Count == 0)
        {
            list.AddChild(MakeMessageLabel(_emptyMessage));
            return;
        }
        foreach (SaveSlotInfo info in _slots)
        {
            string capturedName = info.SlotName;
            var btn = new Button
            {
                Text = _labelFor(info),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
            };
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.Pressed += () => _onPicked(capturedName);
            AudioBus.AttachClick(btn);
            list.AddChild(btn);
        }
    }

    // --- Preview body (game-save hosts) ---

    private void BuildPreviewBody(bool portrait)
    {
        Control list = BuildSlotSelector();
        _preview = BuildPreviewPane();

        if (portrait)
        {
            // List above, large preview fills the rest.
            list.SizeFlagsVertical = Control.SizeFlags.Fill;
            list.CustomMinimumSize = new Vector2(0, 150);
            _body.AddChild(list);
            _body.AddChild(_preview);
        }
        else
        {
            // List rail | hairline | preview, the row filling the mid panel.
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            row.AddThemeConstantOverride("separation", 16);
            list.CustomMinimumSize = new Vector2(250, 0);
            row.AddChild(list);
            row.AddChild(new ColorRect
            {
                Color = UiPalette.LineSoft,
                CustomMinimumSize = new Vector2(1, 0),
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            });
            row.AddChild(_preview);
            _body.AddChild(row);
        }

        _body.AddChild(BuildActionRow());
    }

    /// <summary>The scrollable list of selectable (toggle) slot rows. Picking a
    /// row updates the preview + enables Load. Defaults the selection to the
    /// current <see cref="_selectedSlot"/> (or the first slot).</summary>
    private Control BuildSlotSelector()
    {
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(list);

        // Keep the prior selection if still present, else default to the first.
        if (_selectedSlot == null || !SlotPresent(_selectedSlot))
            _selectedSlot = _slots[0].SlotName;

        var group = new ButtonGroup();
        foreach (SaveSlotInfo info in _slots)
        {
            string capturedName = info.SlotName;
            var btn = new Button
            {
                Text = _labelFor(info),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left,
                ToggleMode = true,
                ButtonGroup = group,
            };
            btn.AddThemeFontSizeOverride("font_size", 17);
            btn.Toggled += on => { if (on) OnSlotSelected(capturedName); };
            AudioBus.AttachClick(btn);
            if (capturedName == _selectedSlot) btn.SetPressedNoSignal(true);
            list.AddChild(btn);
        }
        return scroll;
    }

    private MapThumbnailView BuildPreviewPane()
    {
        var preview = new MapThumbnailView
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(220, 150),
        };
        preview.SetSaveStore(_thumbnailStore!);
        return preview;
    }

    private HBoxContainer BuildActionRow()
    {
        var actions = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        actions.AddThemeConstantOverride("separation", 12);

        var cancel = new Button { Text = "Cancel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cancel.AddThemeFontSizeOverride("font_size", 18);
        cancel.Pressed += Hide;
        AudioBus.AttachClick(cancel);
        actions.AddChild(cancel);

        _loadButton = new Button
        {
            Text = "Load",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Disabled = _selectedSlot == null,
        };
        _loadButton.AddThemeFontSizeOverride("font_size", 18);
        _loadButton.Pressed += OnLoadPressed;
        AudioBus.AttachClick(_loadButton);
        actions.AddChild(_loadButton);
        return actions;
    }

    private void OnSlotSelected(string slotName)
    {
        _selectedSlot = slotName;
        if (_loadButton != null) _loadButton.Disabled = false;
        // The preview request coalesces via its own token, so rapid taps only
        // snapshot the latest. Layout is stable after the first frame, so now.
        RequestPreview(slotName);
    }

    private void OnLoadPressed()
    {
        if (_selectedSlot != null) _onPicked(_selectedSlot);
    }

    /// <summary>Render the selected slot's preview after a layout frame so the
    /// MapThumbnailView sizes against its real (large) on-screen rect.</summary>
    private async Task SchedulePreview(int token)
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (token != _previewToken || _preview == null || _selectedSlot == null) return;
        if (!GodotObject.IsInstanceValid(_preview)) return;
        RequestPreview(_selectedSlot);
    }

    private bool SlotPresent(string name)
    {
        foreach (SaveSlotInfo info in _slots)
            if (info.SlotName == name) return true;
        return false;
    }

    private static Label MakeMessageLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 18);
        return label;
    }

    /// <summary>Display a "Load failed" error inside the picker. Falls back to
    /// <see cref="GD.PushError"/> if the dialog isn't in the tree yet.</summary>
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

        _errorPanel = ModalChrome.BuildCenteredPanel(panelW: ErrorPanelW, panelH: ErrorPanelH);
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
        // Backdrop click — close the picker (don't fall through to the map
        // underneath). Modal contract.
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            _backdrop.AcceptEvent();
            Hide();
        }
    }

    // CanvasLayer.Hide() just toggles Visible — we shadow it so callers (and the
    // backdrop / Escape / Cancel paths) also clear any stacked error overlay.
    private new void Hide()
    {
        Visible = false;
        HideError();
    }

    /// <summary>Format a save timestamp consistently across all load dialogs.
    /// Local time, minute precision.</summary>
    public static string FormatTimestamp(long unixSeconds)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).LocalDateTime;
        return dt.ToString("yyyy-MM-dd HH:mm");
    }
}
