// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Where on the board a replay beat's visible effect lands — the tile a
/// camera should keep on-screen while the beat plays. Null for beats
/// with no single location (EndTurn, claim/dismiss overlays, narration,
/// multi-tile viking spawns). Consumed by the demo-replay camera follow;
/// pure so the mapping is unit-testable.
/// </summary>
public static class ReplayFocus
{
    public static HexCoord? FocusCoord(ReplayBeat beat) => beat switch
    {
        ReplayMoveBeat b => b.To,
        ReplayRejectedMoveBeat b => b.To,
        ReplayBuyBeat b => b.To,
        ReplayBuildTowerBeat b => b.To,
        ReplayLongPressRallyBeat b => b.Target,
        ReplayVikingMoveBeat b => b.To,
        ReplayVikingDisembarkBeat b => b.Land,
        ReplayVikingPerishBeat b => b.Sea,
        ReplaySelectTerritoryBeat b => b.Anchor,
        _ => null,
    };
}
