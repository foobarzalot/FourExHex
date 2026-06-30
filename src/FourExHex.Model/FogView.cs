using System.Collections.Generic;

/// <summary>
/// A fog-of-war projection from the single human player's perspective, handed
/// from the controller to the view each refresh. <see cref="Visible"/> is the
/// set of coords currently in sight; <see cref="Seen"/> is every coord ever
/// seen. The view classifies each tile with <see cref="VisibilityRules.TierOf"/>:
/// visible tiles render live; seen-but-not-visible (stale) tiles render their
/// static terrain greyed + dimmed (no owner, no occupant); never-seen tiles
/// render nothing. Godot-free and integer-only, so it can cross the
/// <c>IHexMapView</c> boundary. A <c>null</c> projection means fog is off.
/// </summary>
public sealed record FogView(
    IReadOnlySet<HexCoord> Visible,
    IReadOnlySet<HexCoord> Seen);
