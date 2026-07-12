using System;
using Godot;

/// <summary>
/// The paged Instructions modal (issue #134): a near-fullscreen dialog
/// teaching the game's rules, opened from the HUD Help menu. Every page
/// splits into two sub-panels — an <see cref="InstructionDemoView"/>
/// playing a looping mini-board animation of the rule, and the
/// explanatory text. Portrait stacks the demo above the text; landscape
/// puts the demo left, text right.
///
/// Interaction model matches the guided tour (<see cref="HudTour"/>):
/// Back / Next wrap through the pages, Close or Escape exits, and every
/// key is swallowed while the panel is up so HUD hotkeys can't mutate
/// the live game underneath.
/// </summary>
public sealed partial class InstructionsPanel : CanvasLayer
{
    /// <summary>One page: title/body string-store keys plus the bundled
    /// tutorial (in <c>res://tutorials/</c>) whose recorded beats drive
    /// the demo animation — null for a text-only page.</summary>
    private sealed record InstructionPage(string TitleKey, string BodyKey, string? TutorialName);

    // The ordered rule pages. Grows as content is authored (issue #134
    // ships them in phases).
    private static readonly InstructionPage[] Pages =
    {
        new(StringKeys.HudInstrTerritoriesTitle,
            StringKeys.HudInstrTerritoriesBody,
            "instr_territories"),
    };

    /// <summary>Raised once when the panel closes (Close button or
    /// Escape). The host frees this node.</summary>
    public event Action? Closed;

    private static readonly Font SerifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    // Panel inset from the viewport edges — near-fullscreen.
    private const float EdgeMarginPx = 12f;

    private readonly SaveStore _saveStore = new SaveStore();

    private InstructionDemoView _demo = null!;
    private Label _titleLabel = null!;
    private Label _bodyLabel = null!;
    private Label _pageLabel = null!;
    private BoxContainer _split = null!;
    private int _index;
    private bool _closed;

    public override void _Ready()
    {
        // Same layer family as the Help menu / pause modal — above the
        // HUD and the tour.
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;

        Viewport viewport = GetViewport();
        AddChild(ModalChrome.BuildBackdrop(viewport.GetVisibleRect().Size));

        // Near-fullscreen slate panel (theme Panel stylebox, like the
        // rest of the modal family, but edge-anchored instead of
        // content-sized).
        var panel = new PanelContainer
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = EdgeMarginPx, OffsetRight = -EdgeMarginPx,
            OffsetTop = EdgeMarginPx, OffsetBottom = -EdgeMarginPx,
        };
        AddChild(panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        panel.AddChild(root);

        // Serif page title + gold rule, matching the tour dialog.
        _titleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _titleLabel.AddThemeFontOverride("font", SerifFont);
        _titleLabel.AddThemeFontSizeOverride("font_size", 32);
        root.AddChild(_titleLabel);
        root.AddChild(new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        });

        // The two sub-panels. One BoxContainer whose Vertical flag flips
        // with the viewport orientation: portrait = demo above text,
        // landscape = demo left of text.
        _split = new BoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _split.AddThemeConstantOverride("separation", 14);
        root.AddChild(_split);

        _demo = new InstructionDemoView
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.3f,
        };
        _split.AddChild(_demo);

        _bodyLabel = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _bodyLabel.AddThemeFontSizeOverride("font_size", 20);
        _bodyLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        _split.AddChild(_bodyLabel);

        // Button row: Back / Next / Close plus the page indicator —
        // same controls as the tour dialog.
        var buttonRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        buttonRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(buttonRow);

        _pageLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
        _pageLabel.AddThemeFontSizeOverride("font_size", 18);
        _pageLabel.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        buttonRow.AddChild(_pageLabel);

        buttonRow.AddChild(MakeButton("Back", () => Step(forward: false)));
        buttonRow.AddChild(MakeButton("Next", () => Step(forward: true)));
        buttonRow.AddChild(MakeButton("Close", () => Close("button")));

        viewport.SizeChanged += ApplyOrientation;
        ApplyOrientation();

        Log.Info(Log.LogCategory.Hud, $"[instr] open: {Pages.Length} pages");
        ShowPage(0, "open");
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        button.AddThemeFontSizeOverride("font_size", 22);
        button.Pressed += onPressed;
        AudioBus.AttachClick(button);
        return button;
    }

    private void ApplyOrientation()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _split.Vertical = ScreenLayout.Resolve(vp.X, vp.Y) == ScreenOrientation.Portrait;
    }

    private void Step(bool forward)
    {
        int count = Pages.Length;
        ShowPage(((_index + (forward ? 1 : -1)) % count + count) % count,
            forward ? "next" : "back");
    }

    private void ShowPage(int index, string via)
    {
        _index = index;
        InstructionPage page = Pages[index];
        _titleLabel.Text = Strings.Get(page.TitleKey);
        _bodyLabel.Text = Strings.Get(page.BodyKey);
        _pageLabel.Text = $"{index + 1} / {Pages.Length}";
        Log.Info(Log.LogCategory.Hud,
            $"[instr] page -> {index + 1}/{Pages.Length} ({page.TitleKey}) (via {via})");

        _demo.Stop();
        if (page.TutorialName == null) return;
        try
        {
            _demo.Play(_saveStore.LoadBundledTutorial(page.TutorialName));
        }
        catch (Exception ex)
        {
            // A missing/corrupt bundled demo shouldn't take down the help
            // dialog — the page still shows its text.
            Log.Warn(Log.LogCategory.Hud,
                $"[instr] demo \"{page.TutorialName}\" failed to load: {ex.Message}");
        }
    }

    /// <summary>Swallow every key while the panel is up (same contract as
    /// the tour): Escape closes, Left/Right page, everything else is
    /// eaten so HUD hotkeys can't fire underneath.</summary>
    public override void _Input(InputEvent @event)
    {
        if (_closed) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        switch (key.Keycode)
        {
            case Key.Escape: Close("escape"); break;
            case Key.Left: Step(forward: false); break;
            case Key.Right: Step(forward: true); break;
        }
        GetViewport().SetInputAsHandled();
    }

    private void Close(string reason)
    {
        if (_closed) return;
        _closed = true;
        _demo.Stop();
        Log.Info(Log.LogCategory.Hud, $"[instr] close ({reason})");
        Closed?.Invoke();
    }
}
