using System;
using Godot;

/// <summary>
/// Credits modal — backdrop + centered panel with a scrollable credits
/// blurb and a Back button. Opened from <see cref="SettingsPanel"/>'s
/// Credits button and layered one above it (Layer 101 vs 100) so it
/// renders on top of the still-visible Settings panel; closing it
/// returns to Settings.
///
/// <see cref="ProcessMode"/> = Always for the same reason as
/// <see cref="SettingsPanel"/>: Settings (and therefore this) can be
/// reached from the in-game pause flow where <c>GetTree().Paused</c> is
/// true, as well as the unpaused main menu.
/// </summary>
public sealed partial class CreditsPanel : CanvasLayer
{
    private const string CreditsText =
        "FourExHex\n\n" +
        "Created by FooBarzalot\n\n" +
        "Inspired by Slay by Sean O'Connor\n\n" +
        "Built with Godot 4.6\n\n" +
        "Coding assistance: Claude Code\n\n" +
        "UI Design: Claude Design\n\n" +
        "SFX: ElevenLabs\n\n" +
        "Fonts:\n" +
        "  DM Serif Display\n" +
        "  Geist\n" +
        "  JetBrains Mono";

    public event Action? Closed;

    public bool IsOpen { get; private set; }

    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    public override void _Ready()
    {
        // One layer above SettingsPanel (100) so it draws on top.
        Layer = 101;
        Visible = false;
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.5f),
            Position = Vector2.Zero,
            Size = viewport,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(_backdrop);

        // Centered panel — picks up the theme's slate Panel stylebox,
        // matching the Settings / Load Game modal family.
        _panel = new PanelContainer
        {
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        AddChild(_panel);

        // Match the SettingsPanel vbox min size (420 x 570) so the
        // credits modal renders at the same panel dimensions — pressing
        // Credits from Settings doesn't resize the box. The scroll area
        // below expands to absorb the slack.
        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(420, 570),
        };
        vbox.AddThemeConstantOverride("separation", 18);
        _panel.AddChild(vbox);

        var title = new Label
        {
            Text = "Credits",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        // Decorative gold rule under the title — matches the menu panels.
        var goldRule = new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        };
        vbox.AddChild(goldRule);

        // Scrollable body so long credits don't blow out the panel.
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(420, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        vbox.AddChild(scroll);

        var body = new Label
        {
            Text = CreditsText,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeFontSizeOverride("font_size", 22);
        body.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        scroll.AddChild(body);

        var backButton = new Button
        {
            Text = "Back",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        backButton.AddThemeFontSizeOverride("font_size", 24);
        backButton.Pressed += Close;
        AudioBus.AttachClick(backButton);
        vbox.AddChild(backButton);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        Visible = true;
        Log.Debug(Log.LogCategory.Input, "CreditsPanel.Open");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
        Log.Debug(Log.LogCategory.Input, "CreditsPanel.Close");
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen) return;
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        Close();
        GetViewport().SetInputAsHandled();
    }
}
