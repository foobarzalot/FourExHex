using System.Collections.Generic;

/// <summary>
/// A fog-of-war projection from the single human player's perspective, handed
/// from the controller to the view each refresh. <see cref="Visible"/> is the
/// set of coords currently in sight; <see cref="Remembered"/> is the last-seen
/// memory for every ever-seen coord. The view classifies each tile with
/// <see cref="VisibilityRules.TierOf"/> and renders the three tiers. Godot-free
/// and integer-only (all Model types), so it can cross the
/// <c>IHexMapView</c> boundary. A <c>null</c> projection means fog is off.
/// </summary>
public sealed record FogView(
    IReadOnlySet<HexCoord> Visible,
    IReadOnlyDictionary<HexCoord, RememberedTile> Remembered);
