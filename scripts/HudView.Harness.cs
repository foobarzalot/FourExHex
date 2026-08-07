// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>Which endgame overlay the view-matrix harness should reveal.</summary>
public enum EndgameOverlayId
{
    Victory,
    VikingsConquered,
    AiWon,
    CampaignVictory,
    Defeat,
    ClaimVictory,
}

public partial class HudView
{
    /// <summary>
    /// View-matrix harness seam (issue #63): reveal one endgame overlay so its
    /// geometry can be audited without simulating a game to its end. The six
    /// overlays are built in _Ready but revealed from
    /// <see cref="Refresh"/> off game state, and the 6AI path that reaches a
    /// real game-over swaps in headless views — so there is no other way to put
    /// a real overlay in front of a real viewport.
    ///
    /// This proves the overlays <i>lay out</i>, not that the right one is
    /// <i>chosen</i>: selection stays covered by EndgameOverlayContentTests and
    /// the controller suite.
    /// </summary>
    internal void ShowEndgameOverlayForHarness(EndgameOverlayId id)
    {
        HideEndgameOverlaysForHarness();
        OverlayFor(id).Visible = true;
        PositionEndgameOverlays();
    }

    internal void HideEndgameOverlaysForHarness()
    {
        _victoryOverlay.Visible = false;
        _vikingsConqueredOverlay.Visible = false;
        _aiWonOverlay.Visible = false;
        _campaignVictoryOverlay.Visible = false;
        _defeatOverlay.Visible = false;
        _claimVictoryOverlay.Visible = false;
    }

    private Control OverlayFor(EndgameOverlayId id) => id switch
    {
        EndgameOverlayId.Victory => _victoryOverlay,
        EndgameOverlayId.VikingsConquered => _vikingsConqueredOverlay,
        EndgameOverlayId.AiWon => _aiWonOverlay,
        EndgameOverlayId.CampaignVictory => _campaignVictoryOverlay,
        EndgameOverlayId.Defeat => _defeatOverlay,
        _ => _claimVictoryOverlay,
    };
}
