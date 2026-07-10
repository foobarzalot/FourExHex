using System;
using Godot;

/// <summary>
/// Reusable "Map Generation" modal: the controls that shape a freshly-generated
/// (random) map — Trees, Mountains, Gold (each a 0..25%-of-land density stepper)
/// plus Territories (a named dropdown over the sparse↔clumped owner-assignment
/// factor, running many→one as the factor climbs 0→100). Summoned by the "?" glyph
/// in the map editor and on the New Game page; both hosts share the single
/// process-wide <see cref="GameSettings"/> densities.
///
/// Changing a density writes <see cref="GameSettings"/> immediately but does NOT
/// re-render any host preview — the New Game thumbnail updates only when the die
/// is pressed (a fresh seed).
/// </summary>
public sealed partial class MapGenSettingsPanel : CanvasLayer
{
    public bool IsOpen { get; private set; }

    // Density range surfaced by every stepper: 0..25% of land, in steps of 5.
    private const int DensityMin = 0;
    private const int DensityMax = 25;
    private const int DensityStep = 5;

    // The "Territories" dropdown names the shared nonlinear clumping stops (also
    // used by the per-level campaign draw). String-store keys, resolved in
    // BuildTerritoryItems; names run many→one as the factor climbs: factor 0 =
    // many fragmented territories, factor 100 = one contiguous blob per player.
    // Kept index-parallel to MapGenOptions.ClumpingFactorStops.
    private static readonly string[] TerritoryNames =
    {
        StringKeys.MapGenClumpMany, StringKeys.MapGenClumpSeveral,
        StringKeys.MapGenClumpSome, StringKeys.MapGenClumpFew,
        StringKeys.MapGenClumpVeryFew, StringKeys.MapGenClumpOne,
    };
    private static readonly (string label, int id)[] TerritoryItems = BuildTerritoryItems();

    private ColorRect _backdrop = null!;
    private PanelContainer _panel = null!;
    private LineEdit _treesField = null!;
    private LineEdit _mountainsField = null!;
    private LineEdit _goldField = null!;
    private OptionButton _clumpingField = null!;

    private static (string label, int id)[] BuildTerritoryItems()
    {
        int[] stops = MapGenOptions.ClumpingFactorStops;
        if (stops.Length != TerritoryNames.Length)
        {
            throw new InvalidOperationException(
                $"TerritoryNames ({TerritoryNames.Length}) must stay index-parallel to " +
                $"ClumpingFactorStops ({stops.Length}).");
        }
        var items = new (string, int)[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            items[i] = (Strings.Get(TerritoryNames[i]), stops[i]);
        }
        return items;
    }

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
            TooltipText = Strings.Get(StringKeys.MapGenTooltipOptions),
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
            Text = Strings.Get(StringKeys.MapGenTitle),
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
            Strings.Get(StringKeys.MapGenTrees), GameSettings.TreeDensity, DensityMin, DensityMax, DensityStep,
            OnTreesChanged, out _treesField));
        vbox.AddChild(UiStepper.BuildStepperRow(
            Strings.Get(StringKeys.MapGenMountains), GameSettings.MountainDensity, DensityMin, DensityMax, DensityStep,
            OnMountainsChanged, out _mountainsField));
        vbox.AddChild(UiStepper.BuildStepperRow(
            Strings.Get(StringKeys.MapGenGold), GameSettings.GoldDensity, DensityMin, DensityMax, DensityStep,
            OnGoldChanged, out _goldField));
        vbox.AddChild(UiDropdown.BuildDropdownRow(
            Strings.Get(StringKeys.MapGenTerritories), GameSettings.ClumpingFactor, TerritoryItems,
            OnClumpingChanged, out _clumpingField));

        var back = new Button
        {
            Text = Strings.Get(StringKeys.MenuBack),
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
        UiDropdown.SelectItemById(_clumpingField, GameSettings.ClumpingFactor);
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
