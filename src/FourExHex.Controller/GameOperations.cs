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
    /// <summary>Passive HUD indicator shown while AI opponents act and
    /// the human's input is intentionally inert. Shared with
    /// <see cref="TutorialPreviewCues"/> so the live-play and
    /// tutorial-preview wording can't drift.</summary>
    public const string OpponentsTakingTurnsMessage = "Opponents are taking their turns…";

    private readonly GameState _state;

    /// <summary>Purchase costs depend on the buyer; in every operations
    /// path the buyer is the current player.</summary>
    private Difficulty CurrentDifficulty => _state.Turns.CurrentPlayer.Difficulty;
    private readonly SessionState _session;
    private readonly IHexMapView _map;
    private readonly IHudView _hud;
    private readonly bool _recordingMode;
    private readonly Func<bool> _isReplayMode;
    private readonly Action _clearUndoAndReplayBookkeeping;
    private readonly Action? _onAfterRefresh;
    // True while the controller's Automate loop is playing the human's
    // remaining moves — drives the Automate button's toggle state.
    private readonly Func<bool> _isAutomating;
    // True after automation stopped because the chooser ran dry; the
    // button greys out until undo / a manual move / end turn un-latches.
    private readonly Func<bool> _isAutomateExhausted;
    private readonly int _maxTurnNumber;
    private readonly Action _onGameEnded;
    private readonly Action _onHumanTurnStarted;
    private readonly int _masterSeed;
    private readonly Func<bool> _aiSilentMode;
    private readonly Func<bool> _isReplayInstantActive;
    // True when the human's Automate loop should run silent + chunked
    // (UserSettings.AutomateSpeed == Instant) — the automate analog of
    // _aiSilentMode. Combined with _isAutomating in InSilentAutomateBatch.
    private readonly Func<bool> _automateSilentMode;
    private readonly bool _previewMode;
    private bool _aiBatchOverlayShown;
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
        Action? onAfterRefresh,
        Func<bool>? isAutomating = null,
        Func<bool>? isAutomateExhausted = null,
        Func<bool>? automateSilentMode = null)
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
        _isAutomating = isAutomating ?? (() => false);
        _isAutomateExhausted = isAutomateExhausted ?? (() => false);
        _automateSilentMode = automateSilentMode ?? (() => false);
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
        && (_state.Turns.CurrentPlayer.IsAi || VikingPhaseActive)
        && !_session.PendingDefeatScreen.HasValue;

    /// <summary>
    /// Single input gate for every mutating human handler: input is
    /// dropped while an Instant AI batch runs (see
    /// <see cref="InSilentAiBatch"/>) and while the viking pseudo-turn is
    /// mid-flight — the turn rotation has already advanced to the human,
    /// but their <see cref="StartPlayerTurn"/> is deferred until the
    /// raiders finish, so acting now would mutate a half-built turn.
    /// Overlay-dismiss handlers (defeat / claim-victory Continue) do NOT
    /// use this — they must stay live to un-pause the phase.
    /// </summary>
    public bool HumanInputLocked => InSilentAiBatch() || VikingPhaseActive;

    /// <summary>
    /// True while the human's Automate loop is fast-forwarding under
    /// Instant speed — the automate analog of <see cref="InSilentAiBatch"/>.
    /// Feeds the cue gate (<see cref="IsSilent"/>) and the view's silent
    /// flag, but NOT <see cref="HumanInputLocked"/>: input between the
    /// batch's frame yields must stay live so it can stop the loop
    /// (interruption is a flag, never a lock).
    /// </summary>
    public bool InSilentAutomateBatch() => _automateSilentMode() && _isAutomating();

    /// <summary>
    /// Silent-mode policy for per-action cues: true while an AI runs under
    /// Instant speed, during an instant-replay fast-forward, or while the
    /// human's Automate loop fast-forwards under Instant speed. The
    /// controller consults this before emitting any per-action sound /
    /// destruction effect (<see cref="EmitSound"/> / <see cref="EmitDestruction"/>),
    /// so the views just play what they're told. A manually played human
    /// turn is never silent — silence covers only the fast-forwards.
    ///
    /// Unlike <see cref="InSilentAiBatch"/> — an input/pacing gate that
    /// opens (returns false) while a human defeat overlay is pending — this
    /// deliberately omits the <see cref="SessionState.PendingDefeatScreen"/>
    /// term. The AI action that defeats the human (destroying their capital)
    /// sets that overlay mid-action, but its own crumble/sound must stay
    /// suppressed like the rest of the silent batch.
    /// </summary>
    public bool IsSilent() =>
        (_aiSilentMode() && (_state.Turns.CurrentPlayer.IsAi || VikingPhaseActive))
        || _isReplayInstantActive()
        || InSilentAutomateBatch();

    /// <summary>
    /// Route a sound cue through the silent-mode gate. Dropped while
    /// <see cref="IsSilent"/>; otherwise forwarded to the view.
    /// </summary>
    public void EmitSound(SoundEffect kind, HexCoord? at = null)
    {
        if (IsSilent()) return;
        _map.PlaySound(kind, at);
    }

    /// <summary>
    /// Route a destruction effect through the silent-mode gate. Dropped
    /// while <see cref="IsSilent"/>; otherwise forwarded to the view.
    /// </summary>
    public void EmitDestruction(HexCoord coord, HexOccupant destroyed)
    {
        if (IsSilent()) return;
        _map.PlayDestructionEffect(coord, destroyed);
    }

    /// <summary>
    /// Tell the view to enter (or leave) silent mode (Instant AI Speed,
    /// or an instant-replay fast-forward), and independently drive the
    /// "Opponents are taking their turns…" HUD overlay. The two are
    /// deliberately decoupled: silence is Instant-only, but the overlay
    /// shows whenever an AI is acting in live play at ANY speed, so the
    /// human always knows their input is intentionally inert while the
    /// background chooser runs.
    /// </summary>
    public void RefreshSilentMode()
    {
        // Instant replay also wants the view silent, and must hold it
        // across turn boundaries. The view flag drives the view's own
        // internal tide-FX stashing (CaptureRisingTidesFx) and is the
        // input/pacing gate, so it keeps InSilentAiBatch's defeat-overlay
        // term; the per-action cue gate is IsSilent() (see EmitSound).
        _map.SetSilentMode(
            InSilentAiBatch() || _isReplayInstantActive() || InSilentAutomateBatch());
        // Tutorial Preview / Record use the tutorial-message slot for
        // their own scripted text; don't clobber it. Outside those
        // modes the slot is free, so reuse it as a passive "AI is
        // working" indicator — for paced AI just as much as Instant.
        if (_previewMode || _recordingMode) return;
        bool aiActing = !_isReplayMode()
            && !GameEndedFired
            && !_session.IsGameOver
            && (_state.Turns.CurrentPlayer.IsAi || VikingPhaseActive)
            && !_session.PendingDefeatScreen.HasValue;
        if (aiActing && !_aiBatchOverlayShown)
        {
            Log.Debug(Log.LogCategory.Ai, "[overlay] show 'Opponents…' (AI acting)");
            _hud.ShowTutorialMessage(OpponentsTakingTurnsMessage);
            _aiBatchOverlayShown = true;
        }
        else if (!aiActing && _aiBatchOverlayShown)
        {
            Log.Debug(Log.LogCategory.Ai,
                $"[overlay] hide 'Opponents…' (gameEnded={GameEndedFired} gameOver={_session.IsGameOver})");
            _hud.HideTutorialMessage();
            _aiBatchOverlayShown = false;
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

    /// <summary>
    /// One-line whole-map tree/grave census — the treepocalypse-incidence
    /// telemetry (issue #100). Emitted once per player-turn at turn end so a
    /// sweep can chart total trees vs. turn and correlate the terminal figure
    /// against map settings. <c>[Conditional("DEBUG")]</c> so the grid walk is
    /// stripped from Release; in dev it's runtime-gated on
    /// <see cref="Log.LogCategory.Tree"/> at Debug.
    /// </summary>
    [Conditional("DEBUG")]
    private void LogTreeCensus()
    {
        TreeCensus census = TreeCensus.Of(_state.Grid);
        Log.Debug(Log.LogCategory.Tree,
            $"[tree-census] T{_state.Turns.TurnNumber} land={census.LandTiles} " +
            $"trees={census.Trees} graves={census.Graves} " +
            $"owned={census.OwnedTrees} neutral={census.NeutralTrees}");
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
            int income = IncomeRules.IncomeFor(t, _state.Grid);
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
        // Rising Tides: apply the current player's locked tide
        // forecast NOW, at turn end (it was telegraphed for the whole turn). This
        // can drown the current player's own last capital — HandleNewlyDefeated
        // raises the defeat cue/overlay (including for a human) and the win check
        // below then sees the post-flood board.
        ApplyPendingTide();

        LogGameEndDiagnostics(
            $"end-of-turn check for {_state.Turns.CurrentPlayer.Name}");
        LogTreeCensus();
        // Rising Tides swaps the end-of-turn sole-capital win for
        // last-player-standing; the mid-turn domination check (HandleCapture)
        // and the human claim-victory prompt still fire, so this is not the
        // only end. Viking Raiders
        // suppresses EVERY win while the onslaught is live (raiders at sea,
        // landed, or in pending waves); after the threat clears, ordinary
        // freeform rules resume.
        PlayerId? winner =
            _state.Mode == GameMode.RisingTides
                ? WinConditionRules.LastPlayerStanding(_state.Territories)
                : VikingThreatActive
                    ? null
                    : WinConditionRules.WinnerAtEndOfTurn(
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
        SkipEliminatedCurrentPlayers();
    }

    /// <summary>
    /// The eliminated-player skip loop of <see cref="AdvanceToNextActivePlayer"/>,
    /// callable on its own: while the CURRENT player is eliminated, run their
    /// phantom turn and advance. Also used at the end of the viking
    /// pseudo-turn, which can eliminate the very player the rotation had just
    /// advanced to. Callers must guarantee at least one player still holds a
    /// capital (the viking total-wipeout path declares the Vikings winners
    /// before ever reaching this loop).
    /// </summary>
    public void SkipEliminatedCurrentPlayers()
    {
        while (WinConditionRules.IsEliminated(_state.Turns.CurrentPlayer.Id, _state.Grid))
        {
            Player ghost = _state.Turns.CurrentPlayer;
            RunNeutralPhantomTurnIfRoundStart();
            RunPhantomTurnFor(ghost.Id, ghost.Name);
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
        // Per-turn visited reset: the new player starts with a clean Tab
        // tour. Done here (the universal per-turn funnel) BEFORE the human
        // hand-off auto-selects, so the auto-selected territory's visited
        // mark survives into the turn. AI turns never mark.
        if (_session.VisitedTerritoryCapitals.Count > 0)
        {
            Log.Debug(Log.LogCategory.Input,
                $"[visited] cleared ({_session.VisitedTerritoryCapitals.Count} entries)");
            _session.VisitedTerritoryCapitals.Clear();
        }
        // The turn-scoped visited set (capital-highlight suppression +
        // all-visited End Turn CTA) resets on the same per-turn funnel.
        // Unlike the cycle set above, nothing else clears it mid-turn —
        // only undo can shrink it once the turn is underway.
        if (_session.VisitedThisTurnCapitals.Count > 0)
        {
            Log.Debug(Log.LogCategory.Input,
                $"[visited] turn-visited cleared ({_session.VisitedThisTurnCapitals.Count} entries)");
            _session.VisitedThisTurnCapitals.Clear();
        }
        _session.SelectionWasRevisit = false;
        _session.EndTurnCtaLatched = false;
        // Toggle the view's silent flag for the player about to act,
        // BEFORE PlayBankruptcy below.
        RefreshSilentMode();

        RunNeutralPhantomTurnIfRoundStart();

        // Rising Tides: FORECAST (don't apply) one of this player's
        // shore tiles at turn start, so it can be telegraphed all turn and weighed
        // by the AI; the actual demote/submerge happens at turn END in
        // EndOfTurnProcessing. The tide runs from turn 1 (unlike income/tree
        // growth, which defer to turn 2) — the very first player's turn-1 forecast
        // is seeded in GameController.Resume instead (StartPlayerTurn isn't called
        // for the initial player).
        ForecastTideForCurrentPlayer();

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
            EmitSound(SoundEffect.Bankruptcy);
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
    /// Start-of-turn processing for a territory-less owner that takes no
    /// real turn: tree growth (skipped round 1) + upkeep + log. Shared by
    /// the phantom-turn loop in <see cref="AdvanceToNextActivePlayer"/>
    /// (eliminated roster players) and the neutral owner
    /// (<see cref="PlayerId.None"/>), which is permanently in this state —
    /// it owns ground but never a capital, so it never takes a real turn yet
    /// its graves should still rot and its trees still spread. Upkeep is a
    /// no-op for neutral (its territories hold no units) but is run anyway so
    /// neutral goes through the exact same path as an eliminated player.
    /// </summary>
    private void RunPhantomTurnFor(PlayerId ownerId, string name)
    {
        // Rising Tides erodes every color present on the map, including neutral
        // and eliminated colors' leftover tiles, on their phantom turn.
        MaybeRiseTidesFor(ownerId);

        if (_state.Turns.TurnNumber > 1)
        {
            TreeRules.RunStartOfTurnGrowth(_state.Grid, ownerId, _state.WaterCoords);
        }
        UpkeepRules.ApplyUpkeepFor(
            ownerId, _state.Territories, _state.Grid, _state.Treasury);
        Log.Info(Log.LogCategory.Turn,
            $"[T{_state.Turns.TurnNumber}] phantom turn for {name} (tree growth + upkeep)");
    }

    /// <summary>
    /// Rising Tides: forecast (but do NOT apply) one of the current
    /// player's shore tiles for THIS turn, storing it on
    /// <see cref="GameState.PendingTide"/> for the telegraph, the AI's evacuation
    /// scoring, and the end-of-turn <see cref="ApplyPendingTide"/>. No
    /// <c>TurnNumber</c> gate — the tide applies from the very first turn. No-op
    /// outside Rising Tides. The selection is deterministic from the map (strict
    /// exposure ordering) and consumes no RNG.
    /// </summary>
    public void ForecastTideForCurrentPlayer()
    {
        if (_state.Mode != GameMode.RisingTides) return;
        // The forecast is the FIRST per-turn RNG consumer (it runs right after
        // ReseedRngForCurrentTurn, before any capture or AI draw), so the draw
        // lands at a fixed stream offset and reproduces on resume/replay. Only a
        // randomized-selection game consumes it; legacy games pass null and the
        // tie-break stays lex-min, leaving their RNG stream untouched.
        _state.PendingTide = RisingTidesRules.ForecastSubmerge(
            _state, _state.Turns.CurrentPlayer.Id, budget: 1, rng: TideTieBreakRng());
    }

    /// <summary>
    /// The RNG to use for the Rising Tides equal-exposure tie-break, or null
    /// for the historical lex-min order. Non-null only for a randomized-selection
    /// game (so legacy games' streams are byte-for-byte unchanged).
    /// </summary>
    private Random? TideTieBreakRng() => _state.UseRandomizedSelection ? _rng : null;

    private void ApplyPendingTide()
    {
        if (_state.Mode != GameMode.RisingTides || _state.PendingTide.Count == 0) return;
        PlayerId owner = _state.Turns.CurrentPlayer.Id;
        HashSet<PlayerId> colorsWithCapitalBefore = ColorsWithCapital(_state.Territories);
        bool changed = RisingTidesRules.ApplyForecast(_state, owner, _state.PendingTide);
        _state.PendingTide = System.Array.Empty<TideStep>();
        if (changed && !SuppressMapRebuild)
        {
            _map.RebuildAfterTerritoryChange();
        }
        if (changed)
        {
            HandleNewlyDefeated(colorsWithCapitalBefore);
        }
    }

    /// <summary>
    /// Rising Tides phantom-turn erosion: forecast AND immediately
    /// apply one of <paramref name="owner"/>'s shore tiles via
    /// <see cref="RisingTidesRules.SubmergeStep"/>. Used only for the phantom
    /// turns of neutral (<see cref="PlayerId.None"/>) and eliminated colors,
    /// which have no during-turn beat to telegraph — so unlike a real player's
    /// turn (forecast at start, apply at end) the two halves run together here.
    /// No-op outside Rising Tides and on round 1 (matching the <c>TurnNumber &gt; 1</c>
    /// gate that defers tree growth), so a freeform game's RNG stream and
    /// behaviour are byte-for-byte unchanged. Budget is fixed at 1 for now.
    /// </summary>
    private void MaybeRiseTidesFor(PlayerId owner)
    {
        if (_state.Mode != GameMode.RisingTides || _state.Turns.TurnNumber <= 1) return;
        HashSet<PlayerId> colorsWithCapitalBefore = ColorsWithCapital(_state.Territories);
        bool changed = RisingTidesRules.SubmergeStep(_state, owner, budget: 1, rng: TideTieBreakRng());
        // A submerge removes tiles (or demotes a mountain) — the land/water
        // tessellation is structural, so it needs the coalesced repaint path,
        // not just the per-turn RefreshOccupantVisuals. Mirror HandleCapture's
        // SuppressMapRebuild gate so an instant fast-forward still coalesces to
        // one rebuild at batch end.
        if (changed && !SuppressMapRebuild)
        {
            _map.RebuildAfterTerritoryChange();
        }
        // A sinking shore tile can drown a player's last capital — raise the
        // same defeat cue/overlay a capture would.
        if (changed)
        {
            HandleNewlyDefeated(colorsWithCapitalBefore);
        }
    }

    /// <summary>
    /// Neutral ground (<see cref="PlayerId.None"/>) is a permanently
    /// territory-less owner, so it takes a phantom turn (tree growth +
    /// no-op upkeep) once per round rather than once per player —
    /// otherwise neutral ground would grow N× faster on an N-player map.
    /// Anchored to slot 0's visit each round (active
    /// <see cref="StartPlayerTurn"/> or the phantom-turn branch in
    /// <see cref="AdvanceToNextActivePlayer"/>, whichever handles player
    /// index 0), and skipped on round 1 to match the per-player growth's
    /// <c>TurnNumber &gt; 1</c> guard.
    /// </summary>
    private void RunNeutralPhantomTurnIfRoundStart()
    {
        if (_state.Turns.TurnNumber <= 1 || _state.Turns.CurrentPlayerIndex != 0)
        {
            return;
        }
        RunPhantomTurnFor(PlayerId.None, "Neutral");
    }

    // --- Viking Raiders pseudo-turn ----------------------------------------

    /// <summary>
    /// True while the viking pseudo-turn is mid-flight: the rotation has
    /// advanced to the round's first player but their
    /// <see cref="StartPlayerTurn"/> is deferred until the raiders finish.
    /// Set/cleared by <see cref="BeginVikingTurn"/> /
    /// <see cref="CompleteVikingTurn"/>; gates human input
    /// (<see cref="HumanInputLocked"/>), silent mode, and the
    /// "Opponents…" overlay.
    /// </summary>
    public bool VikingPhaseActive { get; private set; }

    /// <summary>Live viking threat (mode-gated; always false outside
    /// Viking Raiders). While true, every win path is suppressed.</summary>
    public bool VikingThreatActive => VikingRaidersRules.ThreatRemains(_state);

    /// <summary>
    /// True when this round's viking pseudo-turn still has to run:
    /// <see cref="VikingRaidersRules.TurnDue"/> (the pure state predicate,
    /// also driving the HUD's neutral turn-order swatch) plus the game-live
    /// gates. Checked by the three
    /// <c>AdvanceToNextActivePlayer(); StartPlayerTurn();</c> seams (which
    /// defer StartPlayerTurn) and by the turn driver's dispatch boundaries
    /// (which run the phase).
    /// </summary>
    public bool VikingTurnPending =>
        !GameEndedFired
        && !_session.IsGameOver
        && VikingRaidersRules.TurnDue(_state);

    /// <summary>
    /// Enter the viking pseudo-turn: reseed the RNG onto the vikings' own
    /// per-round stream (playerIndex −1 — save/load-safe regardless of how
    /// many draws the surrounding turns consumed), un-spend the landed
    /// raiders' moves, and flip the phase flag (input lock + overlay).
    /// </summary>
    public void BeginVikingTurn()
    {
        // Last round's perish markers wash away as the new raider turn opens.
        _state.Vikings.ClearSeaGraves();
        _rng = new Random(MixSeed(_masterSeed, _state.Turns.TurnNumber, playerIndex: -1));
        foreach (HexTile tile in _state.Grid.Tiles)
        {
            if (tile.Unit is { } u && u.Owner.IsNone)
            {
                u.HasMovedThisTurn = false;
            }
        }
        VikingPhaseActive = true;
        RefreshSilentMode();
        Log.Info(Log.LogCategory.Viking,
            $"[viking] T{_state.Turns.TurnNumber} phase begin: " +
            $"atSea={_state.Vikings.AtSea.Count} landed={CountLandedVikings()} " +
            $"nextWave={_state.Vikings.NextWaveIndex}/{VikingRaidersRules.TotalWaves}");
    }

    /// <summary>
    /// Finish the viking pseudo-turn: mark the round done, check for a
    /// total wipeout (the raiders destroyed every capital → the Vikings win
    /// outright), and drop the phase flag. The caller (turn driver) then
    /// runs the deferred <see cref="StartPlayerTurn"/> — unless the game
    /// just ended.
    /// </summary>
    public void CompleteVikingTurn()
    {
        _state.Vikings.LastCompletedRound = _state.Turns.TurnNumber;
        VikingPhaseActive = false;
        Log.Info(Log.LogCategory.Viking,
            $"[viking] T{_state.Turns.TurnNumber} phase complete: " +
            $"atSea={_state.Vikings.AtSea.Count} landed={CountLandedVikings()} " +
            $"wavesLeft={VikingRaidersRules.TotalWaves - _state.Vikings.NextWaveIndex}");
        MaybeLogVikingThreatCleared();
        // No future viking turn will run once the threat is gone — don't
        // leave this round's perish markers floating forever.
        if (!VikingThreatActive)
        {
            _state.Vikings.ClearSeaGraves();
        }
        if (ColorsWithCapital(_state.Territories).Count == 0)
        {
            Log.Warn(Log.LogCategory.Turn,
                $"[T{_state.Turns.TurnNumber}] the Vikings destroyed every capital — Vikings win");
            DeclareWinner(PlayerId.None);
            CheckGameEndConditions();
        }
        RefreshSilentMode();
    }

    /// <summary>
    /// The sea raider at <paramref name="sea"/> lands on <paramref name="land"/>:
    /// a capture when the tile is player-owned (full <see cref="HandleCapture"/>
    /// reconcile, defeat overlays, gated domination check), a reposition-like
    /// landing when it is already neutral. The landed unit is spent for this
    /// phase. Validates against <see cref="VikingRaidersRules.DisembarkTargets"/>
    /// — an illegal landing means a buggy sequencer, so it throws.
    /// </summary>
    public void ExecuteVikingDisembark(HexCoord sea, HexCoord land)
    {
        SeaViking? viking = null;
        foreach (SeaViking v in _state.Vikings.AtSea)
        {
            if (v.Coord == sea) { viking = v; break; }
        }
        if (viking == null)
        {
            throw new InvalidOperationException(
                $"Viking disembark from {sea}: no sea viking there.");
        }
        if (!VikingRaidersRules.DisembarkTargets(_state, sea, viking.Value.Level).Contains(land))
        {
            throw new InvalidOperationException(
                $"Viking disembark {sea}→{land}: not a legal landing for a {viking.Value.Level}.");
        }

        _state.Vikings.RemoveAtSea(sea);
        HexTile tile = _state.Grid.Get(land)!;
        bool wasCapture = !tile.Owner.IsNone;
        HexOccupant? displaced = tile.Occupant;
        tile.Owner = PlayerId.None;
        tile.Occupant = new Unit(PlayerId.None, viking.Value.Level) { HasMovedThisTurn = true };
        Log.Info(Log.LogCategory.Viking,
            $"[viking] disembark {viking.Value.Level} {sea}→{land}" +
            (wasCapture ? " (capture)" : " (neutral landing)"));

        if (wasCapture)
        {
            HandleCapture($"Viking disembark {sea}→{land}");
        }
        if (displaced != null)
        {
            EmitDestruction(land, displaced);
        }
        DispatchActionSound(land, new MoveResult(wasCapture, displaced), wasCombine: false);
    }

    /// <summary>The sea raider at <paramref name="sea"/> has no landing
    /// site and dies at sea.</summary>
    public void ExecuteVikingPerish(HexCoord sea)
    {
        if (!_state.Vikings.RemoveAtSea(sea))
        {
            throw new InvalidOperationException(
                $"Viking perish at {sea}: no sea viking there.");
        }
        // Leave a grave marker on the water (cosmetic — the view plays the
        // same shrink-into-grave choreography a land bankruptcy does; the
        // grave washes away when the next viking turn begins, since a grave
        // on water can never become a tree) and sound the Rising Tides
        // submerge "bloop".
        _state.Vikings.AddSeaGrave(sea);
        Log.Info(Log.LogCategory.Viking, $"[viking] perished at sea {sea} (no landing site)");
        EmitSound(SoundEffect.TileSubmerged, sea);
        MaybeLogVikingThreatCleared();
    }

    /// <summary>
    /// Spawn a wave: park the action's pre-drawn placements at sea and
    /// advance the schedule cursor. Always the LAST action of a viking
    /// turn, and <see cref="VikingState.LastSpawnRound"/> keeps the fresh
    /// wave from disembarking before the next round.
    /// </summary>
    public void ExecuteVikingSpawnWave(VikingSpawnWaveAction action)
    {
        if (action.WaveIndex != _state.Vikings.NextWaveIndex)
        {
            throw new InvalidOperationException(
                $"Viking spawn wave {action.WaveIndex}: schedule cursor is at " +
                $"{_state.Vikings.NextWaveIndex}.");
        }
        foreach (SeaViking spawn in action.Spawns)
        {
            if (_state.Vikings.HasVikingAt(spawn.Coord))
            {
                throw new InvalidOperationException(
                    $"Viking spawn at {spawn.Coord}: coord already holds a sea viking.");
            }
            _state.Vikings.AddAtSea(spawn);
        }
        _state.Vikings.NextWaveIndex++;
        _state.Vikings.LastSpawnRound = _state.Turns.TurnNumber;
        Log.Info(Log.LogCategory.Viking,
            $"[viking] wave {action.WaveIndex} spawned: {action.Spawns.Count} raiders at " +
            $"{string.Join(",", action.Spawns.Select(s => $"{s.Coord}:{s.Level}"))}");
        // One longship-arrival cue per wave (not per raider).
        if (action.Spawns.Count > 0)
        {
            EmitSound(SoundEffect.VikingArrival, action.Spawns[0].Coord);
        }
    }

    /// <summary>A landed viking's ordinary land move (capture or
    /// defensive reposition) — the viking-owner flavor of
    /// <see cref="ExecuteAiMove"/>.</summary>
    public void ExecuteVikingMove(HexCoord source, HexCoord destination)
        => ExecuteMoveCore(PlayerId.None, source, destination);

    private int CountLandedVikings()
    {
        int n = 0;
        foreach (HexTile t in _state.Grid.Tiles)
        {
            if (t.Occupant is Unit u && u.Owner.IsNone) n++;
        }
        return n;
    }

    // One-shot "threat cleared" Info line, fired the moment the last
    // viking (sea, landed, or scheduled) is gone. Checked from the two
    // places the threat can end: a viking-phase step (perish / phase
    // completion) and a player capture killing the last landed raider.
    private bool _vikingThreatClearedLogged;

    private void MaybeLogVikingThreatCleared()
    {
        if (_vikingThreatClearedLogged) return;
        if (_state.Mode != GameMode.VikingRaiders) return;
        if (VikingThreatActive) return;
        _vikingThreatClearedLogged = true;
        Log.Info(Log.LogCategory.Viking,
            $"[viking] T{_state.Turns.TurnNumber} threat cleared — " +
            "ordinary win conditions restored");
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
        long tWhole = Log.Stamp();
        bool hasActionable = HasAnyActionableForCurrentPlayer();
        long tHud = Log.Stamp();
        _hud.Refresh(_state, _session, hasActionable);
        Log.Since(Log.LogCategory.Capture, "[hitch] HudView.Refresh", tHud);
        // Fog Of War: render from the single human's perspective — recompute
        // their sight, refresh last-seen memory, and push the projection BEFORE
        // occupants so the occupant pass sees the current visibility. Keyed off
        // the human (not the current player) so fog stays stable through AI
        // turns. null = fog off → the view renders everything normally.
        _map.ShowFog(ComputeFogView());
        long tOccupants = Log.Stamp();
        // Visited capitals lose their pending-action highlight — except
        // the one the player is actively working: a newly-visited (not
        // revisited) selected territory keeps its capital lit until its
        // actions run out (the view's own affordability check ends the
        // pulse) or it stops being the selection.
        IReadOnlySet<HexCoord> suppressedCapitals = _session.VisitedThisTurnCapitals;
        if (!_session.SelectionWasRevisit
            && _session.SelectedTerritory?.Capital is HexCoord workedCapital
            && suppressedCapitals.Contains(workedCapital))
        {
            var minusWorked = new HashSet<HexCoord>(suppressedCapitals);
            minusWorked.Remove(workedCapital);
            suppressedCapitals = minusWorked;
        }
        _map.RefreshOccupantVisuals(_state.Turns.CurrentPlayer.Id, _state.Treasury,
            suppressedCapitals);
        Log.Since(Log.LogCategory.Capture, "[hitch] RefreshOccupantVisuals", tOccupants);
        // Rising Tides: telegraph the current player's locked tide
        // forecast for the whole turn ("these tiles erode at turn end"). Empty
        // outside Rising Tides, which clears any prior telegraph.
        _map.ShowTideForecast(_state.PendingTide);
        // Viking Raiders: raiders waiting at sea + this round's perish
        // markers. Empty outside the mode (and once a wave lands), which
        // clears any prior glyphs.
        _map.ShowSeaVikings(_state.Vikings.AtSea, _state.Vikings.SeaGraves);
        // End Turn CTA when the current player has nothing actionable
        // left, OR (human only) when every actionable territory has been
        // visited this turn AND the player isn't still working the
        // selected one — "you've seen everything and finished (or left)
        // the last stop; end the turn". A completed automation run
        // (exhausted latch) counts as finished regardless of what it
        // left selected; the latch clears on any manual mutation, undo/
        // redo, or turn end. Lives here (not inside _hud.Refresh) so
        // Tutorial Preview's onAfterRefresh callback can overwrite it
        // when the next scripted beat is End Turn — "last write wins"
        // with the preview cue.
        bool isHuman = !_state.Turns.CurrentPlayer.IsAi;
        Territory? sel = _session.SelectedTerritory;
        bool selExhausted = sel == null || !TerritoryHasAvailableAction(sel);
        bool allVisited = isHuman && hasActionable && AllActionableTerritoriesVisited();
        bool endTurnShouldLight = !hasActionable
            || (allVisited && (selExhausted || _isAutomateExhausted()));
        // Sticky: once lit on a human turn the CTA latches, surviving
        // later selections. The latch lives in SessionState and rides
        // the undo snapshot, so undoing past the step that lit it (or
        // starting the next turn) is the only way back to dark.
        if (isHuman && endTurnShouldLight && !_session.EndTurnCtaLatched)
        {
            _session.EndTurnCtaLatched = true;
            Log.Debug(Log.LogCategory.Hud, "[cta] EndTurn latched");
        }
        // Human-only, like every HUD affordance: AI turns keep the CTA
        // dark even when the AI has nothing actionable.
        bool endTurnCta = isHuman && (endTurnShouldLight || _session.EndTurnCtaLatched);
        _hud.SetCta(CtaButton.EndTurn, endTurnCta, pulse: false);
        // Next-Territory CTA: highlight the star when the current human
        // player has somewhere actionable to jump to but their current
        // selection (if any) is itself exhausted or is a revisit of a
        // territory already toured this turn. Suppressed on AI turns —
        // the star is a human-input affordance — and whenever the End
        // Turn CTA holds (one clear signal wins). Same "last write wins"
        // policy as EndTurn so Tutorial Preview can override.
        bool nextCta = isHuman && hasActionable
            && (selExhausted || _session.SelectionWasRevisit)
            && !endTurnCta;
        _hud.SetCta(CtaButton.NextTerritory, nextCta, pulse: false);
        Log.Debug(Log.LogCategory.Hud,
            $"[cta] EndTurn={endTurnCta} NextTerritory={nextCta} (isHuman={isHuman}, " +
            $"hasActionable={hasActionable}, allVisited={allVisited}, " +
            $"selExhausted={selExhausted}, revisit={_session.SelectionWasRevisit}, " +
            $"automateExhausted={_isAutomateExhausted()})");
        // Automate toggle: enabled on a human turn with actions remaining
        // (or while running, so Stop stays reachable); running while the
        // loop is active. Hidden outright (not drawn) in the tutorial
        // Record / Preview modes, where automated moves would desync the
        // script; disabled in replay and after the exhaustion latch.
        bool automating = _isAutomating();
        bool automateVisible = !_previewMode && !_recordingMode;
        bool automateEnabled = automateVisible
            && isHuman
            && !_session.IsGameOver && !GameEndedFired
            && !_isReplayMode()
            && !_isAutomateExhausted()
            && (hasActionable || automating);
        _hud.SetAutomateState(automateEnabled, automating, automateVisible);
        _onAfterRefresh?.Invoke();
        Log.Since(Log.LogCategory.Capture, "[hitch] RefreshViews total", tWhole);
    }

    /// <summary>
    /// Set the highlight (null clears) and immediately refresh the
    /// views. The pair appears in both the AI and replay step
    /// machines plus several game-end / dismissal sites — a one-line
    /// helper de-duplicates without inventing new abstraction.
    /// </summary>
    public void ShowHighlightAndRefresh(Territory? selected)
    {
        _map.ShowHighlight(selected);
        RefreshViews();
    }

    // Max wall-clock a single instant tick may spend draining steps
    // before it yields a frame. Small so the main thread stays
    // responsive mid-fast-forward — input, camera pan/zoom and
    // rendering all run between ticks. A mid-turn budget break yields
    // WITHOUT a redraw (nothing visual changed the user needs yet);
    // the screen is repainted only at turn boundaries. Shared by
    // instant replay and live-AI instant.
    private const int InstantBudgetMs = 8;

    /// <summary>
    /// Shared chunked, frame-yielded fast-forward loop behind both
    /// instant replay (<see cref="ReplayRecorder"/>) and live-AI
    /// instant (<see cref="AiTurnDriver"/>). Drains <paramref name="step"/>
    /// with no per-step visual work (captures skip their rebuild via
    /// <see cref="SuppressMapRebuild"/>; sound/VFX/tweens off via
    /// silent mode), repaints the whole board exactly once per turn,
    /// and caps each tick at <see cref="InstantBudgetMs"/> so a huge
    /// turn still yields frames (pan/zoom/input stay alive) without
    /// redrawing until that turn ends. Reschedules itself via
    /// <c>ScheduleUnscaled</c> — the driver owns its cadence; the speed
    /// multiplier must not touch these delays.
    /// </summary>
    public void RunInstantTick(
        Func<bool> active, Func<InstantStep> step,
        Action onExhausted, Action<bool> reschedule)
    {
        if (!active()) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool turnBoundary = false;
        SuppressMapRebuild = true;
        while (true)
        {
            InstantStep s = step();
            if (s == InstantStep.Exhausted)
            {
                SuppressMapRebuild = false;
                onExhausted();
                return;
            }
            if (s == InstantStep.TurnBoundary) { turnBoundary = true; break; }
            if (sw.ElapsedMilliseconds >= InstantBudgetMs) break;
        }
        SuppressMapRebuild = false;

        // Repaint only when a turn just completed. A budget-driven
        // break mid-turn yields a bare frame (input/camera stay live)
        // and resumes next tick — no redraw until the turn boundary.
        if (turnBoundary)
        {
            _map.RebuildAfterTerritoryChange();
            RefreshViews();
        }
        // Re-dispatch through the caller's scheduler (NOT a fixed
        // self-reschedule) so a mid-run speed change can switch off the
        // instant track here. The scheduler owns the delay per track.
        reschedule(turnBoundary);
    }

    /// <summary>
    /// Build the fog-of-war projection for the view, or null when fog is off.
    /// Fog requires exactly one human player (guaranteed by the Fog Of War menu
    /// lock); anything else fails safe to null (no fog) rather than guessing a
    /// perspective. Recomputes the human's sight and refreshes their last-seen
    /// memory as a side effect — the only place fog memory is updated.
    /// </summary>
    private FogView? ComputeFogView()
    {
        // Victory (or an opponent's decisive win) lifts the fog — reveal the
        // whole map. Defeat by elimination is handled inside BuildProjection.
        if (_state.FogEnabled && _session.IsGameOver)
        {
            Log.Debug(Log.LogCategory.Fog, "[fog] revealed (game over)");
            return null;
        }
        FogView? fog = VisibilityRules.BuildProjection(_state);
        if (fog != null)
            Log.Debug(Log.LogCategory.Fog,
                $"[fog] visible={fog.Visible.Count} seen={_state.Seen.Count}");
        return fog;
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
            if (PurchaseRules.CanAffordRecruit(territory, _state.Treasury, CurrentDifficulty))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Per-territory variant of <see cref="HasAnyActionableForCurrentPlayer"/>:
    /// true iff the current player can take some legal action on
    /// <paramref name="territory"/> right now — either it contains an
    /// unmoved current-player unit, or the capital has enough gold to
    /// buy the cheapest unit (a recruit). Tower cost (15g) is a strict
    /// superset of recruit cost (10g), so checking recruit alone covers
    /// every purchase. Used by <c>GameController.StepTerritorySelection</c>
    /// to skip past territories where the player has nothing to do, and
    /// by <see cref="RefreshViews"/> to decide whether to highlight the
    /// Next-Territory star CTA.
    /// </summary>
    internal bool TerritoryHasAvailableAction(Territory territory)
    {
        if (PurchaseRules.CanAffordRecruit(territory, _state.Treasury, CurrentDifficulty))
        {
            return true;
        }
        PlayerId color = _state.Turns.CurrentPlayer.Id;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = _state.Grid.Get(coord);
            if (tile?.Occupant is Unit unit
                && unit.Owner == color
                && !unit.HasMovedThisTurn)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True iff every current-player territory that still has an
    /// available action (<see cref="TerritoryHasAvailableAction"/>) has
    /// been visited this turn. Vacuously true with nothing actionable.
    /// A capital-less (singleton) territory with an unmoved unit can
    /// never be visited, so it conservatively holds the CTA off until
    /// the unit moves.
    /// </summary>
    private bool AllActionableTerritoriesVisited()
    {
        PlayerId color = _state.Turns.CurrentPlayer.Id;
        foreach (Territory territory in _state.Territories)
        {
            if (territory.Owner != color) continue;
            if (!TerritoryHasAvailableAction(territory)) continue;
            if (territory.Capital is not HexCoord capital
                || !_session.VisitedThisTurnCapitals.Contains(capital))
            {
                return false;
            }
        }
        return true;
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
        // Any pending UI intent (buy/build/move + RepeatedMovement chain
        // bit) is meaningless once the game is over — the action panel's
        // "Click to place a ..." hint must not bleed through the win
        // overlay. Clear pending action + chain bit + map overlays here
        // so every game-over path (claim-victory WinNow, capture of last
        // capital, turn-cap domination) is consistently quiet.
        _session.RepeatedMovement = false;
        _session.ClearPendingAction();
        _map.ClearAllOverlays();
        Player? winnerPlayer = _state.Turns.Players
            .FirstOrDefault(p => p.Id == winnerColor);
        if (winnerPlayer != null && !winnerPlayer.IsAi)
        {
            EmitSound(SoundEffect.GameWon);
        }
    }

    /// <summary>
    /// True iff <paramref name="coord"/>'s tile is owned by
    /// <paramref name="owner"/> AND occupied by a Unit. The destination
    /// state right before a Move/PlaceNew that triggers MovementRules'
    /// combine branch — shared by all four Execute paths.
    /// </summary>
    public bool WasFriendlyUnitAt(HexCoord coord, PlayerId owner)
        => AiActionCore.IsFriendlyUnitAt(coord, owner, _state);

    /// <summary>
    /// Apply a recorded AI move (also used by replay playback to
    /// re-execute a recorded move beat). Throws if the source has no
    /// unit, the unit has already moved, or the destination isn't a
    /// legal target — defense in depth against a buggy chooser.
    /// <see cref="Unit.HasMovedThisTurn"/> changes only through
    /// <see cref="MovementRules.ResolveArrival"/> (movement-consuming
    /// arrivals), so a reposition leaves the unit actionable for every
    /// actor and replays with no special casing.
    /// </summary>
    public void ExecuteAiMove(HexCoord source, HexCoord destination)
        => ExecuteMoveCore(_state.Turns.CurrentPlayer.Id, source, destination);

    /// <summary>
    /// Owner-parameterized body shared by <see cref="ExecuteAiMove"/>
    /// (owner = current player) and <see cref="ExecuteVikingMove"/>
    /// (owner = <see cref="PlayerId.None"/>, whose phase runs while the
    /// current player may be a human).
    /// </summary>
    private void ExecuteMoveCore(PlayerId owner, HexCoord source, HexCoord destination)
    {
        string actorName = owner.IsNone ? "the Vikings" : _state.Turns.CurrentPlayer.Name;
        Territory? attacker = TerritoryLookup.FindOwnedContaining(
            _state.Territories, owner, source);
        if (attacker == null)
        {
            throw new InvalidOperationException(
                $"AI Move from {source}: that coord is not in a territory owned by " +
                $"{actorName}.");
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

        AiApplyResult r = AiActionCore.Move(source, destination, _state, attacker);
        if (r.Move.WasCapture)
        {
            HandleCapture($"Move {source}→{destination}", attacker.Capital);
        }
        if (r.Move.Destroyed != null)
        {
            EmitDestruction(destination, r.Move.Destroyed);
        }

        DispatchActionSound(destination, r.Move, r.WasCombine);
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
        if (!PurchaseRules.CanAfford(attacker, _state.Treasury, level, CurrentDifficulty))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit from capital {capital}: territory cannot afford a {level} " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g, cost = {PurchaseRules.CostFor(level, CurrentDifficulty)}g).");
        }

        List<HexCoord> legalTargets = MovementRules.ValidTargets(
            level, attacker, _state.Grid, _state.Territories);
        if (!legalTargets.Contains(destination))
        {
            throw new InvalidOperationException(
                $"AI BuyUnit to {destination} from capital {capital}: destination is " +
                $"not a legal {level} placement target.");
        }

        AiApplyResult r = AiActionCore.Buy(capital, destination, level, _state, attacker);
        if (r.Move.WasCapture)
        {
            HandleCapture($"Buy {level} → {destination}", capital);
        }
        if (r.Move.Destroyed != null)
        {
            EmitDestruction(destination, r.Move.Destroyed);
        }

        DispatchActionSound(destination, r.Move, r.WasCombine);
    }

    /// <summary>
    /// Apply a phase-2b AI buy-and-combine action: purchase a unit of
    /// <paramref name="level"/> from <paramref name="capital"/>'s treasury
    /// and combine it onto the existing friendly unit at
    /// <paramref name="combineTarget"/>. The combined unit inherits the
    /// target's <c>HasMovedThisTurn=false</c> so it remains actionable for
    /// a subsequent phase-1 capture. Does NOT mark the unit as moved (unlike
    /// buy-reposition). Throws if the territory can't afford the buy or if
    /// <paramref name="combineTarget"/> doesn't hold a combinable unit.
    /// </summary>
    public void ExecuteAiBuyCombine(HexCoord capital, HexCoord combineTarget, UnitLevel level)
    {
        Territory? attacker = TerritoryLookup.FindByCapital(_state.Territories, capital);
        if (attacker == null)
        {
            throw new InvalidOperationException(
                $"AI BuyCombine with capital {capital}: no territory has that capital.");
        }
        if (!PurchaseRules.CanAfford(attacker, _state.Treasury, level, CurrentDifficulty))
        {
            throw new InvalidOperationException(
                $"AI BuyCombine from capital {capital}: territory cannot afford a {level} " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g, cost = {PurchaseRules.CostFor(level, CurrentDifficulty)}g).");
        }
        HexTile? dstTile = _state.Grid.Get(combineTarget);
        if (dstTile?.Unit == null)
        {
            throw new InvalidOperationException(
                $"AI BuyCombine to {combineTarget}: no unit on the target tile.");
        }
        if (!level.CanCombineWith(dstTile.Unit.Level))
        {
            throw new InvalidOperationException(
                $"AI BuyCombine to {combineTarget}: a {level} cannot combine with the " +
                $"{dstTile.Unit.Level} there (level sum exceeds Commander).");
        }

        MoveResult result = AiActionCore.BuyCombine(capital, combineTarget, level, _state, attacker);
        // A buy-combine onto a friendly unit is never a capture.
        if (result.Destroyed != null)
        {
            EmitDestruction(combineTarget, result.Destroyed);
        }
        DispatchActionSound(combineTarget, result, wasCombine: true);
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
    /// Enforces the universal placement rule
    /// (<see cref="PurchaseRules.IsValidTowerLocation"/>: empty +
    /// in-territory) for every actor. The AI's make-way sequence moves
    /// a resident unit aside as its own discrete beat before this one
    /// (lowered at the chooser boundary), so an occupied destination
    /// here is always a bug.
    /// </summary>
    public void ExecuteAiBuildTower(HexCoord capital, HexCoord destination)
    {
        Territory? territory = TerritoryLookup.FindByCapital(_state.Territories, capital);
        if (territory == null)
        {
            throw new InvalidOperationException(
                $"AI BuildTower with capital {capital}: no territory has that capital.");
        }
        if (!PurchaseRules.CanAffordTower(territory, _state.Treasury, CurrentDifficulty))
        {
            throw new InvalidOperationException(
                $"AI BuildTower from capital {capital}: territory cannot afford a tower " +
                $"(treasury = {_state.Treasury.GetGold(capital)}g).");
        }
        if (!territory.Contains(destination))
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

        AiActionCore.BuildTower(capital, destination, _state, territory);
        EmitSound(SoundEffect.TowerPlaced, destination);
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
            EmitSound(SoundEffect.UnitCombined, destination);
            return;
        }
        switch (result.Destroyed)
        {
            case Unit:
                EmitSound(SoundEffect.UnitDestroyed, destination);
                return;
            case Tower:
                EmitSound(SoundEffect.TowerDestroyed, destination);
                return;
            case Tree:
            case Grave:
                EmitSound(SoundEffect.TreeCleared, destination);
                return;
            case Capital:
                EmitSound(SoundEffect.CapitalDestroyed, destination);
                return;
        }
        if (_state.Grid.Get(destination)?.Unit?.HasMovedThisTurn == true)
        {
            EmitSound(SoundEffect.UnitPlaced, destination);
        }
    }

    /// <summary>
    /// Re-run TerritoryFinder + capital reconciliation after a tile
    /// changed ownership, fire player-defeat sound + overlay if
    /// someone just lost their last capital, optionally rebuild the
    /// view (suppressed mid-batch by <see cref="SuppressMapRebuild"/>),
    /// then check for a mid-turn domination win and end the game if so.
    /// <paramref name="originCapital"/> is the acting territory's capital
    /// (moves: the source territory's; buys: the purchasing one) — in a
    /// <see cref="GameState.UseOriginMergeCapital"/> game a same-owner merge
    /// keeps that capital. Honored only when the flag is set, so legacy
    /// games reconcile with arguments identical to before the rule existed.
    /// </summary>
    public void HandleCapture(string actionDesc, HexCoord? originCapital = null)
    {
        long tWhole = Log.Stamp();
        IReadOnlyList<Territory> previous = _state.Territories;
        Dictionary<HexCoord, (PlayerId Owner, int Gold)> oldCaps = SnapshotCapitals(previous);
        HashSet<PlayerId> colorsWithCapitalBefore = ColorsWithCapital(previous);

        long tRecompute = Log.Stamp();
        _state.Territories = TerritoryFinder.Recompute(
            _state.Grid, previous, _state.Treasury, _state.UseRandomizedSelection,
            _state.UseOriginMergeCapital ? originCapital : null);
        Log.Since(Log.LogCategory.Capture, "[hitch] TerritoryFinder.Recompute", tRecompute);
        Log.Debug(Log.LogCategory.Capture,
            $"[hitch] counts tiles={_state.Grid.Count} territories={_state.Territories.Count}");

        Dictionary<HexCoord, (PlayerId Owner, int Gold)> newCaps = SnapshotCapitals(_state.Territories);
        LogCaptureDiff(actionDesc, oldCaps, newCaps);

        // Neutral-hex captures: a coord that belonged to a
        // None-owned (neutral) territory before the recompute and now has a
        // real owner was just captured from neutral. Logged so manual tests
        // can confirm the neutral-capture path actually executed.
        foreach (Territory prev in previous)
        {
            if (!prev.Owner.IsNone) continue;
            foreach (HexCoord c in prev.Coords)
            {
                PlayerId nowOwner = _state.Grid.Get(c)?.Owner ?? PlayerId.None;
                if (!nowOwner.IsNone)
                {
                    Log.Debug(Log.LogCategory.Capture,
                        $"[capture] neutral hex {c} -> {nowOwner}");
                }
            }
        }

        // A player whose set of capital-bearing territories drops to
        // empty is freshly defeated by this capture. At most one color
        // can transition per capture (a single move/place captures one
        // tile from one color).
        HandleNewlyDefeated(colorsWithCapitalBefore);

        // Instant replay coalesces the structural redraw to once per
        // turn (see InstantReplayTick); skip the per-capture rebuild
        // here so a capture-heavy turn doesn't re-tessellate every
        // border on every beat.
        long tRebuild = Log.Stamp();
        if (!SuppressMapRebuild) _map.RebuildAfterTerritoryChange();
        Log.Since(Log.LogCategory.Capture, "[hitch] RebuildAfterTerritoryChange", tRebuild);

        // A capture can kill the last landed viking — check for the
        // one-shot threat-cleared transition before the win check below.
        MaybeLogVikingThreatCleared();

        // Mid-turn win check: only ends the game if the current player
        // owns every cell. The "opponent reduced to orphan singletons"
        // win path is handled at end-of-turn instead (see
        // EndOfTurnProcessing) — in Rising Tides the shrinking grid
        // excludes submerged tiles, so "every cell" already means "every
        // non-water tile". Viking Raiders gates domination while any
        // threat remains: landed raiders block it structurally (their
        // neutral tiles deny sole ownership), but raiders at sea and
        // unspawned waves need the explicit gate. Undo is cleared so the
        // player can't rewind past the killing blow.
        PlayerId? winner = VikingThreatActive
            ? null
            : WinConditionRules.WinnerByDomination(_state.Grid);
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

        Log.Since(Log.LogCategory.Capture, "[hitch] HandleCapture total", tWhole);
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
    /// For each color in <paramref name="colorsWithCapitalBefore"/> that no
    /// longer has a capital-bearing territory, it was just defeated: play the
    /// defeat cue and, for a non-AI human (and not a non-main recording slot),
    /// raise the defeat overlay. Shared by <see cref="HandleCapture"/> and the
    /// Rising Tides start-of-turn submerge — a sinking shore tile can
    /// drown a player's last capital just like a capture can.
    /// </summary>
    private void HandleNewlyDefeated(HashSet<PlayerId> colorsWithCapitalBefore)
    {
        HashSet<PlayerId> colorsWithCapitalAfter = ColorsWithCapital(_state.Territories);
        foreach (PlayerId c in colorsWithCapitalBefore)
        {
            if (colorsWithCapitalAfter.Contains(c)) continue;
            EmitSound(SoundEffect.PlayerDefeated);
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
