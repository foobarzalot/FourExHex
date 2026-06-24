using System;
using Godot;

/// <summary>
/// Reusable "Map Generation" modal (issue #48 / #66 / #72): the controls that
/// shape a freshly-generated (random) map — Trees, Mountains, and Gold (each a
/// 0..25%-of-land density stepper) plus Clumping (a 0..100 sparse↔clumped
/// territory-assignment factor). Summoned by the "?" glyph button next to the die in
/// the map editor and on the New Game map-setup page; both hosts open the same
/// panel, and it reads/writes the single process-wide <see cref="GameSettings"/>
/// densities, so the choice is shared across the menu and the editor.
///
/// Backdrop + content-sized centered panel, mirroring <see cref="EscMenu"/>.
/// <see cref="Open"/>/<see cref="Close"/> drive visibility. Changing a density
/// here writes <see cref="GameSettings"/> immediately but deliberately does NOT
/// re-render any host preview — the New Game thumbnail updates only when the die
/// is pressed (a fresh seed), so adjusting a density doesn't churn the preview
/// under the open panel.
/// </summary>
public sealed partial class MapGenSettingsPanel : CanvasLayer
{
    public bool IsOpen { get; private set; }

    // Density range surfaced by every stepper: 0..25% of land, in steps of 5.
    private const int DensityMin = 0;
    private const int DensityMax = 25;
    private const int DensityStep = 5;

    // Clumping factor (issue #72) spans 0 (fragmented baseline) to 100 (one
    // contiguous blob per player), drawn from the shared nonlinear stop set in
    // MapGenOptions (also used by the per-level campaign draw).
    private static readonly int[] ClumpStops = MapGenOptions.ClumpingFactorStops;

    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private LineEdit _treesField = null!;
    private LineEdit _mountainsField = null!;
    private LineEdit _goldField = null!;
    private LineEdit _clumpingField = null!;

    private static readonly Font _serifFont =
        GD.Load<FontFile>("res://fonts/DMSerifDisplay-Regular.ttf");

    /// <summary>Build the square "?" chip that summons this panel — a real
    /// typographic question mark (serif), so it reads on mobile with no tooltip.
    /// Same affordance on the New Game map-setup page and in the map editor; the
    /// host wires <paramref name="onPressed"/> to its own panel's
    /// <see cref="Open"/>.</summary>
    public static HudIconButton MakeOpenButton(Action onPressed, float size = 68f, int? fontSize = null)
    {
        var button = new HudIconButton("?", _serifFont, fontSize ?? (int)(size * 0.5f))
        {
            CustomMinimumSize = new Vector2(size, size),
            TooltipText = "Map generation options",
        };
        button.Pressed += () => onPressed();
        AudioBus.AttachClick(button);
        return button;
    }

    public override void _Ready()
    {
        Layer = 100;
        Visible = false;
        // Always — interactive whether the host tree is paused (none of the
        // current hosts pause, but match the EscMenu/SettingsPanel convention).
        ProcessMode = ProcessModeEnum.Always;

        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        _backdrop = ModalChrome.BuildBackdrop(viewport);
        AddChild(_backdrop);

        _panel = ModalChrome.BuildCenteredPanel();
        AddChild(_panel);

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        vbox.AddThemeConstantOverride("separation", 16);
        _panel.AddChild(vbox);

        var title = new Label
        {
            Text = "Map Generation",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontOverride("font", _serifFont);
        title.AddThemeFontSizeOverride("font_size", 36);
        vbox.AddChild(title);

        vbox.AddChild(new ColorRect
        {
            Color = UiPalette.GoldDim,
            CustomMinimumSize = new Vector2(200, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        });

        vbox.AddChild(UiStepper.BuildStepperRow(
            "Trees", GameSettings.TreeDensity, DensityMin, DensityMax, DensityStep,
            OnTreesChanged, out _treesField));
        vbox.AddChild(UiStepper.BuildStepperRow(
            "Mountains", GameSettings.MountainDensity, DensityMin, DensityMax, DensityStep,
            OnMountainsChanged, out _mountainsField));
        vbox.AddChild(UiStepper.BuildStepperRow(
            "Gold", GameSettings.GoldDensity, DensityMin, DensityMax, DensityStep,
            OnGoldChanged, out _goldField));
        vbox.AddChild(UiStepper.BuildStepperRow(
            "Clumping", GameSettings.ClumpingFactor, ClumpStops,
            OnClumpingChanged, out _clumpingField));

        var back = new Button
        {
            Text = "Back",
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        back.AddThemeFontSizeOverride("font_size", 24);
        back.Pressed += Close;
        AudioBus.AttachClick(back);
        vbox.AddChild(back);
    }

    /// <summary>Show the panel, re-syncing each stepper from
    /// <see cref="GameSettings"/> (the other host may have changed it).</summary>
    public void Open()
    {
        if (IsOpen) return;
        UiStepper.Resync(_treesField, GameSettings.TreeDensity);
        UiStepper.Resync(_mountainsField, GameSettings.MountainDensity);
        UiStepper.Resync(_goldField, GameSettings.GoldDensity);
        UiStepper.Resync(_clumpingField, GameSettings.ClumpingFactor);
        IsOpen = true;
        Visible = true;
        Log.Debug(Log.LogCategory.MapGen, "MapGenSettingsPanel: opened");
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Visible = false;
    }

    private void OnTreesChanged(int density)
    {
        GameSettings.TreeDensity = density;
        Log.Debug(Log.LogCategory.MapGen, $"MapGenSettingsPanel: TreeDensity -> {density}");
    }

    private void OnMountainsChanged(int density)
    {
        GameSettings.MountainDensity = density;
        Log.Debug(Log.LogCategory.MapGen, $"MapGenSettingsPanel: MountainDensity -> {density}");
    }

    private void OnGoldChanged(int density)
    {
        GameSettings.GoldDensity = density;
        Log.Debug(Log.LogCategory.MapGen, $"MapGenSettingsPanel: GoldDensity -> {density}");
    }

    private void OnClumpingChanged(int factor)
    {
        GameSettings.ClumpingFactor = factor;
        Log.Debug(Log.LogCategory.MapGen, $"MapGenSettingsPanel: ClumpingFactor -> {factor}");
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
