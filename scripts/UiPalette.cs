using Godot;

/// <summary>
/// Design-token palette for non-player UI chrome — surfaces, lines, ink,
/// brass accents, and the in-game water. Single source of truth so view
/// code and the central <c>Theme</c> resource agree on the same shades.
///
/// Values are sourced from the visual-redesign handoff (heraldic
/// board-game direction). Player tile colors live separately on
/// <see cref="PlayerPalette"/> because they're roster-driven; everything
/// here is fixed.
/// </summary>
public static class UiPalette
{
    // Surfaces — warm dark slate.
    public static readonly Color BgDeep  = new Color("23211d"); // canvas / outer
    public static readonly Color BgPanel = new Color("312e29"); // modal panels
    public static readonly Color BgElev  = new Color("3d3934"); // raised within panel
    public static readonly Color BgRow   = new Color("373430"); // list row
    public static readonly Color BgRowH  = new Color("454039"); // list row hover
    public static readonly Color HudBar  = new Color("28251f"); // in-game / editor HUD bar (a touch darker than BgDeep)

    // Dim scrim behind every CanvasLayer modal dialog.
    public static readonly Color ModalBackdrop = new Color(0f, 0f, 0f, 0.5f);

    // Lines / chrome.
    public static readonly Color Line     = new Color("544f47");
    public static readonly Color LineSoft = new Color("454039");
    public static readonly Color LineHard = new Color("69625a");

    // Text.
    public static readonly Color Ink      = new Color("f3f1ec");
    public static readonly Color InkSoft  = new Color("d4cfc4");
    public static readonly Color InkMute  = new Color("a39d91");
    public static readonly Color InkFaint = new Color("7a746a");

    // Brass / gold (primary actions, decorative rules).
    public static readonly Color Gold     = new Color("d8b65a");
    public static readonly Color GoldDeep = new Color("a38228");
    public static readonly Color GoldDim  = new Color("7a6534");

    // Water (in-game board background). Lerped 50% between the
    // original brighter blue (#336ba6) and the heraldic slate-navy
    // (#2d3a55) — restores enough chroma that the sea reads as
    // genuinely watery without the original's neon brightness.
    public static readonly Color Water     = new Color("4a6488");
    public static readonly Color WaterDeep = new Color("30537e");
}
