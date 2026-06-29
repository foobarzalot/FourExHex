using Godot;

/// <summary>
/// Fixed colors for the game board itself — terrain art (trees), structure
/// art (castles, graves), the illegal-action rejection red, and the economy
/// status hues. Distinct from <see cref="UiPalette"/>, which owns non-player
/// UI chrome, and from <see cref="PlayerPalette"/>, which owns roster-driven
/// player tile colors. Single source of truth so the on-tile rendering in
/// <c>HexMapView</c> and the HUD swatches in <c>HudIcons</c> stay in sync.
/// </summary>
public static class BoardPalette
{
    // Illegal-action feedback (rejected unit/tower ghost, forbidden slash).
    public static readonly Color RejectRed = new Color(1f, 0.15f, 0.15f, 1f);

    // Conifer art — canopy green + trunk brown. Shared by the on-tile tree
    // and the HUD/editor tree swatch (HudIcons.DrawTree).
    public static readonly Color ForestCanopy = new Color(0.16f, 0.48f, 0.18f, 1f);
    public static readonly Color ForestTrunk  = new Color(0.36f, 0.22f, 0.1f, 1f);

    // Structure art.
    public static readonly Color CastleFill = new Color("4a4640"); // warm dark slate body
    public static readonly Color GraveCross = new Color("74706a"); // muted slate dead-unit X

    // Mountain art — Tolkien-map peak: grey rock body + white
    // snow cap. Shared by the on-tile mountain glyph and the editor swatch
    // (HudIcons.DrawMountain).
    public static readonly Color MountainRock = new Color("6f6a64"); // grey-brown stone

    // Economy status hues (selected-territory gold label + on-tile badge).
    public static readonly Color WarnRed    = Colors.Red;    // bankrupt next turn
    public static readonly Color WarnYellow = Colors.Yellow; // negative delta, still solvent
}
