using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

/// <summary>
/// Mutation / orchestration core for the game model. Extracted from
/// <see cref="GameController"/> so that the live AI path and the
/// replay step machine both call into a single shared implementation
/// without the controller becoming a circular dependency for a
/// future <c>ReplayRecorder</c> extraction. Holds direct references
/// to <see cref="GameState"/>, <see cref="SessionState"/>, and the
/// view interfaces; receives the rest (event hooks, mode-read flags,
/// undo/bookkeeping clear hooks) via constructor callbacks so the
/// controller can keep ownership of its public events and
/// recording state.
/// </summary>
public class GameOperations
{
    private readonly GameState _state;
    private readonly SessionState _session;
    private readonly IHexMapView _map;
    private readonly IHudView _hud;
    private readonly bool _recordingMode;
    private readonly Func<bool> _isReplayMode;
    private readonly Action _clearUndoAndReplayBookkeeping;
    private readonly Action? _onAfterRefresh;
    private readonly int _maxTurnNumber;
    private readonly Action _onGameEnded;
    private readonly Action _onHumanTurnStarted;
    private readonly int _masterSeed;
    private readonly Func<bool> _aiSilentMode;
    private readonly Func<bool> _isReplayInstantActive;
    private readonly bool _previewMode;
    private bool _silentBatchOverlayShown;
    private Random _rng;

    /// <summary>
    /// The per-turn RNG, reseeded at the top of every
    /// <see cref="StartPlayerTurn"/> from
    /// (masterSeed, turnNumber, currentPlayerIndex). Exposed so the
    /// controller's AI scheduler can pass it to the injected chooser
    /// without owning the field itself.
    /// </summary>
    public Random Rng => _rng;

    /// <summary>
    /// Per-turn one-shot guard so <see cref="GameController.HumanTurnStarted"/>
    /// fires exactly once at the start of a human turn (StartPlayerTurn
    /// can be entered multiple times — Resume + the AI loop's hand-back
    /// — but we only want one autosave per turn). Reset by
    /// <see cref="StartPlayerTurn"/> at its top, set true when the event
    /// fires. Public because <see cref="GameController.BeginReplay"/>
    /// also resets it, and the controller's StartGame path reads it via
    /// MaybeFireHumanTurnStartedFromStartGame.
    /// </summary>
    public bool HumanTurnFiredForCurrentTurn { get; set; }

    /// <summary>
    /// True once <see cref="CheckGameEndConditions"/> has fired
    /// <c>GameEnded</c>. Reset by BeginReplay on the controller side
    /// (via the public setter). Guards every entry into the AI step
    /// machine, replay step machine, and instant driver so a game-over
    /// signal isn't followed by further turn work.
    /// </summary>
    public bool GameEndedFired { get; set; }

    /// <summary>
    /// Mid-batch suppression of per-capture full-map redraws. The
    /// instant AI / instant replay drivers (which still live on
    /// <see cref="GameController"/>) set this true while draining
    /// beats so a capture-heavy turn doesn't re-tessellate every
    /// border per beat, then clear it before the end-of-batch
    /// repaint. <see cref="HandleCapture"/> is the read site.
    /// </summary>
    public bool SuppressMapRebuild { get; set; }

    public GameOperations(
        GameState state,
        SessionState session,
        IHexMapView map,
        IHudView hud,
        bool recordingMode,
        bool previewMode,
        Func<bool> isReplayMode,
        Func<bool> aiSilentMode,
        Func<bool> isReplayInstantActive,
        Action clearUndoAndReplayBookkeeping,
        Action onGameEnded,
        Action onHumanTurnStarted,
        int maxTurnNumber,
        int masterSeed,
        Action? onAfterRefresh)
    {
        _state = state;
        _session = session;
        _map = map;
        _hud = hud;
        _recordingMode = recordingMode;
        _previewMode = previewMode;
        _isReplayMode = isReplayMode;
        _aiSilentMode = aiSilentMode;
        _isReplayInstantActive = isReplayInstantActive;
        _clearUndoAndReplayBookkeeping = clearUndoAndReplayBookkeeping;
        _onGameEnded = onGameEnded;
        _onHumanTurnStarted = onHumanTurnStarted;
        _maxTurnNumber = maxTurnNumber;
        _masterSeed = masterSeed;
        _onAfterRefresh = onAfterRefresh;
        // Initial _rng is derived from the master seed alone;
        // ReseedRngForCurrentTurn replaces it with the proper
        // per-turn reseed at the top of every StartPlayerTurn (and
        // on Resume) before any gameplay RNG consumption.
        _rng = new Random(masterSeed);
    }

    /// <summary>
    /// Reset <see cref="Rng"/> to a fresh <see cref="Random"/> derived
    /// solely from the master seed and the current (turn, player)
    /// pair. The per-turn reseed that makes save/load deterministic:
    /// a save records only the master seed, and load reproduces
    /// identical RNG sequences regardless of how many random numbers
    /// the prior turns consumed.
    /// </summary>
    public void ReseedRngForCurrentTurn()
    {
        int subSeed = MixSeed(
            _masterSeed,
            _state.Turns.TurnNumber,
            _state.Turns.CurrentPlayerIndex);
        _rng = new Random(subSeed);
    }

    /// <summary>
    /// Deterministic 32-bit mixer over (masterSeed, turn, player).
    /// XOR-of-small-ints would correlate adjacent (turn, player) pairs;
    /// this uses three rounds of xorshift-multiply (the
    /// "splitmix32" pattern) so adjacent inputs hash to uncorrelated
    /// outputs.
    /// </summary>
    private static int MixSeed(int masterSeed, int turn, int playerIndex)
    {
        unchecked
        {
            uint x = (uint)masterSeed;
            x ^= (uint)turn * 0x9E3779B1u;
            x ^= (uint)playerIndex * 0x85EBCA77u;
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (int)x;
        }
    }

    /// <summary>
    /// True while an AI player is acting under the Instant speed
    /// setting. The AI step machine consults this to skip per-beat
    /// highlight / view-refresh calls; every top-level human input
    /// handler consults it as an input gate so input can't mutate
    /// session state between the driver's frame yields. Returns false
    /// while <see cref="SessionState.PendingDefeatScreen"/> is set:
    /// the AI batch is paused waiting for the human to dismiss the
    /// overlay.
    /// </summary>
    public bool InSilentAiBatch() =>
        _aiSilentMode()
        && _state.Turns.CurrentPlayer.IsAi
        && !_session.PendingDefeatScreen.HasValue;

    /// <summary>
    /// Tell the view to enter (or leave) silent mode based on whether
    /// the player about to act is AI and whether the user opted into
    /// Instant AI Speed. Also drives the "Opponents are taking their
    /// turns…" HUD overlay so the human knows their input is
    /// intentionally inert while the background chooser runs.
    /// </summary>
    public void RefreshSilentMode()
    {
        bool aiBatchSilent = InSilentAiBatch();
        // Instant replay also wants the view silent, and must hold it
        // across turn boundaries.
        _map.SetSilentMode(aiBatchSilent || _isReplayInstantActive());
        // Tutorial Preview / Record use the tutorial-message slot for
        // their own scripted text; don't clobber it. Outside those
        // modes the slot is free, so reuse it as a passive "AI is
        // working" indicator.
        if (_previewMode || _recordingMode) return;
        if (aiBatchSilent && !_silentBatchOverlayShown)
        {
            _hud.ShowTutorialMessage("Opponents are taking their turns…");
            _silentBatchOverlayShown = true;
        }
        else if (!aiBatchSilent && _silentBatchOverlayShown)
        {
            _hud.HideTutorialMessage();
            _silentBatchOverlayShown = false;
        }
    }

    /// <summary>
    /// Reset HasMovedThisTurn on every unit owned by
    /// <paramref name="player"/>. Called at the start of that
    /// player's turn.
    /// </summary>
    private static void ResetMovementFor(Player player, HexGrid grid)
    {
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Unit != null && tile.Unit.Owner == player.Id)
            {
                tile.Unit.HasMovedThisTurn = false;
            }
        }
    }

    /// <summary>
    /// One-line dump of per-player tile count and capital-bearing
    /// territory count, plus context — for debugging stuck game-end
    /// conditions. The whole method is <c>[Conditional("DEBUG")]</c>
    /// so the body is stripped from Release; in dev it's runtime-
    /// gated via <see cref="Log.LogCategory.Turn"/> at Info.
    /// </summary>
    [Conditional("DEBUG")]
    private void LogGameEndDiagnostics(string context)
    {
        var tiles = new Dictionary<PlayerId, int>();
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            tiles.TryGetValue(tile.Owner, out int n);
            tiles[tile.Owner] = n + 1;
        }

        var caps = new Dictionary<PlayerId, int>();
        foreach (Territory t in _state.Territories)
        {
            if (!t.HasCapital) continue;
            caps.TryGetValue(t.Owner, out int n);
            caps[t.Owner] = n + 1;
        }

        var parts = new List<string>();
        foreach (Player p in _state.Turns.Players)
        {
            tiles.TryGetValue(p.Id, out int t);
            caps.TryGetValue(p.Id, out int c);
            parts.Add($"{p.Name}:{t}t/{c}c");
        }

        Log.Info(Log.LogCategory.Turn, $"[T{_state.Turns.TurnNumber}] {context} — " +
            string.Join(", ", parts));
    }

    [Conditional("DEBUG")]
    private void LogTurnStart()
    {
        Player p = _state.Turns.CurrentPlayer;
        int tiles = 0;
        int ownedTerritories = 0;
        int totalGold = 0;
        int totalNet = 0;
        foreach (Territory t in _state.Territories)
        {
            if (t.Owner != p.Id) continue;
            ownedTerritories++;
            tiles += t.Coords.Count;
            int income = TreeRules.CountIncomeProducingTiles(t, _state.Grid);
            int upkeep = UpkeepRules.TotalUpkeepFor(t, _state.Grid);
            totalNet += income - upkeep;
            if (t.HasCapital)
            {
                totalGold += _state.Treasury.GetGold(t.Capital!.Value);
            }
        }
        Log.Info(Log.LogCategory.Turn,
            $"[T{_state.Turns.TurnNumber}] {p.Name} ({p.Kind}) turn begins — " +
            $"{tiles} tiles, {ownedTerritories} territories, " +
            $"{totalNet:+#;-#;0} net income, {totalGold}g total");
    }

    /// <summary>
    /// End-of-turn win check: the current player wins iff they're the
    /// sole owner of any capital-bearing territory — orphan singletons
    /// of other colors don't keep the game alive. Income and tree
    /// growth both run at the START of the NEXT player's turn (see
    /// <see cref="StartPlayerTurn"/>).
    /// </summary>
    public void EndOfTurnProcessing()
    {
        LogGameEndDiagnostics(
            $"end-of-turn check for {_state.Turns.CurrentPlayer.Name}");
        PlayerId? winner = WinConditionRules.WinnerAtEndOfTurn(
            _state.Turns.CurrentPlayer.Id, _state.Territories);
        if (winner.HasValue)
        {
            Log.Info(Log.LogCategory.Turn, $"[T{_state.Turns.TurnNumber}] " +
                "end-of-turn winner declared: " +
                $"{_state.Turns.Players.FirstOrDefault(p => p.Id == winner.Value)?.Name ?? "?"}");
            DeclareWinner(winner.Value);
        }
    }

    /// <summary>
    /// Advance to the next non-eliminated player. A player with no
    /// capital-bearing territory is skipped entirely — they own
    /// nothing they can act on. The end-of-turn win check guarantees
    /// the current player has a capital when this is called, so at
    /// least one player remains in the rotation and the loop always
    /// terminates.
    /// </summary>
    public void AdvanceToNextActivePlayer()
    {
        _state.Turns.EndTurn();
        while (WinConditionRules.IsEliminated(_state.Turns.CurrentPlayer.Id, _state.Grid))
        {
            Player ghost = _state.Turns.CurrentPlayer;
            if (_state.Turns.TurnNumber > 1)
            {
                TreeRules.RunStartOfTurnGrowth(
                    _state.Grid, ghost.Id, _state.WaterCoords);
            }
            UpkeepRules.ApplyUpkeepFor(
                ghost, _state.Territories, _state.Grid, _state.Treasury);
            Log.Info(Log.LogCategory.Turn, $"[T{_state.Turns.TurnNumber}] phantom turn for eliminated " +
                $"player {ghost.Name} (tree growth + upkeep)");
            _state.Turns.EndTurn();
        }
    }

    /// <summary>
    /// Start-of-turn bookkeeping for the now-current player. Order:
    /// reseed RNG, tree growth (skipped round 1), reset move flags,
    /// collect income (skipped round 1), apply upkeep (may bankrupt
    /// territories). The income → upkeep ordering matters: it lets a
    /// territory's freshly-credited income subsidize that same turn's
    /// upkeep before bankruptcy is checked. Fires
    /// <c>GameController.HumanTurnStarted</c> (via callback) iff the
    /// new current player is human, the game isn't over, and we're
    /// not in replay.
    /// </summary>
    public void StartPlayerTurn()
    {
        ReseedRngForCurrentTurn();
        HumanTurnFiredForCurrentTurn = false;
        // Toggle the view's silent flag for the player about to act,
        // BEFORE PlayBankruptcy below.
        RefreshSilentMode();

        if (_state.Turns.TurnNumber > 1)
        {
            TreeRules.RunStartOfTurnGrowth(
                _state.Grid, _state.Turns.CurrentPlayer.Id, _state.WaterCoords);
        }

        ResetMovementFor(_state.Turns.CurrentPlayer, _state.Grid);

        if (_state.Turns.TurnNumber > 1)
        {
            _state.Treasury.CollectIncomeFor(
                _state.Turns.CurrentPlayer, _state.Territories, _state.Grid);
        }

        bool anyBankrupt = UpkeepRules.ApplyUpkeepFor(
            _state.Turns.CurrentPlayer, _state.Territories, _state.Grid, _state.Treasury);
        if (anyBankrupt)
        {
            // One toll per turn-start regardless of how many of the
            // player's territories went bankrupt — see IHexMapView.
            _map.PlaySound(SoundEffect.Bankruptcy);
        }

        LogTurnStart();
        CheckGameEndConditions();

        // Fire the autosave hook for human turns. Skipped for AI
        // (autosave is keyed to human turn-start, not AI). Skipped on
        // game-over (no point saving a finished game).
        if (!_session.IsGameOver
            && !GameEndedFired
            && !_state.Turns.CurrentPlayer.IsAi
            && !HumanTurnFiredForCurrentTurn
            && !_isReplayMode())
        {
            HumanTurnFiredForCurrentTurn = true;
            _onHumanTurnStarted();
        }
    }

    /// <summary>
    /// Check for terminal game conditions — natural game over via
    /// <see cref="SessionState.IsGameOver"/>, or exceeding the
    /// constructor-provided turn cap — and fire the onGameEnded
    /// callback (which raises <c>GameController.GameEnded</c>) exactly
    /// once if either holds.
    /// </summary>
    public void CheckGameEndConditions()
    {
        if (GameEndedFired) return;

        if (_session.IsGameOver)
        {
            Player? winner = null;
            foreach (Player p in _state.Turns.Players)
            {
                if (p.Id == _session.Winner)
                {
                    winner = p;
                    break;
                }
            }
            Log.Warn(Log.LogCategory.Turn,
                $"[T{_state.Turns.TurnNumber}] GAME OVER — " +
                $"winner: {winner?.Name ?? "(none)"}");
            GameEndedFired = true;
            _onGameEnded();
            return;
        }

        if (_state.Turns.TurnNumber > _maxTurnNumber)
        {
            Log.Warn(Log.LogCategory.Turn,
                $"[T{_state.Turns.TurnNumber}] GAME OVER — " +
                $"turn cap {_maxTurnNumber} exceeded (stasis)");
            GameEndedFired = true;
            _onGameEnded();
        }
    }

    /// <summary>
    /// Push current state into both views in one call. Used after any
    /// state change (click, button press, turn end, undo/redo) — the
    /// controller's only way to update the UI.
    /// </summary>
    public void RefreshViews()
    {
        bool hasActionable = HasAnyActionableForCurrentPlayer();
        _hud.Refresh(_state, _session, hasActionable);
        _map.RefreshOccupantVisuals(_state.Turns.CurrentPlayer.Id, _state.Treasury);
        // End Turn CTA when the current player has nothing actionable
        // left. Lives here (not inside _hud.Refresh) so Tutorial Preview's
        // onAfterRefresh callback can overwrite it when the next scripted
        // beat is End Turn — "last write wins" with the preview cue.
        _hud.SetCta(CtaButton.EndTurn, !hasActionable, pulse: false);
        _onAfterRefresh?.Invoke();
    }

    /// <summary>
    /// Re-fire the onAfterRefresh callback. Used by
    /// <c>GameController.TrackHandler</c> after a human handler body
    /// runs — handler bodies sometimes paint targets / overlays AFTER
    /// their mid-body RefreshViews call, and the cue must paint last.
    /// Re-entrancy in TutorialPreviewCues.Apply is guarded separately,
    /// so the extra invocation is safe.
    /// </summary>
    public void InvokeAfterRefresh() => _onAfterRefresh?.Invoke();

    private bool HasAnyActionableForCurrentPlayer()
    {
        PlayerId color = _state.Turns.CurrentPlayer.Id;

        foreach (HexTile tile in _state.Grid.Tiles)
        {
            if (tile.Occupant is Unit unit
                && unit.Owner == color
                && !unit.HasMovedThisTurn)
            {
                return true;
            }
        }

        foreach (Territory territory in _state.Territories)
        {
            if (territory.Owner != color) continue;
            if (PurchaseRules.CanAffordPeasant(territory, _state.Treasury))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Set <see cref="SessionState.Winner"/> and fire the game-won
    /// fanfare if the winner is human. Centralized because Winner is
    /// set from multiple paths (mid-turn domination capture, end-of-
    /// turn orphan-singleton check, claim-victory, replay claim-victory).
    /// CheckGameEndConditions doesn't run after every Execute path, so
    /// the sound has to fire at the Winner-set point or it'd miss the
    /// mid-turn human win.
    /// </summary>
    public void DeclareWinner(PlayerId winnerColor)
    {
        _session.Winner = winnerColor;
        Player? winnerPlayer = _state.Turns.Players
            .FirstOrDefault(p => p.Id == winnerColor);
        if (winnerPlayer != null && !winnerPlayer.IsAi)
        {
            _map.PlaySound(SoundEffect.GameWon);
        }
    }

    /// <summary>
    /// True iff <paramref name="coord"/>'s tile is owned by
    /// <paramref name="owner"/> AND occupied by a Unit. The destination
    /// state right before a Move/PlaceNew that triggers MovementRules'
    /// combine branch — shared by all four Execute paths.
    /// </summary>
    public bool WasFriendlyUnitAt(HexCoord coord, PlayerId owner)
    {
        HexTile? tile = _state.Grid.Get(coord);
        return tile != null && tile.Owner == owner && tile.Occupant is Unit;
    }

    /// <summary>
    /// Apply a recorded AI move (also used by replay playback to
    /// re-execute a recorded move beat). Throws if the source has no
    /// unit, the unit has already moved, or the destination isn't a
    /// legal target — defense in depth against a buggy chooser. A
    /// reposition onto an own-empty cell normally consumes the unit's
    /// move so the chooser doesn't re-pick the same unit, but this
    /// "consumes the move" rule is skipped during replay so a recorded
    /// HUMAN reposition followed by another move of that unit doesn't
    /// throw.
    /// </summary>
    public void ExecuteAiMove(HexCoord source, HexCoord destination)
    {
        Territory? attacker = TerritoryLookup.FindOwnedContaining(
            _state.Territories, _state.Turns.CurrentPlayer.Id, source);
        if (attacker == null)
        {
            throw new InvalidOperationException(
                $"AI Move from {source}: that coord is not in a territory owned by " +
                $"{_state.Turns.CurrentPlayer.Name}.");
        }

        HexTile? srcTile = _state.Grid.Get(source);
        if (srcTile?.Unit == null)
        {
            throw new InvalidOperationException(
                $"AI Move from {source}: no unit on the source tile.");
        }
        if (srcTile.Unit.HasMovedThisTurn)
        {
            throw new InvalidOperationException(
                $"AI Move from {source}: unit has already moved this turn.");
        }

        List<HexCoord> legalTargets = MovementRules.ValidTargets(
            srcTile.Unit.Level, attacker, _state.Grid, _state.Territories);
        if (!legalTargets.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI Move from {source} to {destination}: destination is not a " +
                $"legal target for a {srcTile.Unit.Level}.");
        }

        HexTile? dstTile = _state.Grid.Get(destination);
        bool wasReposition = dstTile != null
            && dstTile.Owner == attacker.Owner
            && dstTile.Occupant == null;
        bool wasCombine = WasFriendlyUnitAt(destination, attacker.Owner);

        MoveResult result = MovementRules.Move(source, destination, _state.Grid, attacker);
        if (result.WasCapture)
        {
            HandleCapture($"Move {source}→{destination}");
        }
        if (result.Destroyed != null)
        {
            _map.PlayDestructionEffect(destination, result.Destroyed);
        }
        // "A reposition consumes the unit's move" is an AI-loop selection
        // concern — the human ExecuteMove path never sets this. Skip it
        // during replay: a recorded HUMAN reposition followed by another
        // move of that unit would otherwise throw "already moved this turn".
        if (wasReposition && !_isReplayMode())
        {
            Unit? movedUnit = _state.Grid.Get(destination)?.Unit;
            if (movedUnit != null) movedUnit.HasMovedThisTurn = true;
        }

        DispatchActionSound(destination, result, wasCombine);
    }

    /// <summary>
    /// Apply a recorded AI unit buy (also used by replay playback).
    /// Throws on insufficient gold or illegal target — defense in depth.
    /// </summary>
    public void ExecuteAiBuyUnit(HexCoord capital, HexCoord destination, UnitLevel level)
    {
        Territory? attacker = TerritoryLookup.FindByCapital(_state.Territories, capital);
        if (attacker == null)
        {
            throw new InvalidOperationException(
                $"AI BuyUnit with capital {capital}: no territory has that capital.");
        }
        if (!PurchaseRules.CanAfford(attacker, _state.Treasury, level))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit from capital {capital}: territory cannot afford a {level} " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g, cost = {PurchaseRules.CostFor(level)}g).");
        }

        List<HexCoord> legalTargets = MovementRules.ValidTargets(
            level, attacker, _state.Grid, _state.Territories);
        if (!legalTargets.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit to {destination} from capital {capital}: destination is " +
                $"not a legal {level} placement target.");
        }

        // Same AI semantic as ExecuteAiMove: a buy onto an own empty tile
        // is treated as consuming the fresh unit's move so the AI doesn't
        // immediately move it again next call.
        HexTile? dstTile = _state.Grid.Get(destination);
        bool wasReposition = dstTile != null
            && dstTile.Owner == attacker.Owner
            && dstTile.Occupant == null;
        bool wasCombine = WasFriendlyUnitAt(destination, attacker.Owner);

        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.CostFor(level));
        var unit = new Unit(attacker.Owner, level);
        MoveResult result = MovementRules.PlaceNew(unit, destination, _state.Grid, attacker);
        if (result.WasCapture)
        {
            HandleCapture($"Buy {level} → {destination}");
        }
        if (result.Destroyed != null)
        {
            _map.PlayDestructionEffect(destination, result.Destroyed);
        }
        // "A fresh buy onto own-empty consumes the unit's move" is an
        // AI-loop selection concern — the human ExecuteBuyAndPlace path
        // never sets this. Skip it during replay: a recorded HUMAN buy
        // followed by a move of that unit would otherwise throw "already
        // moved this turn" — exact mirror of the gate on ExecuteAiMove's
        // reposition arm above. Live AI play still consumes the move.
        if (wasReposition && !_isReplayMode())
        {
            Unit? placed = _state.Grid.Get(destination)?.Unit;
            if (placed != null) placed.HasMovedThisTurn = true;
        }

        DispatchActionSound(destination, result, wasCombine);
    }

    /// <summary>
    /// Apply a long-press rally at <paramref name="target"/> against
    /// the current state — same algorithm as the live
    /// <c>OnTileLongClickedBody</c> path, but skips the pending-mode
    /// and game-over guards and doesn't touch TrackHandler's
    /// accounting. Deterministic from current state (explicit lex-min
    /// tiebreaks in both unit selection and destination choice).
    /// </summary>
    public void ApplyLongPressRally(HexCoord target)
    {
        HexTile? tile = _state.Grid.Get(target);
        if (tile == null) return;
        PlayerId currentColor = _state.Turns.CurrentPlayer.Id;
        Territory? territory = TerritoryLookup.FindOwnedContaining(
            _state.Territories, currentColor, target);
        if (territory == null) return;

        RallyRules.ResolveRally(_state.Grid, territory, target, currentColor);
    }

    /// <summary>
    /// Apply a recorded AI tower build (also used by replay playback).
    /// Only enforces real legality (territory ownership, gold, occupancy);
    /// AI tower-spacing heuristics live in AiCommon.Enumerate at candidate
    /// generation time, never gated here — humans aren't bound by them.
    /// </summary>
    public void ExecuteAiBuildTower(HexCoord capital, HexCoord destination)
    {
        Territory? territory = TerritoryLookup.FindByCapital(_state.Territories, capital);
        if (territory == null)
        {
            throw new InvalidOperationException(
                $"AI BuildTower with capital {capital}: no territory has that capital.");
        }
        if (!PurchaseRules.CanAffordTower(territory, _state.Treasury))
        {
            throw new InvalidOperationException(
                $"AI BuildTower from capital {capital}: territory cannot afford a tower " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g).");
        }
        if (!territory.Coords.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI BuildTower at {destination} from capital {capital}: destination is " +
                $"not in that territory.");
        }
        HexTile? dst = _state.Grid.Get(destination);
        if (dst == null)
        {
            throw new InvalidOperationException(
                $"AI BuildTower at {destination}: coord is off-map.");
        }
        if (!PurchaseRules.IsValidTowerLocation(dst, territory, _state.Grid))
        {
            throw new InvalidOperationException(
                $"AI BuildTower at {destination} from capital {capital}: " +
                $"location is invalid (occupied or out-of-territory).");
        }

        _state.Treasury.SetGold(
            capital, _state.Treasury.GetGold(capital) - PurchaseRules.TowerCost);
        dst.Occupant = new Tower();
        _map.PlaySound(SoundEffect.TowerPlaced, destination);
    }

    /// <summary>
    /// Decide and fire the single audio cue for a just-resolved
    /// Move/PlaceNew. Priority: combine > destruction (by destroyed
    /// occupant type) > generic place (only if the move was consumed).
    /// Reposition onto own-empty stays silent.
    /// </summary>
    public void DispatchActionSound(HexCoord destination, MoveResult result, bool wasCombine)
    {
        if (wasCombine)
        {
            _map.PlaySound(SoundEffect.UnitCombined, destination);
            return;
        }
        switch (result.Destroyed)
        {
            case Unit:
                _map.PlaySound(SoundEffect.UnitDestroyed, destination);
                return;
            case Tower:
                _map.PlaySound(SoundEffect.TowerDestroyed, destination);
                return;
            case Tree:
            case Grave:
                _map.PlaySound(SoundEffect.TreeCleared, destination);
                return;
            case Capital:
                _map.PlaySound(SoundEffect.CapitalDestroyed, destination);
                return;
        }
        if (_state.Grid.Get(destination)?.Unit?.HasMovedThisTurn == true)
        {
            _map.PlaySound(SoundEffect.UnitPlaced, destination);
        }
    }

    /// <summary>
    /// Re-run TerritoryFinder + capital reconciliation after a tile
    /// changed ownership, fire player-defeat sound + overlay if
    /// someone just lost their last capital, optionally rebuild the
    /// view (suppressed mid-batch by <see cref="SuppressMapRebuild"/>),
    /// then check for a mid-turn domination win and end the game if so.
    /// </summary>
    public void HandleCapture(string actionDesc)
    {
        IReadOnlyList<Territory> previous = _state.Territories;
        Dictionary<HexCoord, (PlayerId Owner, int Gold)> oldCaps = SnapshotCapitals(previous);
        HashSet<PlayerId> colorsWithCapitalBefore = ColorsWithCapital(previous);

        _state.Territories = TerritoryFinder.Recompute(_state.Grid, previous, _state.Treasury);

        Dictionary<HexCoord, (PlayerId Owner, int Gold)> newCaps = SnapshotCapitals(_state.Territories);
        LogCaptureDiff(actionDesc, oldCaps, newCaps);

        // A player whose set of capital-bearing territories drops to
        // empty is freshly defeated by this capture. At most one color
        // can transition per capture (a single move/place captures one
        // tile from one color).
        HashSet<PlayerId> colorsWithCapitalAfter = ColorsWithCapital(_state.Territories);
        foreach (PlayerId c in colorsWithCapitalBefore)
        {
            if (!colorsWithCapitalAfter.Contains(c))
            {
                _map.PlaySound(SoundEffect.PlayerDefeated);
                int defeatedIndex = -1;
                for (int i = 0; i < _state.Turns.Players.Count; i++)
                {
                    if (_state.Turns.Players[i].Id == c) { defeatedIndex = i; break; }
                }
                if (defeatedIndex >= 0
                    && !_state.Turns.Players[defeatedIndex].IsAi
                    && (!_recordingMode || defeatedIndex == 0))
                {
                    _session.PendingDefeatScreen = c;
                }
            }
        }

        // Instant replay coalesces the structural redraw to once per
        // turn (see InstantReplayTick); skip the per-capture rebuild
        // here so a capture-heavy turn doesn't re-tessellate every
        // border on every beat.
        if (!SuppressMapRebuild) _map.RebuildAfterTerritoryChange();

        // Mid-turn win check: only ends the game if the current
        // player owns every cell. The "opponent reduced to orphan
        // singletons" win path is handled at end-of-turn instead
        // (see EndOfTurnProcessing). Undo is cleared so the player
        // can't rewind past the killing blow.
        PlayerId? winner = WinConditionRules.WinnerByDomination(_state.Grid);
        if (winner.HasValue)
        {
            Log.Info(Log.LogCategory.Capture, $"[T{_state.Turns.TurnNumber}] " +
                "post-capture domination winner: " +
                $"{_state.Turns.Players.FirstOrDefault(p => p.Id == winner.Value)?.Name ?? "?"}");
            DeclareWinner(winner.Value);
            _clearUndoAndReplayBookkeeping();
            // Fire GameEnded for the mid-turn capture-win path. The
            // End-Turn and claim-victory paths call CheckGameEndConditions
            // themselves; without this, TrackHandler sees IsGameOver and
            // early-returns, leaving GameEnded never raised — so Main
            // never enables the victory-overlay Replay button. The
            // GameEndedFired guard inside CheckGameEndConditions keeps
            // this idempotent if a subsequent caller fires it again.
            CheckGameEndConditions();
        }
    }

    /// <summary>
    /// Snapshot of every capital's owner + gold keyed by the capital
    /// coord. Used by the [Capture] trace to diff before/after
    /// reconcile so the log records only what actually changed.
    /// </summary>
    private Dictionary<HexCoord, (PlayerId Owner, int Gold)> SnapshotCapitals(
        IReadOnlyList<Territory> territories)
    {
        var snap = new Dictionary<HexCoord, (PlayerId Owner, int Gold)>();
        foreach (Territory t in territories)
        {
            if (!t.HasCapital) continue;
            HexCoord cap = t.Capital!.Value;
            snap[cap] = (t.Owner, _state.Treasury.GetGold(cap));
        }
        return snap;
    }

    /// <summary>
    /// Set of colors that own at least one capital-bearing territory
    /// in <paramref name="territories"/>. Used by HandleCapture to
    /// detect freshly-eliminated players (had a capital before, none
    /// after).
    /// </summary>
    private static HashSet<PlayerId> ColorsWithCapital(IReadOnlyList<Territory> territories)
    {
        var set = new HashSet<PlayerId>();
        foreach (Territory t in territories)
        {
            if (t.HasCapital) set.Add(t.Owner);
        }
        return set;
    }

    /// <summary>
    /// Print the [Capture] trace: header + one body line per
    /// capital-coord whose existence, owner, or gold changed across the
    /// reconcile. Untouched capitals are omitted so the log stays
    /// readable even on large multi-player maps.
    /// </summary>
    [Conditional("DEBUG")]
    private void LogCaptureDiff(
        string actionDesc,
        Dictionary<HexCoord, (PlayerId Owner, int Gold)> oldCaps,
        Dictionary<HexCoord, (PlayerId Owner, int Gold)> newCaps)
    {
        Log.Debug(Log.LogCategory.Capture,
            $"[Capture T{_state.Turns.TurnNumber} {_state.Turns.CurrentPlayer.Name}] {actionDesc}");

        var coords = new HashSet<HexCoord>(oldCaps.Keys);
        coords.UnionWith(newCaps.Keys);
        var sorted = new List<HexCoord>(coords);
        sorted.Sort();

        bool any = false;
        foreach (HexCoord c in sorted)
        {
            bool inOld = oldCaps.TryGetValue(c, out (PlayerId Owner, int Gold) o);
            bool inNew = newCaps.TryGetValue(c, out (PlayerId Owner, int Gold) n);
            if (inOld && inNew && o.Owner == n.Owner && o.Gold == n.Gold) continue;

            string oldStr = inOld ? $"{PlayerNameFor(o.Owner)}={o.Gold}g" : "—";
            string newStr = inNew ? $"{PlayerNameFor(n.Owner)}={n.Gold}g" : "gone";
            Log.Debug(Log.LogCategory.Capture, $"  {c}: {oldStr} → {newStr}");
            any = true;
        }
        if (!any) Log.Debug(Log.LogCategory.Capture, "  (no capital/gold changes)");
    }

    private string PlayerNameFor(PlayerId c)
    {
        foreach (Player p in _state.Turns.Players)
        {
            if (p.Id == c) return p.Name;
        }
        return c.ToString();
    }
}
