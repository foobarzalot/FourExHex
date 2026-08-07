// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Reusable modal that picks a save/map/tutorial slot to load. Used by four
/// hosts (main-menu Load Game, in-game Load Game, map-editor Load Map,
/// tutorial-builder Load Tutorial). Centralizes the load-dialog chrome so
/// changes land in one place.
///
/// Two body layouts, chosen per-open:
/// <list type="bullet">
///   <item><b>Text-only</b> (editor / tutorial hosts) — a scrollable column of
///   click-to-load buttons.</item>
///   <item><b>Preview</b> (game-save hosts, when a <see cref="SaveStore"/> is
///   passed to <see cref="ShowSlots"/>) — a selectable slot list beside a single
///   large board <see cref="MapThumbnailView"/> that updates as you pick a slot,
///   plus Cancel / Load. Mirrors the New Game map-setup page: a
///   distinct portrait (list-above-preview) vs landscape (list-rail | preview)
///   layout, rebuilt on orientation flip.</item>
/// </list>
///
/// Keyboard: Up / Down move the selection between rows (both layouts, ends are
/// walls), Enter loads the selected slot, Escape backs out. Row buttons carry
/// <c>FocusMode = None</c> like the rest of the UI, so the arrows reach
/// <see cref="_UnhandledInput"/> instead of driving Godot's focus navigation.
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
    // Title for the current open: the ctor title unless ShowSlots overrode
    // it (used by BuildBody, which also re-runs on orientation flips).
    private string _activeTitle = "";
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

    // Keyboard-navigation state, rebuilt with the body in both modes: the row
    // buttons in _slots order and the scroller they live in, so Up / Down can
    // move the selection and keep it on screen.
    private readonly List<Button> _rowButtons = new();
    private ScrollContainer? _rowScroll;

    // Held-arrow auto-repeat, driven from _Process rather than the platform's
    // key echoes — see KeyRepeater.
    private const float ArrowRepeatDelaySec = 0.25f;
    private const float ArrowRepeatIntervalSec = 0.05f;
    private readonly KeyRepeater _arrowRepeat = new(ArrowRepeatDelaySec, ArrowRepeatIntervalSec);
    private int _arrowDelta;
    private Key _arrowKey = Key.None;

    // Rendering a board thumbnail deserializes the save and rebuilds the whole
    // map's visuals, so it must not run once per row while arrowing through the
    // list. The selection moves immediately; the preview follows once the
    // selection has been still this long. Negative = nothing pending.
    private const float PreviewSettleSec = 0.12f;
    private float _previewSettleRemaining = -1f;
    private string? _previewPendingSlot;
    private ScreenOrientation _orientation = ScreenOrientation.Landscape;

    // Text-only (editor / tutorial) modal — a small fixed centered panel that
    // scales to fit. The preview (game-save) panel instead fills the safe
    // viewport up to a generous cap via LandscapeMenuChrome, matching the New
    // Game map-setup page's footprint.
    private const float TextPanelW = 560f, TextPanelH = 480f;
    private const float ErrorPanelW = 420f, ErrorPanelH = 200f;
    private const float ViewportMargin = UiMetrics.ViewportMarginPx;

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
        (float availW, float availH) = PanelFitMath.AvailableBox(vp.X, vp.Y, safe, ViewportMargin);
        panel.OffsetLeft = -w * 0.5f;
        panel.OffsetRight = w * 0.5f;
        panel.OffsetTop = -h * 0.5f;
        panel.OffsetBottom = h * 0.5f;
        float scale = PanelFitMath.ScaleToFit(w, h, availW, availH);
        panel.PivotOffset = new Vector2(w, h) * 0.5f;
        panel.Scale = new Vector2(scale, scale);
    }

    /// <summary>
    /// List navigation, handled here rather than in <see cref="_UnhandledInput"/>
    /// because Godot's GUI layer runs in between and binds Up / Down to focus
    /// navigation: a focusable Control behind the modal consumes the key-down to
    /// move focus, so only the release would ever reach unhandled input. Taking
    /// the keys in <c>_Input</c> puts this ahead of that — the same reason
    /// <see cref="EscMenu"/> offers a full key-swallow mode.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is not InputEventKey key) return;
        if (key.Keycode is not (Key.Up or Key.Down or Key.Enter or Key.KpEnter)) return;

        Log.Trace(Log.LogCategory.Input,
            $"SlotPicker: key {key.Keycode} pressed={key.Pressed} echo={key.Echo} " +
            $"rows={_rowButtons.Count} sel='{_selectedSlot}'");

        if (!key.Pressed)
        {
            // Only the arrow currently driving the repeat ends it — releasing
            // the other one mid-hold must not strand a held key.
            if (key.Keycode == _arrowKey) StopArrowRepeat();
            return;
        }
        // We schedule our own repeats; the platform's echoes are noise, but
        // still ours to swallow so they can't reach anything underneath.
        if (key.Echo)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        // The error overlay is a layer above the list: Escape (handled in
        // _UnhandledInput) dismisses it, and nothing here applies until it does.
        if (_errorPanel.Visible) return;

        switch (key.Keycode)
        {
            case Key.Up:
            case Key.Down:
                _arrowDelta = key.Keycode == Key.Up ? -1 : +1;
                _arrowKey = key.Keycode;
                _arrowRepeat.Press();
                MoveSelection(_arrowDelta);
                break;
            default: // Enter / KpEnter
                ActivateSelection();
                break;
        }
        // Consumed either way: the map camera pans on Up / Down, and focus must
        // not wander through the menu behind an open modal.
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Escape only. It is not a focus-navigation key, so it survives the
    /// GUI layer and can stay on the shared unhandled-input ladder the rest of
    /// the menu's back-out handling uses.</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != Key.Escape) return;
        if (_errorPanel.Visible) HideError();
        else Hide();
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        // A key-up can be missed while the dialog closes (or the window loses
        // focus), which would otherwise leave the list scrolling on its own.
        if (!Visible) { StopArrowRepeat(); _previewSettleRemaining = -1f; return; }

        int steps = _arrowRepeat.Advance((float)delta);
        for (int i = 0; i < steps; i++) MoveSelection(_arrowDelta);

        if (_previewSettleRemaining < 0f) return;
        _previewSettleRemaining -= (float)delta;
        if (_previewSettleRemaining > 0f) return;
        _previewSettleRemaining = -1f;
        if (_previewPendingSlot == null) return;
        Log.Debug(Log.LogCategory.Input, $"SlotPicker: preview settles on '{_previewPendingSlot}'");
        RequestPreview(_previewPendingSlot);
    }

    private void StopArrowRepeat()
    {
        _arrowRepeat.Release();
        _arrowKey = Key.None;
    }

    /// <summary>Move the selection <paramref name="delta"/> rows and keep it on
    /// screen. Shared by both body layouts — in preview mode this also swaps
    /// the board thumbnail (via <see cref="OnSlotSelected"/>) and enables
    /// Load.</summary>
    private void MoveSelection(int delta)
    {
        if (_rowButtons.Count == 0)
        {
            Log.Debug(Log.LogCategory.Input, "SlotPicker: move ignored — no rows");
            return;
        }

        int from = IndexOfSelected();
        int to = ListNavMath.Step(from, _rowButtons.Count, delta);
        if (to < 0 || to == from)
        {
            Log.Debug(Log.LogCategory.Input,
                $"SlotPicker: move ignored — {from} -> {to} of {_rowButtons.Count} (at the end)");
            return;
        }

        for (int i = 0; i < _rowButtons.Count; i++)
            _rowButtons[i].SetPressedNoSignal(i == to);
        OnSlotSelected(_slots[to].SlotName);
        RevealRow(to);

        Log.Debug(Log.LogCategory.Input,
            $"SlotPicker: {(delta < 0 ? "Up" : "Down")} {from} -> {to} '{_slots[to].SlotName}'");
        // Read the highlight back off the buttons rather than trusting the
        // write: a grouped toggle button can refuse a pressed state, which
        // looks exactly like "the selection never moved" even though the
        // index above changed. Expect a single 'X' at position `to`.
        Log.Debug(Log.LogCategory.Input, $"SlotPicker: highlight {PressedMask()}");
    }

    /// <summary>The row buttons' actual pressed state as a readable mask —
    /// <c>.X...</c> means only row 1 is highlighted.</summary>
    private string PressedMask()
    {
        var mask = new System.Text.StringBuilder(_rowButtons.Count);
        foreach (Button row in _rowButtons) mask.Append(row.ButtonPressed ? 'X' : '.');
        return mask.ToString();
    }

    /// <summary>Load the selected slot, mirroring the Load button (preview
    /// mode) and a row click (text-only mode).</summary>
    private void ActivateSelection()
    {
        if (_selectedSlot == null) return;
        Log.Debug(Log.LogCategory.Input, $"SlotPicker: Enter loads '{_selectedSlot}'");
        _onPicked(_selectedSlot);
    }

    private int IndexOfSelected()
    {
        if (_selectedSlot == null) return -1;
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].SlotName == _selectedSlot) return i;
        return -1;
    }

    /// <summary>Scroll the row at <paramref name="index"/> into view. The row's
    /// position is relative to the list box, which is exactly the scroller's
    /// content space.</summary>
    private void RevealRow(int index)
    {
        if (_rowScroll == null || index < 0 || index >= _rowButtons.Count) return;
        Button row = _rowButtons[index];
        int before = _rowScroll.ScrollVertical;
        float offset = ListNavMath.ScrollToReveal(
            row.Position.Y, row.Size.Y, _rowScroll.ScrollVertical, _rowScroll.Size.Y);
        _rowScroll.ScrollVertical = Mathf.RoundToInt(offset);
        // Row geometry of zero means the list hasn't been laid out, which would
        // make every reveal a no-op — worth seeing rather than inferring.
        Log.Debug(Log.LogCategory.Input,
            $"SlotPicker: reveal row {index} y={row.Position.Y:0} h={row.Size.Y:0} " +
            $"viewH={_rowScroll.Size.Y:0} scroll {before} -> {_rowScroll.ScrollVertical}");
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
    /// click-to-load list — the preview is opt-in per-open.</param>
    public void ShowSlots(
        IReadOnlyList<SaveSlotInfo> slots,
        string emptyMessage,
        Func<SaveSlotInfo, string> labelFor,
        Action<string> onPicked,
        SaveStore? thumbnailStore = null,
        bool previewMaps = false,
        string? titleOverride = null)
    {
        _slots = slots;
        _emptyMessage = emptyMessage;
        _labelFor = labelFor;
        _onPicked = onPicked;
        _thumbnailStore = thumbnailStore;
        // Maps live in a different directory than game saves; the preview must
        // load from the right one.
        _previewUsesMaps = previewMaps;
        // A shared instance serves multiple flows (Load Game vs the map
        // import file listing); the override retitles this open only.
        _activeTitle = titleOverride ?? _title;
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
        // The rows belong to the panel being freed; both builders re-register.
        _rowButtons.Clear();
        _rowScroll = null;
        StopArrowRepeat();
        _previewSettleRemaining = -1f;
        _previewPendingSlot = null;
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

        _body.AddChild(ModalChrome.BuildSerifTitle(
            _activeTitle.Length > 0 ? _activeTitle : _title));

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

        _rowScroll = scroll;

        if (_slots.Count == 0)
        {
            list.AddChild(MakeMessageLabel(_emptyMessage));
            return;
        }
        // ToggleMode + a shared group purely for the highlight: a click still
        // loads immediately (Pressed fires for toggle buttons too), so the
        // pressed look only ever shows where the keyboard put the selection.
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
                // Godot's focus navigation binds ui_up / ui_down and would
                // consume the arrows before _UnhandledInput sees them; the
                // pressed highlight is the selection cue, not a focus ring.
                FocusMode = Control.FocusModeEnum.None,
            };
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.Pressed += () => _onPicked(capturedName);
            AudioBus.AttachClick(btn);
            list.AddChild(btn);
            _rowButtons.Add(btn);
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
                FocusMode = Control.FocusModeEnum.None,  // see BuildTextOnlyBody
            };
            btn.AddThemeFontSizeOverride("font_size", 17);
            btn.Toggled += on => { if (on) OnSlotSelected(capturedName); };
            AudioBus.AttachClick(btn);
            if (capturedName == _selectedSlot) btn.SetPressedNoSignal(true);
            list.AddChild(btn);
            _rowButtons.Add(btn);
        }
        _rowScroll = scroll;
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

        var cancel = new Button
        {
            Text = Strings.Get(StringKeys.ButtonCancel),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        cancel.AddThemeFontSizeOverride("font_size", 18);
        cancel.Pressed += Hide;
        AudioBus.AttachClick(cancel);
        actions.AddChild(cancel);

        _loadButton = new Button
        {
            Text = Strings.Get(StringKeys.ButtonLoad),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
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
        // Deferred, not immediate: rendering the board is far too heavy to run
        // once per row while an arrow is held. _Process fires it once the
        // selection settles; MapThumbnailView's own token drops any render
        // this supersedes.
        _previewPendingSlot = slotName;
        _previewSettleRemaining = PreviewSettleSec;
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
        // Autowrap so a long empty-state message wraps instead of driving
        // the surrounding ScrollContainer sideways.
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        return label;
    }

    /// <summary>Label suffix crediting a shared map's author (" — by X"),
    /// empty for maps without one. Shared by every map-list labelFor
    /// lambda so attribution renders identically everywhere.</summary>
    public static string AuthorSuffix(SaveSlotInfo info) =>
        info.Author == null
            ? ""
            : " — " + Strings.Get(StringKeys.MenuMapAuthorTag, ("author", info.Author));

    /// <summary>Display a "Load failed" error inside the picker. Falls back to
    /// <see cref="GD.PushError"/> if the dialog isn't in the tree yet.
    /// Standalone messages belong in <see cref="NoticeModal"/>, not here.</summary>
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
        (_errorBackdrop, _errorPanel, _errorTitleLabel, _errorBodyLabel) =
            ModalChrome.BuildErrorOverlay(viewport, ErrorPanelW, ErrorPanelH, _errorTitle, HideError);
        AddChild(_errorBackdrop);
        AddChild(_errorPanel);
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
