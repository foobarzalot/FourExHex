// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Fog-of-war visibility of a tile from the single human player's
/// perspective. Ordered so a higher value means more information.
/// </summary>
public enum VisibilityTier
{
    /// <summary>Never seen — render nothing, not even terrain.</summary>
    Fog = 0,

    /// <summary>Seen before but not currently in sight — render the last-seen
    /// owner/occupant, dimmed; live changes are hidden.</summary>
    Stale = 1,

    /// <summary>Currently within sight — render live.</summary>
    Visible = 2,
}
