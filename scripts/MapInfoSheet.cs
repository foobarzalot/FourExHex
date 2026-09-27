// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// A "play this board?" confirm sheet: a dim backdrop plus a
/// centered dialog with a serif title, gold rule, status line, a "who you're
/// playing as" block, a large live <see cref="MapThumbnailView"/> preview, and
/// Cancel / confirm buttons. Generalized from the campaign confirm sheet so it
/// serves both the campaign ladder (one guaranteed human) and the New Game /
/// Map Editor "load starting map" flows, where a map can have multiple human
/// players, or none.
///
/// The caller supplies the title, an optional status line, the list of human
/// identities to surface, a thumbnail-request delegate (the sheet owns no
/// knowledge of seeds vs. saved maps — campaign passes a procedural request,
/// the load flow a saved-map request), and optionally its own action
/// buttons: the campaign sheet's set depends on the level's stored attempt
/// (Play / Continue + Restart / Watch Replay + Restart). The first action is
/// the primary (Enter); Cancel is always the sheet's own. Layout/chrome mirror the New Game
/// map-config screen via <see cref="LandscapeMenuChrome"/>: fills the safe area
/// on a phone, caps to 920×520 (transposed in portrait) on desktop; an
/// orientation flip rebuilds the body and re-renders.
///
/// Paging is opt-in (<see cref="Paging"/>): a sheet given a pager steps
/// between neighboring pages on a horizontal swipe or the Left/Right keys.
/// Each page is a complete dialog of its own — surface, title through
/// buttons — and two of them ride a viewport-wide <see cref="PageCarousel"/>,
/// so a page turn shows two distinct dialogs sliding past.
/// </summary>
public sealed partial class MapInfoSheet : CanvasLayer
{
    public event Action? Confirmed;
    public event Action? Canceled;

    public bool IsOpen { get; private set; }

    /// <summary>Index of the page the sheet is on (the paging target once a
    /// step commits); 0 for a sheet without paging.</summary>
    public int PageIndex { get; private set; }

    /// <summary>One human player to surface in the "playing as" block.</summary>
    public readonly record struct HumanIdentity(string Name, Color Color);

    /// <summary>One action button: the sheet closes, then runs
    /// <see cref="OnPressed"/> — unless <see cref="KeepOpen"/>, for an action
    /// that stacks its own confirm over the still-visible sheet and closes
    /// it (or not) itself.</summary>
    public readonly record struct SheetAction(string Label, Action OnPressed, bool KeepOpen = false);

    /// <summary>Everything one page shows. <see cref="GameMode"/> empty = no
    /// mode row; <see cref="GameModeEmphasis"/> golds it.
    /// <see cref="RequestThumbnail"/> configures and kicks off the preview
    /// render on the page's thumbnail.</summary>
    public sealed record SheetPage(
        string Title,
        string Status,
        IReadOnlyList<HumanIdentity> Humans,
        Action<MapThumbnailView> RequestThumbnail,
        IReadOnlyList<SheetAction> Actions,
        string GameMode = "",
        bool GameModeEmphasis = false);

    /// <summary>Opt-in paging. <see cref="Neighbor"/> maps (index, forward)
    /// to the adjacent index, or null at an end; <see cref="PageAt"/> builds
    /// that page; <see cref="CanPage"/> lets the owner suspend paging (a
    /// modal stacked over the sheet); <see cref="Stepped"/> reports a
    /// committed step as (from, to, via "swipe" | "key").</summary>
    public sealed record Paging(
        int Index,
        Func<int, SheetPage> PageAt,
        Func<int, bool, int?> Neighbor,
        Func<bool> CanPage,
        Action<int, int, string> Stepped);

    private const float MaxLong = 920f;
    private const float MaxShort = 520f;

    // Share of the finger's travel the page follows when dragged toward an
    // end of the pages, where there is no neighbor to reveal.
    private const float EndResistance = 0.3f;

    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    /// <summary>One carousel slot: a viewport-sized click-through root
    /// holding a page's own dialog surface. <see cref="Content"/> null =
    /// the slot is empty and its surface hidden.</summary>
    private sealed class Slot
    {
        public Control Root = null!;
        public PanelContainer Surface = null!;
        public BoxContainer? Body;
        public MapThumbnailView? Thumbnail;
        public SheetPage? Content;
        public int Index;
    }

    private readonly SheetPage _initial;
    private readonly Paging? _paging;
    private readonly Slot[] _slots = { new Slot(), new Slot() };
    // Horizontal-swipe paging (left = next, right = previous), fed from
    // _Input; the carousel tracks its Drag offset live.
    private readonly SwipeDetector _swipe = new SwipeDetector();

    private PageCarousel _carousel = null!;
    private ScreenOrientation _orientation;
    private bool _resizeHooked;

    public MapInfoSheet(
        string title,
        string status,
        IReadOnlyList<HumanIdentity> humans,
        Action<MapThumbnailView> requestThumbnail,
        IReadOnlyList<SheetAction>? actions = null,
        string gameMode = "",
        bool gameModeEmphasis = false)
    {
        // Defaults to a single Play action raising Confirmed.
        _initial = new SheetPage(title, status, humans, requestThumbnail,
            actions ?? new[]
            {
                new SheetAction(Strings.Get(StringKeys.ButtonPlay), () => Confirmed?.Invoke()),
            },
            gameMode, gameModeEmphasis);
    }

    public MapInfoSheet(SheetPage initial, Paging paging)
    {
        _initial = initial;
        _paging = paging;
        PageIndex = paging.Index;
    }

    public override void _Ready()
    {
        Layer = 100;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        _orientation = ScreenLayout.Resolve(viewport.X, viewport.Y);
        AddChild(ModalChrome.BuildBackdrop(viewport));

        foreach (Slot slot in _slots)
        {
            slot.Root = new Control();
            slot.Surface = LandscapeMenuChrome.Build();
            slot.Surface.Visible = false;
            slot.Root.AddChild(slot.Surface);
        }
        _carousel = new PageCarousel(_slots[0].Root, _slots[1].Root);
        AddChild(_carousel);
        Populate(_slots[0], _initial, PageIndex);

        GetViewport().SizeChanged += OnViewportResized;
        SafeArea.Changed += OnSafeAreaChanged;
        _resizeHooked = true;
        ApplyLayout();
    }

    public override void _ExitTree()
    {
        SafeArea.Changed -= OnSafeAreaChanged;
        if (!_resizeHooked) return;
        GetViewport().SizeChanged -= OnViewportResized;
        _resizeHooked = false;
    }

    private Slot SlotOf(Control root) => _slots[0].Root == root ? _slots[0] : _slots[1];

    private Slot FrontSlot => SlotOf(_carousel.Front);

    // Fill a slot with a page: a fresh body, and — once the sheet is up —
    // its preview render, so an incoming page is rendering as it slides in.
    private void Populate(Slot slot, SheetPage page, int index)
    {
        Clear(slot);
        slot.Content = page;
        slot.Index = index;
        slot.Surface.Visible = true;
        BuildBody(slot, page);
        if (IsOpen) page.RequestThumbnail(slot.Thumbnail!);
    }

    // Freeing the body takes its thumbnail out of the tree, which abandons
    // any render still in flight.
    private static void Clear(Slot slot)
    {
        slot.Body?.QueueFree();
        slot.Body = null;
        slot.Surface.Visible = false;
        slot.Thumbnail = null;
        slot.Content = null;
    }

    private void BuildBody(Slot slot, SheetPage page)
    {
        if (_orientation == ScreenOrientation.Portrait) BuildPortraitBody(slot, page);
        else BuildLandscapeBody(slot, page);
    }

    private void BuildPortraitBody(Slot slot, SheetPage page)
    {
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        slot.Surface.AddChild(col);
        slot.Body = col;

        col.AddChild(MakeTitle(page, HorizontalAlignment.Center, 36));
        col.AddChild(MakeGoldRule(Control.SizeFlags.ShrinkCenter));
        if (page.Status.Length > 0) col.AddChild(MakeStatus(page, HorizontalAlignment.Center));
        if (page.GameMode.Length > 0) col.AddChild(MakeGameMode(page, HorizontalAlignment.Center));
        col.AddChild(MakePlayingAs(page, HorizontalAlignment.Center));
        slot.Thumbnail = MakeThumbnail();
        col.AddChild(slot.Thumbnail);

        // Two buttons share a row; three or more stack (a phone-width row
        // can't fit "Watch Replay / Restart / Cancel" at this font size).
        BoxContainer buttonRow = page.Actions.Count > 1
            ? new VBoxContainer()
            : new HBoxContainer();
        buttonRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buttonRow.AddThemeConstantOverride("separation", 12);
        col.AddChild(buttonRow);
        if (page.Actions.Count > 1)
        {
            AddActionButtons(buttonRow, page);
            buttonRow.AddChild(MakeSheetButton(Strings.Get(StringKeys.ButtonCancel), Cancel));
        }
        else
        {
            buttonRow.AddChild(MakeSheetButton(Strings.Get(StringKeys.ButtonCancel), Cancel));
            AddActionButtons(buttonRow, page);
        }
    }

    private void BuildLandscapeBody(Slot slot, SheetPage page)
    {
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 20);
        slot.Surface.AddChild(hbox);
        slot.Body = hbox;

        var rail = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(240, 0),
            SizeFlagsVertical = Control.SizeFlags.Fill,
        };
        rail.AddThemeConstantOverride("separation", 10);
        hbox.AddChild(rail);

        rail.AddChild(MakeTitle(page, HorizontalAlignment.Left, 32));
        rail.AddChild(MakeGoldRule(Control.SizeFlags.ShrinkBegin));
        if (page.Status.Length > 0) rail.AddChild(MakeStatus(page, HorizontalAlignment.Left));
        if (page.GameMode.Length > 0) rail.AddChild(MakeGameMode(page, HorizontalAlignment.Left));
        rail.AddChild(MakePlayingAs(page, HorizontalAlignment.Left));
        rail.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        rail.AddChild(MakeSheetButton(Strings.Get(StringKeys.ButtonCancel), Cancel));
        AddActionButtons(rail, page);

        hbox.AddChild(new ColorRect
        {
            Color = UiPalette.LineSoft,
            CustomMinimumSize = new Vector2(1, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        slot.Thumbnail = MakeThumbnail();
        hbox.AddChild(slot.Thumbnail);
    }

    private static Label MakeTitle(SheetPage page, HorizontalAlignment align, int fontSize)
    {
        var title = new Label { Text = page.Title, HorizontalAlignment = align };
        title.AddThemeFontOverride("font", SerifFont);
        title.AddThemeFontSizeOverride("font_size", fontSize);
        return title;
    }

    private static ColorRect MakeGoldRule(Control.SizeFlags horizontal) => new()
    {
        Color = UiPalette.GoldDim,
        CustomMinimumSize = new Vector2(200, 1),
        SizeFlagsHorizontal = horizontal,
    };

    private static Label MakeStatus(SheetPage page, HorizontalAlignment align)
    {
        var status = new Label
        {
            Text = page.Status,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = align,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        status.AddThemeFontSizeOverride("font_size", 22);
        status.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        return status;
    }

    /// <summary>The game-mode line. Same wrapped style as the status
    /// row, but golded when it's the Rising Tides callout so it reads as a
    /// distinct, important note rather than ordinary metadata.</summary>
    private static Label MakeGameMode(SheetPage page, HorizontalAlignment align)
    {
        var mode = new Label
        {
            Text = page.GameMode,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = align,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        mode.AddThemeFontSizeOverride("font_size", 22);
        mode.AddThemeColorOverride("font_color",
            page.GameModeEmphasis ? UiPalette.Gold : UiPalette.InkSoft);
        return mode;
    }

    /// <summary>The "who you're playing as" block. Zero humans → a plain
    /// all-Computer note; exactly one → the campaign's tinted sentence (kept
    /// pixel-identical); two or more → a "You will be playing as:" lead-in over
    /// a wrapping row of color-swatch + name chips, one per human.</summary>
    private static Control MakePlayingAs(SheetPage page, HorizontalAlignment align)
    {
        if (page.Humans.Count == 0)
        {
            var none = new Label
            {
                Text = Strings.Get(StringKeys.MapInfoAllComputer),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = align,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            none.AddThemeFontSizeOverride("font_size", 22);
            none.AddThemeColorOverride("font_color", UiPalette.InkSoft);
            return none;
        }

        if (page.Humans.Count == 1)
        {
            HumanIdentity h = page.Humans[0];
            var label = new Label
            {
                Text = Strings.Get(StringKeys.MapInfoPlayingAs, ("name", h.Name)),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = align,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            label.AddThemeFontSizeOverride("font_size", 22);
            label.AddThemeColorOverride("font_color", h.Color);
            return label;
        }

        var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        var lead = new Label
        {
            Text = Strings.Get(StringKeys.MapInfoPlayingAsHeading),
            HorizontalAlignment = align,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        lead.AddThemeFontSizeOverride("font_size", 22);
        lead.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        col.AddChild(lead);

        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 12);
        chips.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (align == HorizontalAlignment.Center)
            chips.Alignment = BoxContainer.AlignmentMode.Center;
        foreach (HumanIdentity h in page.Humans)
        {
            var chip = new HBoxContainer();
            chip.AddThemeConstantOverride("separation", 6);
            chip.AddChild(new ColorRect
            {
                Color = h.Color,
                CustomMinimumSize = new Vector2(20, 20),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
            var name = new Label { Text = h.Name, VerticalAlignment = VerticalAlignment.Center };
            name.AddThemeFontSizeOverride("font_size", 22);
            name.AddThemeColorOverride("font_color", h.Color);
            chip.AddChild(name);
            chips.AddChild(chip);
        }
        col.AddChild(chips);
        return col;
    }

    private static MapThumbnailView MakeThumbnail() => new()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill,
    };

    /// <summary>One button per action, primary first, each closing the
    /// sheet before it runs so a scene change never races the modal.</summary>
    private void AddActionButtons(BoxContainer container, SheetPage page)
    {
        foreach (SheetAction action in page.Actions)
        {
            SheetAction captured = action;
            container.AddChild(MakeSheetButton(captured.Label, () => Run(captured)));
        }
    }

    private static Button MakeSheetButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", 24);
        button.CustomMinimumSize = new Vector2(0, 52);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    private void ApplyLayout()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        bool portrait = ScreenLayout.Resolve(vp.X, vp.Y) == ScreenOrientation.Portrait;
        // The carousel spans the viewport (its Resized handler re-homes the
        // dialogs when not transitioning); each dialog centers inside it.
        _carousel.Size = vp;
        foreach (Slot slot in _slots)
        {
            LandscapeMenuChrome.ApplyLayout(slot.Surface, vp, SafeArea.Current,
                maxW: portrait ? MaxShort : MaxLong,
                maxH: portrait ? MaxLong : MaxShort);
        }
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visible = true;
        Slot front = FrontSlot;
        front.Content!.RequestThumbnail(front.Thumbnail!);
        Log.Debug(Log.LogCategory.Display,
            $"MapInfoSheet.Open \"{front.Content.Title}\" humans={front.Content.Humans.Count} " +
            $"orient={_orientation} paging={_paging != null}");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        _swipe.Cancel();
    }

    private void Run(SheetAction action)
    {
        // No pressing a page's button while it is sliding off.
        if (_carousel.Transitioning) return;
        if (!action.KeepOpen) Close();
        Log.Debug(Log.LogCategory.Display,
            $"MapInfoSheet action \"{action.Label}\" (keepOpen={action.KeepOpen})");
        action.OnPressed();
    }

    /// <summary>Enter / the primary action: the current page's first one.</summary>
    private void Confirm() => Run(FrontSlot.Content!.Actions[0]);

    private void Cancel()
    {
        Close();
        Canceled?.Invoke();
    }

    /// <summary>
    /// Close exactly as if the user pressed Escape on the open sheet:
    /// hide and fire <see cref="Canceled"/> so the owner's teardown
    /// (null-out + QueueFree) runs. Used by the Android system-back
    /// ladder. No-op when not open.
    /// </summary>
    public void CloseAsCancel()
    {
        if (!IsOpen) return;
        Cancel();
    }

    private bool CanPage => _paging != null && IsOpen && _paging.CanPage();

    // Populate the back slot with the neighbor a drag is revealing. False
    // at an end of the pages: the back slot is emptied and nothing peeks.
    private bool EnsurePeek(float offset)
    {
        Slot back = SlotOf(_carousel.Back);
        int? target = _paging!.Neighbor(PageIndex, offset < 0f);
        if (target == null)
        {
            Clear(back);
            return false;
        }
        if (back.Content != null && back.Index == target.Value) return true;
        Populate(back, _paging.PageAt(target.Value), target.Value);
        Log.Debug(Log.LogCategory.Display, $"MapInfoSheet peek -> index {target.Value}");
        return true;
    }

    /// <summary>
    /// Animated page change, shared by swipe commits and the arrow keys:
    /// the current page slides off (from wherever the drag left it) while
    /// the neighbor slides in beside it. False when nothing stepped — an
    /// end of the pages, paging suspended, or a slide already running.
    /// </summary>
    private bool Step(bool forward, string via)
    {
        if (!CanPage || _carousel.Transitioning) return false;
        int? target = _paging!.Neighbor(PageIndex, forward);
        if (target == null)
        {
            Log.Debug(Log.LogCategory.Display,
                $"MapInfoSheet step blocked at end (index {PageIndex}, via {via})");
            return false;
        }

        // A drag peek (or the page just stepped away from) is already in
        // the back slot; otherwise build it here.
        Slot incoming = SlotOf(_carousel.Back);
        if (incoming.Content == null || incoming.Index != target.Value)
            Populate(incoming, _paging.PageAt(target.Value), target.Value);

        int from = PageIndex;
        PageIndex = target.Value;
        _carousel.Commit(forward, onLanded: () => { });
        _paging.Stepped(from, target.Value, via);
        return true;
    }

    /// <summary>Swipe paging: mouse presses are observed (not consumed) so
    /// taps still reach the buttons; touch arrives here as emulated
    /// finger-0 mouse events.</summary>
    public override void _Input(InputEvent @event)
    {
        if (_paging == null || !IsOpen) return;

        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                if (CanPage && !_carousel.Transitioning) _swipe.Press(mb.Position.X, mb.Position.Y);
                return;
            }
            bool wasTracking = _swipe.IsTrackingHorizontal;
            SwipeDirection dir = _swipe.Release(mb.Position.X, mb.Position.Y);
            if (dir == SwipeDirection.None && !wasTracking) return;

            // Page-turning: finger left = next, finger right = previous.
            if (dir == SwipeDirection.None || !Step(forward: dir == SwipeDirection.Left, via: "swipe"))
            {
                Log.Debug(Log.LogCategory.Display, $"MapInfoSheet spring back (index {PageIndex})");
                _carousel.SpringBack();
            }
            // A drag-release isn't a click anyone needs — eat it so no
            // button underneath fires.
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseMotion mm)
        {
            if (_carousel.Transitioning) return;
            float offset = _swipe.Drag(mm.Position.X, mm.Position.Y);
            if (!_swipe.IsTrackingHorizontal) return;
            bool peeking = offset == 0f || EnsurePeek(offset);
            _carousel.Track(peeking ? offset : offset * EndResistance);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode == Key.Escape)
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
        else if (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter)
        {
            // Handled first: the action may change scene, taking this
            // sheet out of the tree.
            GetViewport().SetInputAsHandled();
            Confirm();
        }
        else if (_paging != null && (keyEvent.Keycode == Key.Left || keyEvent.Keycode == Key.Right))
        {
            Step(forward: keyEvent.Keycode == Key.Right, via: "key");
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnViewportResized()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        ScreenOrientation next = ScreenLayout.Resolve(vp.X, vp.Y);
        if (next != _orientation)
        {
            _orientation = next;
            foreach (Slot slot in _slots)
            {
                if (slot.Content != null) Populate(slot, slot.Content, slot.Index);
            }
            Log.Debug(Log.LogCategory.Display,
                $"MapInfoSheet rebuilt for {_orientation} on index {PageIndex}");
        }
        ApplyLayout();
    }

    private void OnSafeAreaChanged(LogicalSafeInsets s) => ApplyLayout();
}
