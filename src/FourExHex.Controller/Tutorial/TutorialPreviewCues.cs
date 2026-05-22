using System;

/// <summary>
/// Visual cues for Tutorial Preview's one-and-only-legal-move UI.
/// Reads <see cref="TutorialPreview.NextPlayer0Beat"/> after every
/// <see cref="GameController.RefreshViews"/> and applies the right
/// highlights (CTA-styled HUD button, single-tile map highlight,
/// auto-selected territory) so the previewing dev always sees one
/// obvious next action. Owned by Preview wiring; <c>null</c> in
/// ordinary play.
/// </summary>
public sealed class TutorialPreviewCues
{
    private readonly TutorialPreview _preview;
    private readonly GameState _state;
    private readonly SessionState _session;
    private readonly IHudView _hud;
    private readonly IHexMapView _map;
    private readonly PlayerId _humanPlayer;
    private readonly Action<Territory?> _selectTerritory;
    private readonly Action _cancelAction;
    private TutorialNarrationDriver? _narration;
    private bool _applying;

    public TutorialPreviewCues(
        TutorialPreview preview,
        GameState state,
        SessionState session,
        IHudView hud,
        IHexMapView map,
        PlayerId humanPlayer,
        Action<Territory?> selectTerritory,
        Action cancelAction)
    {
        _preview = preview;
        _state = state;
        _session = session;
        _hud = hud;
        _map = map;
        _humanPlayer = humanPlayer;
        _selectTerritory = selectTerritory;
        _cancelAction = cancelAction;
    }

    /// <summary>
    /// Wire the narration driver after construction (two-step because
    /// the driver needs a refresh callback that talks back to the
    /// controller, while cues only need to query its
    /// <see cref="TutorialNarrationDriver.IsPresenting"/>). When the
    /// driver is presenting a tutorial-only beat, <see cref="Apply"/>
    /// is a no-op so the narration message isn't overwritten.
    /// </summary>
    public void SetNarrationDriver(TutorialNarrationDriver narration)
    {
        _narration = narration;
    }

    /// <summary>
    /// Update CTA + map highlights from the next expected player-0 beat.
    /// Safe to call recursively: re-entry from <c>selectTerritory</c> →
    /// <see cref="GameController.RefreshViews"/> → onAfterRefresh →
    /// <see cref="Apply"/> is short-circuited by an internal guard so the
    /// outer call finishes painting consistently.
    /// </summary>
    public void Apply()
    {
        if (_applying) return;
        _applying = true;
        try { ApplyCore(); }
        finally { _applying = false; }
    }

    private void ApplyCore()
    {
        // Narration takes priority: while a tutorial-only beat is
        // presenting (e.g., display-text awaiting tap), don't paint
        // cues or touch the tutorial-message panel — the narration
        // driver owns those surfaces until the player acknowledges.
        if (_narration?.IsPresenting == true) return;

        if (_state.Turns.CurrentPlayerIndex != 0)
        {
            ClearAllCtas();
            // Mid-tutorial AI turns: wipe the stale player-0 instruction
            // so e.g. "Press End Turn." doesn't linger while the opponent
            // acts. When the script is exhausted (NextPlayer0Beat null),
            // leave the panel alone so PreviewPane's "Tutorial complete."
            // toast survives.
            if (_preview.NextPlayer0Beat != null)
            {
                _hud.HideTutorialMessage();
            }
            return;
        }

        ReplayBeat? next = _preview.NextPlayer0Beat;
        if (next == null)
        {
            ClearAllCtas();
            return;
        }

        // If the player is in a mode that doesn't match what the next
        // beat requires, cancel the pending action automatically so the
        // cue for the next beat is the only thing the player sees. The
        // cancel callback triggers its own RefreshViews → Apply
        // recursion, which the re-entrancy guard short-circuits.
        if (!IsModeCompatibleWith(next, _session.Mode, _session.MoveSource))
        {
            _cancelAction();
        }

        ClearAllCtas();

        switch (next)
        {
            case ReplayEndTurnBeat _:
                _hud.SetCta(CtaButton.EndTurn, true, pulse: true);
                break;
            case ReplayBuyBeat bu:
                ApplyBuyCue(bu);
                break;
            case ReplayBuildTowerBeat bt:
                ApplyBuildTowerCue(bt);
                break;
            case ReplayMoveBeat mv:
                ApplyMoveCue(mv);
                break;
            case ReplayLongPressRallyBeat rl:
                ApplyRallyCue(rl);
                break;
            case ReplayClaimVictoryBeat _:
                _hud.SetCta(CtaButton.ClaimVictoryWinNow, true);
                break;
            case ReplayDismissClaimBeat _:
                _hud.SetCta(CtaButton.ClaimVictoryContinue, true);
                break;
            case ReplayDismissDefeatBeat _:
                _hud.SetCta(CtaButton.DefeatContinue, true);
                break;
        }

        // Drive the bottom-center message panel from the same beat +
        // post-cancel session state. Sub-step-aware (e.g. Buy beat
        // switches text once the player enters the matching Buying
        // mode). Early-return branches above intentionally don't touch
        // the panel so PreviewPane's rejection/completion text persists.
        _hud.ShowTutorialMessage(TutorialInstructionText.For(next, _state, _session));
    }

    private void ClearAllCtas()
    {
        _hud.SetCta(CtaButton.EndTurn, false, pulse: false);
        _hud.SetCta(CtaButton.BuyRecruit, false);
        _hud.SetCta(CtaButton.BuildTower, false);
        _hud.SetCta(CtaButton.ClaimVictoryWinNow, false);
        _hud.SetCta(CtaButton.ClaimVictoryContinue, false);
        _hud.SetCta(CtaButton.DefeatContinue, false);
    }

    private void ApplyBuyCue(ReplayBuyBeat bu)
    {
        Territory? territory = TerritoryLookup.FindByCapital(_state.Territories, bu.Capital);
        if (territory == null) return;
        if (_session.SelectedTerritory != territory)
        {
            _selectTerritory(territory);
        }
        // Keep the CTA up while the player still needs to press Buy
        // (Mode None or a Buying-X-below-target mode that wants further
        // escalation presses). Once they're in the matching Buying mode,
        // drop the CTA so attention shifts to the single-tile target
        // highlight.
        bool inPlaceMode = SessionState.BuyModeLevel(_session.Mode) == bu.Level;
        _hud.SetCta(CtaButton.BuyRecruit, !inPlaceMode);
        if (inPlaceMode)
        {
            _map.ShowMoveTargets(new[] { bu.To }, bu.Level);
        }
    }

    private void ApplyBuildTowerCue(ReplayBuildTowerBeat bt)
    {
        Territory? territory = TerritoryLookup.FindByCapital(_state.Territories, bt.Capital);
        if (territory == null) return;
        if (_session.SelectedTerritory != territory)
        {
            _selectTerritory(territory);
        }
        bool inPlaceMode = _session.Mode == SessionState.ActionMode.BuildingTower;
        _hud.SetCta(CtaButton.BuildTower, !inPlaceMode);
        if (inPlaceMode)
        {
            _map.ShowTowerTargets(new[] { bt.To });
        }
    }

    private void ApplyMoveCue(ReplayMoveBeat mv)
    {
        Territory? territory = TerritoryLookup.FindOwnedContaining(
            _state.Territories, _humanPlayer, mv.From);
        if (territory == null) return;
        if (_session.SelectedTerritory != territory)
        {
            _selectTerritory(territory);
        }

        // Already picked up the source unit → point at the destination.
        // Otherwise point at the source so the player picks it up first.
        if (_session.Mode == SessionState.ActionMode.MovingUnit
            && _session.MoveSource == mv.From)
        {
            UnitLevel moveLevel = LevelAtOrRecruit(mv.From);
            _map.ShowMoveTargets(new[] { mv.To }, moveLevel);
        }
        else
        {
            UnitLevel fromLevel = LevelAtOrRecruit(mv.From);
            _map.ShowMoveTargets(new[] { mv.From }, fromLevel);
        }
    }

    private void ApplyRallyCue(ReplayLongPressRallyBeat rl)
    {
        Territory? territory = TerritoryLookup.FindOwnedContaining(
            _state.Territories, _humanPlayer, rl.Target);
        if (territory == null) return;
        if (_session.SelectedTerritory != territory)
        {
            _selectTerritory(territory);
        }
        _map.ShowMoveTargets(new[] { rl.Target }, UnitLevel.Recruit);
    }

    private UnitLevel LevelAtOrRecruit(HexCoord coord)
    {
        HexTile? tile = _state.Grid.Get(coord);
        if (tile?.Occupant is Unit unit) return unit.Level;
        return UnitLevel.Recruit;
    }

    /// <summary>
    /// True iff the player's current pending-action mode is one the next
    /// beat can be executed from. EndTurn / Rally / overlay-dismiss beats
    /// require Mode == None. Buy beats accept None or any BuyingXxx
    /// (player cycles freely). BuildTower accepts None or BuildingTower.
    /// Move accepts None, or MovingUnit with MoveSource matching the
    /// beat's From tile.
    /// </summary>
    private static bool IsModeCompatibleWith(
        ReplayBeat beat, SessionState.ActionMode mode, HexCoord? moveSource) =>
        beat switch
        {
            ReplayEndTurnBeat _ => mode == SessionState.ActionMode.None,
            ReplayBuyBeat _ =>
                mode == SessionState.ActionMode.None
                || SessionState.BuyModeLevel(mode) != null,
            ReplayBuildTowerBeat _ =>
                mode == SessionState.ActionMode.None
                || mode == SessionState.ActionMode.BuildingTower,
            ReplayMoveBeat mv =>
                mode == SessionState.ActionMode.None
                || (mode == SessionState.ActionMode.MovingUnit
                    && moveSource == mv.From),
            ReplayLongPressRallyBeat _ => mode == SessionState.ActionMode.None,
            ReplayClaimVictoryBeat _ => mode == SessionState.ActionMode.None,
            ReplayDismissClaimBeat _ => mode == SessionState.ActionMode.None,
            ReplayDismissDefeatBeat _ => mode == SessionState.ActionMode.None,
            _ => true,
        };
}
