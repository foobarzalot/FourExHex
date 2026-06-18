using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Scene root. Constructs the model (<see cref="GameState"/> +
/// <see cref="SessionState"/>), the two views (<see cref="HexMapView"/>
/// and <see cref="HudView"/>), hands them to a
/// <see cref="GameController"/>, and kicks off the game. Holds no game
/// logic itself — all orchestration lives in the controller.
/// </summary>
public partial class Main : Node2D
{
    private GameController _controller = null!;
    private SaveStore _saveStore = null!;
    private IReadOnlyList<Player> _players = null!;
    private GameState _state = null!;
    private SessionState _session = null!;
    private int _maxTurnNumber;

    /// <summary>
    /// Name of the starting map this game descended from, or null for
    /// procedural (Random Map) games. Carried into every save so a
    /// resumed game can keep showing "Map: foo" in the bottom-left.
    /// </summary>
    private string? _originMapName;
    /// <summary>Campaign level (issue #2) this game plays, or null for
    /// freeform games. Read from GameSettings / the loaded save in _Ready;
    /// drives the win-recording GameEnded hook, the campaign victory
    /// overlay, and the CampaignLevel field of every save written.</summary>
    private int? _campaignLevel;
    private SaveNameModal? _saveModal;
    private SlotPickerDialog? _loadDialog;
    // True between "user picked a slot" and the deferred scene swap —
    // signals OnLoadDialogClosedDuringPause to skip re-showing the
    // pause menu because the whole scene is about to be torn down.
    private bool _loadDialogPicked;

    // Pause coordination. _isPaused tracks our own GetTree().Paused
    // toggle so submenu transitions (save dialog, settings panel) can
    // stay paused across an EscMenu hide-and-reshow without us
    // misinterpreting the close as "user wants to unpause." _escMenu
    // and _settingsPanel are only constructed in non-diagnostic mode —
    // FOUREXHEX_6AI runs headless with no HUD to raise EscRequested.
    private bool _isPaused;
    private EscMenu _escMenu = null!;
    private SettingsPanel _settingsPanel = null!;

    public override void _Ready()
    {
        // Diagnostic launches: setting FOUREXHEX_6AI or FOUREXHEX_6AI_QUICK
        // before starting Godot forces all six slots to Computer, enables
        // verbose AI logging to stdout, runs the game synchronously (no
        // pacing delays), and auto-quits on game over. Intended for
        // Claude to run headless and read the logs. Log.Sink +
        // FOUREXHEX_LOG are wired by the LogBootstrap autoload before any
        // scene loads (see scripts/LogBootstrap.cs), so the sink and
        // category levels are already live here. The diagnostic block
        // below only adds its diagnostic level overrides on top.
        //
        // FOUREXHEX_6AI (full) uses HexMapView's default grid (30×20,
        // 600 tiles) and a 500-turn cap — slow but faithful to a
        // menu-launched game, which is what catches AI economic bugs
        // (see #24 / #22). FOUREXHEX_6AI_QUICK uses an 18×13 grid (234
        // tiles, 2.6× fewer) and a 100-turn cap — a fast smoke test for
        // crash regressions and determinism checks. Full mode wins if
        // both are set.
        //
        // FOUREXHEX_SEED=<int> locks the master seed so two runs of the
        // same mode produce byte-identical output (the determinism check
        // for issue #20). Falls back to the saved-load seed, then
        // GameSettings.MasterSeed, then a fresh Random.Shared.Next().
        bool fullDiagMode = OS.GetEnvironment("FOUREXHEX_6AI").Length > 0;
        bool quickDiagMode = !fullDiagMode
            && OS.GetEnvironment("FOUREXHEX_6AI_QUICK").Length > 0;
        bool diagnosticMode = fullDiagMode || quickDiagMode;
        if (diagnosticMode)
        {
            for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = PlayerKind.Computer;
            }
            // FOUREXHEX_DIFFICULTY="recruit,soldier,captain,commander,...":
            // per-slot difficulty levels (issue #11). Difficulty is an
            // upkeep HANDICAP, so a commander slot pays 1.5× upkeep and
            // should underperform in a headless 6-AI run (and a recruit
            // slot, paying less, should overperform). Comma-separated level
            // names, case-insensitive, up to 6; missing or unknown slots
            // stay Soldier (the default). Must run before Player.BuildRoster
            // (below) reads it.
            string diffSpec = OS.GetEnvironment("FOUREXHEX_DIFFICULTY");
            if (diffSpec.Length > 0)
            {
                string[] parts = diffSpec.Split(',');
                for (int i = 0; i < GameSettings.Difficulties.Length; i++)
                {
                    GameSettings.Difficulties[i] =
                        i < parts.Length
                        && System.Enum.TryParse(parts[i].Trim(), ignoreCase: true,
                            out Difficulty d)
                            ? d
                            : Difficulty.Soldier;
                }
            }
            // Reproduce the verbose AI/turn stdout the old
            // AiLog.Enabled=true produced. Set AFTER Configure so the
            // headless regression harness can't be silenced by a stray
            // FOUREXHEX_LOG=*:Off.
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Debug);
            Log.SetLevel(Log.LogCategory.Turn, Log.LogLevel.Info);
            Log.SetLevel(Log.LogCategory.Capture, Log.LogLevel.Debug);
            GD.Print(quickDiagMode
                ? "=== FOUREXHEX_6AI_QUICK diagnostic mode (smoke test, 18×13, cap=200) ==="
                : "=== FOUREXHEX_6AI diagnostic mode (full, 30×20, cap=500) ===");
        }

        // Consume any pending load request from the menu. Clear it
        // immediately so a subsequent menu→game transition starts
        // a fresh game.
        LoadedSave? pendingLoad = LoadRequest.Pending;
        LoadRequest.Pending = null;

        // --- Model construction ------------------------------------------
        // In normal mode we read grid dimensions off a HexMapView
        // shell (its [Export] Cols/Rows); in diagnostic mode we
        // hardcode the defaults so we can skip constructing the
        // view altogether — real HexMapView / HudView do a lot of
        // layout and rendering work we don't want in a headless
        // tight loop.
        int cols, rows;
        HexMapView? visibleMap = null;
        if (quickDiagMode)
        {
            // Smoke-test grid (18×13, 234 tiles) — small enough that a
            // full 100-turn 6AI game finishes in seconds. Loses #24's
            // AI-economic-bug signal (the doom-spiral bankruptcy
            // pattern needs the full 30×20 to surface), so use full
            // mode for AI-behavior checks, quick mode for crash-only
            // smoke + determinism reruns.
            cols = 18;
            rows = 13;
        }
        else if (fullDiagMode)
        {
            // Match HexMapView's [Export] defaults — diverging here used to
            // hide whole classes of AI economic bugs (see #24): the 6AI
            // harness on 18×13 showed 0 bankruptcies in 10 runs while
            // menu-launched 6AI on the default 30×20 showed dozens. Pull
            // dimensions from a throwaway HexMapView instance so the two
            // launch paths can't drift again.
            visibleMap = new HexMapView();
            cols = visibleMap.Cols;
            rows = visibleMap.Rows;
            visibleMap = null;  // not added to the scene tree; let GC reclaim
        }
        else
        {
            visibleMap = new HexMapView();
            cols = visibleMap.Cols;
            rows = visibleMap.Rows;
        }

        // One seed drives both the initial grid and the GameController's
        // per-turn RNG, so the menu's "Map Seed" field is reproducible
        // end-to-end. Order of precedence:
        //   1. FOUREXHEX_SEED env var (locks the seed for determinism
        //      reruns — e.g. issue #20's "two FOUREXHEX_6AI_QUICK runs
        //      must produce identical output" check).
        //   2. Saved-load (replays the saved game).
        //   3. Menu's selection.
        //   4. Fresh Random for the diagnostic path (FOUREXHEX_6AI never
        //      visits the menu so MasterSeed is null).
        int? envSeed = null;
        string envSeedStr = OS.GetEnvironment("FOUREXHEX_SEED");
        if (envSeedStr.Length > 0 && int.TryParse(envSeedStr, out int parsedSeed))
        {
            envSeed = parsedSeed;
            if (diagnosticMode) GD.Print($"=== FOUREXHEX_SEED={parsedSeed} ===");
        }
        int seed = envSeed
                ?? pendingLoad?.MasterSeed
                ?? GameSettings.MasterSeed
                ?? SeedFormat.NextSeed(System.Random.Shared);

        // A loaded "starting map" is distinguished by TurnNumber == 0 on
        // disk — the editor never advances the turn counter, while in-
        // progress saves are always at turn 1+. We branch on this so the
        // play scene can do the right thing for each case.
        bool isStartingMap = pendingLoad != null && pendingLoad.State.Turns.TurnNumber == 0;

        // Campaign pointer (issue #2). A fresh menu launch arrives with
        // GameSettings.CampaignLevel already set (campaign screen) or
        // cleared (freeform Start Game). Loads override it from the save:
        // a resumed campaign autosave restores its level so the win still
        // counts; a freeform save (null in the file) clears any leftover.
        // Starting-map and diagnostic games are never campaign games.
        if (pendingLoad != null || diagnosticMode)
        {
            GameSettings.CampaignLevel = (isStartingMap || diagnosticMode)
                ? null
                : pendingLoad!.CampaignLevel;
        }
        _campaignLevel = GameSettings.CampaignLevel;
        if (_campaignLevel is int campaignLvl)
        {
            Log.Info(Log.LogCategory.Campaign,
                $"Main: campaign game for level {CampaignProgress.LabelFor(campaignLvl)}" +
                (pendingLoad != null ? " (resumed from save)" : ""));
        }

        if (pendingLoad != null && !isStartingMap)
        {
            // Resume in-progress save: state, players, seed, max-turn cap
            // all come from the save. Skip fresh grid/territory construction.
            _state = pendingLoad.State;
            _players = pendingLoad.Players;
            _maxTurnNumber = pendingLoad.MaxTurnNumber;
            // Carry the origin map name forward so the bottom-left label
            // and future autosaves keep identifying the map of origin.
            _originMapName = pendingLoad.OriginMapName;
        }
        else if (pendingLoad != null && isStartingMap)
        {
            // Starting-map flow: terrain (grid, water, territories,
            // pre-placed trees/towers/capitals) comes from the saved
            // map, but everything player-ish is fresh. Players come
            // from the menu's role config — the saved map intentionally
            // doesn't carry kinds. Turn state starts at turn 1, player 0
            // (red goes first) and the treasury is empty.
            _players = Player.BuildRoster();
            _state = new GameState(
                pendingLoad.State.Grid,
                pendingLoad.State.Territories,
                _players,
                new TurnState(_players),
                new Treasury(),
                pendingLoad.State.WaterCoords);
            _maxTurnNumber = quickDiagMode ? 200
                : fullDiagMode ? 500
                : int.MaxValue;
            _originMapName = pendingLoad.SlotName;
        }
        else
        {
            _players = Player.BuildRoster();
            _state = ProceduralGame.Build(cols, rows, _players, seed,
                new MapGenOptions(IncludeMountains: GameSettings.IncludeMountains));
            _maxTurnNumber = quickDiagMode ? 200
                : fullDiagMode ? 500
                : int.MaxValue;
            _originMapName = null;
        }
        _session = new SessionState();
        // Resumed games carry the per-player highest claim-victory tier
        // already dismissed — persist across save/load so the prompt
        // won't re-fire on a tier the player has already seen.
        if (pendingLoad != null)
        {
            foreach (KeyValuePair<PlayerId, int> kvp in pendingLoad.ClaimVictoryPromptedHighestThreshold)
            {
                _session.ClaimVictoryPromptedHighestThreshold[kvp.Key] = kvp.Value;
            }
        }

        // --- Views --------------------------------------------------------
        IHexMapView map;
        IHudView hud;
        HudView? visibleHud = null;
        if (diagnosticMode)
        {
            map = new HeadlessHexMapView();
            hud = new HeadlessHudView();
        }
        else
        {
            visibleMap!.Init(_state);
            AddChild(visibleMap);
            // HexMapView._Ready owns its initial Position now (clamped
            // pan, supports maps larger than the viewport).

            visibleHud = new HudView();
            // The HUD owns layout policy (orientation + which bars are up) and
            // publishes the map's reserved top/bottom insets; relay them to the
            // map. Subscribe BEFORE AddChild so the HUD's _Ready-time publish is
            // caught. visibleMap is already in the tree (added above), so its
            // SetMapInsets re-centers immediately.
            HexMapView mapForInsets = visibleMap!;
            visibleHud.MapInsetsChanged += (top, bottom) => mapForInsets.SetMapInsets(top, bottom);
            AddChild(visibleHud);

            // Scene-level actions: Play Again reloads the whole
            // scene with the same play config — same player roster,
            // same master seed (so a procedural map regenerates
            // identically), and for starting-map games the same
            // map. MainMenuClicked still fires from the Victory and
            // Defeat overlays' "Main Menu" buttons.
            visibleHud.NewGameClicked += RestartCurrentGame;
            visibleHud.MainMenuClicked += AbandonAndReturnToMenu;

            // Campaign games swap the victory overlay for the campaign
            // variant ("Level XX — won", Next unbeaten / Back to campaign).
            if (_campaignLevel is int hudCampaignLevel)
            {
                visibleHud.SetCampaignLevel(hudCampaignLevel);
                visibleHud.CampaignBackClicked += () =>
                {
                    MainMenuScene.OpenCampaignOnArrival = true;
                    AbandonAndReturnToMenu();
                };
                visibleHud.CampaignNextLevelClicked += LaunchNextUnbeatenCampaignLevel;
            }

            // ESC and the Pause HUD button both raise EscRequested.
            // The pause modal lives at the scene root so its options
            // can reach the controller / scene tree directly, and the
            // settings panel sits alongside it so the pause menu's
            // Settings entry can hand off to a real component.
            _escMenu = new EscMenu();
            AddChild(_escMenu);
            // Escape-key dismissal of the pause menu unpauses; button
            // clicks manage pause state themselves (Resume / Exit
            // unpause explicitly, Save / Settings stay paused while a
            // sub-screen takes over).
            _escMenu.EscapeClosed += ExitPause;

            _settingsPanel = new SettingsPanel();
            AddChild(_settingsPanel);

            visibleHud.EscRequested += EnterPause;

            map = visibleMap;
            hud = visibleHud;
        }

        // --- Controller takes over from here -----------------------------
        // Normal launch: Godot-backed pacer so AI turns visibly play
        // out over time. Diagnostic launch: synchronous pacer so the
        // whole game runs inline and we can read the full log, plus
        // a hard turn cap so stasis runs terminate instead of
        // looping forever.
        // Replay playback reuses the same pacer/runner as live AI, but
        // the user's Replay Speed setting is what should govern its
        // delays — not Ai Speed. A captured local on the controller
        // (set after construction) lets the pacer multiplier and the
        // silent-mode predicate both consult IsReplayMode at call
        // time without a forward-reference cycle.
        GameController? controllerRef = null;
        IAiPacer pacer = diagnosticMode
            ? new SynchronousAiPacer()
            : new GodotAiPacer(
                new SceneTreeTimerFactory(GetTree()),
                () => controllerRef?.IsReplayMode == true
                    ? UserSettings.SpeedMultiplierPercent(UserSettings.ReplaySpeed)
                    : UserSettings.SpeedMultiplierPercent(UserSettings.AiSpeed));
        // If we're resuming an in-progress save that carries a replay,
        // hand it to the controller so recording continues against the
        // same beat log (and BeginReplay can rewind to the original
        // game-start snapshot). Starting maps and fresh games pass null.
        Replay? loadedReplay = (pendingLoad != null && !isStartingMap)
            ? pendingLoad.Replay
            : null;
        _controller = new GameController(
            _state, _session, map, hud,
            seed: seed,
            aiPacer: pacer,
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: _maxTurnNumber,
            loadedReplay: loadedReplay,
            // Selects the chunked InstantAiTick path (via ScheduleAiTurn)
            // for live AI turns when Ai Speed is Instant and we're not
            // replaying.
            aiSilentMode: () => controllerRef?.IsReplayMode != true
                && UserSettings.AiSpeed == PlaybackSpeed.Instant,
            // Consulted once per BeginReplay (the only caller, fired by
            // the post-game Replay button) to pick the chunked
            // fast-forward path instead of the paced step machine.
            replayIsInstantMode: () => UserSettings.ReplaySpeed == PlaybackSpeed.Instant);
        controllerRef = _controller;

        if (!diagnosticMode)
        {
            // Wire the Replay button. Replay availability flips on
            // GameEnded so the button enables only after the game
            // finishes, and only when we have history from game start.
            // BeginReplay re-fires GameEnded at the end of playback,
            // which re-runs this handler harmlessly (button stays on).
            hud.ReplayClicked += () => _controller.BeginReplay();
            _controller.GameEnded += () =>
                hud.SetReplayAvailable(_controller.ReplayDataIsCompleteFromStart);
            // Campaign result (issue #2): record before the controller's
            // trailing RefreshViews paints the overlay, so the campaign
            // victory screen reads updated totals. Subscribed after the
            // replay hook; both run synchronously on GameEnded.
            _controller.GameEnded += OnGameEndedRecordCampaignResult;
        }

        if (diagnosticMode)
        {
            // Defer the quit so Godot finishes setting up the
            // scene tree before we tear it down. Calling Quit()
            // synchronously from inside _Ready() / StartGame()
            // races with scene init and can crash on exit.
            _controller.GameEnded += () => GetTree().CallDeferred("quit");
        }

        // Save/load wiring (skipped in diagnostic mode — no UI to
        // drive saves, and the controller's HumanTurnStarted never
        // fires when all players are AI).
        _saveStore = new SaveStore();
        if (!diagnosticMode)
        {
            BuildSaveDialog();
            BuildLoadDialog();
            _controller.HumanTurnStarted += OnHumanTurnStartedAutosave;
            // Save / Load are invoked from the pause-menu callbacks
            // now — no standalone HUD buttons to wire up.
        }

        // Resume only on actual in-progress loads. Starting maps need a
        // fresh game flow (turn 1, full income/upkeep cycle) on top of
        // the saved terrain.
        if (pendingLoad != null && !isStartingMap)
        {
            _controller.Resume();
        }
        else
        {
            _controller.StartGame();
        }

        // Games descended from a starting map identify by name; procedural
        // games show the seed driving the per-turn RNG.
        string mapLabel = _originMapName != null
            ? $"Map: {_originMapName}"
            : $"Seed: {SeedFormat.ToHex(_controller.MasterSeed)}";
        hud.SetMapLabel(mapLabel);
        Log.Info(Log.LogCategory.Turn,
            $"Main: master seed {SeedFormat.ToHex(_controller.MasterSeed)}");

#if DEBUG
        CheatMenu.Attach(this);
#endif
    }

    /// <summary>
    /// Play Again handler. Reloads the play scene preserving the
    /// just-finished game's master seed (so a procedural map
    /// regenerates identically) and, for starting-map games,
    /// re-populates <see cref="LoadRequest.Pending"/> with a fresh
    /// load of the original map so <c>_Ready</c>'s starting-map
    /// branch fires again instead of falling back to procedural.
    /// </summary>
    private void RestartCurrentGame()
    {
        GameSettings.MasterSeed = _controller.MasterSeed;
        if (_originMapName != null)
        {
            try
            {
                LoadRequest.Pending = _saveStore.LoadStartingMap(_originMapName);
            }
            catch (System.Exception ex)
            {
                // If the map file vanished between play and restart,
                // fall through to procedural with the preserved seed
                // rather than crashing the reload.
                GD.PushWarning($"Could not reload starting map '{_originMapName}': {ex.Message}");
            }
        }
        GetTree().ReloadCurrentScene();
    }

    /// <summary>
    /// GameEnded hook for campaign games (issue #2): if the human won,
    /// flip the level to Won (terminal) and persist. Any other outcome —
    /// AI winner, turn-cap stasis — leaves the mark-at-launch Lost status
    /// standing. No-op for freeform games (_campaignLevel null). BeginReplay
    /// re-fires GameEnded at the end of playback; MarkWon is idempotent so
    /// the re-run is harmless.
    /// </summary>
    private void OnGameEndedRecordCampaignResult()
    {
        if (_campaignLevel is not int level) return;
        Player? winner = _session.Winner.HasValue
            ? _players.FirstOrDefault(p => p.Id == _session.Winner.Value)
            : null;
        if (winner?.Kind == PlayerKind.Human)
        {
            CampaignStore.MarkWon(level);
        }
        else
        {
            Log.Info(Log.LogCategory.Campaign,
                $"Main: campaign level {CampaignProgress.LabelFor(level)} ended " +
                $"without a human win (winner: {winner?.Name ?? "none"}) — stays " +
                $"{CampaignStore.Progress.StatusOf(level)}");
        }
    }

    /// <summary>
    /// "Next unbeaten level" on the campaign victory overlay: launch the
    /// lowest non-won level via the shared campaign launch path. Hidden by
    /// HudView when everything is won, so the null check is defensive.
    /// </summary>
    private void LaunchNextUnbeatenCampaignLevel()
    {
        if (CampaignStore.Progress.NextUp is not int next) return;
        // Same teardown rationale as AbandonAndReturnToMenu: drop any
        // in-flight AI step before swapping scenes.
        _controller?.AbandonGame();
        CampaignStore.PrepareLaunch(next);
        GetTree().ChangeSceneToFile("res://scenes/main.tscn");
    }

    /// <summary>
    /// Drop any pending AI step before tearing down the scene so an in-
    /// flight SceneTreeTimer can't fire StepAiExecute against disposed
    /// Polygon2D nodes after the swap, then return to the main menu.
    /// Invoked by the EscMenu's Exit Game option and by HudView's
    /// MainMenuClicked event (Victory / Defeat overlay buttons).
    /// </summary>
    private void AbandonAndReturnToMenu()
    {
        _controller?.AbandonGame();
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }

    /// <summary>
    /// Autosave handler. Captures the current game state into the
    /// "autosave" slot every time a human turn begins, so closing the
    /// game between turns never loses progress.
    /// </summary>
    private void OnHumanTurnStartedAutosave()
    {
        try
        {
            _saveStore.WriteAutosave(_state, _controller.MasterSeed, _players,
                _maxTurnNumber, _originMapName, _session.ClaimVictoryPromptedHighestThreshold,
                replay: BuildReplaySnapshot(),
                campaignLevel: _campaignLevel);
        }
        catch (System.Exception ex)
        {
            GD.PushError($"Autosave failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Build a <see cref="Replay"/> payload from the controller's
    /// current state for embedding in a save. Returns null if the
    /// controller doesn't yet have a captured initial snapshot
    /// (shouldn't happen post-StartGame, but defensive).
    /// </summary>
    private Replay? BuildReplaySnapshot()
    {
        GameStateSnapshot? init = _controller.InitialReplaySnapshot;
        if (init == null) return null;
        return new Replay(init,
            _controller.InitialReplayTurnNumber,
            _controller.InitialReplayCurrentPlayerIndex,
            _controller.ReplayBeats);
    }

    /// <summary>
    /// Build (once) the styled save modal used by the pause menu's
    /// Save Game action. Reused across every save.
    /// </summary>
    private void BuildSaveDialog()
    {
        _saveModal = new SaveNameModal();
        _saveModal.Confirmed += OnSaveNameConfirmed;
        AddChild(_saveModal);
    }

    private void OpenSaveDialog()
    {
        _saveModal?.Open($"save_t{_state.Turns.TurnNumber}");
    }

    private void OnSaveNameConfirmed(string rawName)
    {
        if (_saveModal == null) return;
        string name = SaveStore.SanitizeSlotName(rawName);
        if (name == SaveStore.AutosaveSlotName)
        {
            // Don't let the user manually overwrite the autosave slot
            // — its purpose is to stay an automated checkpoint.
            _saveModal.ShowError("'autosave' is reserved. Please pick a different name.");
            return;
        }
        try
        {
            _saveStore.WriteSlot(name, _state, _controller.MasterSeed, _players,
                _maxTurnNumber, _originMapName, _session.ClaimVictoryPromptedHighestThreshold,
                replay: BuildReplaySnapshot(),
                campaignLevel: _campaignLevel);
        }
        catch (System.Exception ex)
        {
            _saveModal.ShowError($"Could not save: {ex.Message}");
            return;
        }
        _saveModal.Close();
    }

    // --- Pause coordinator -----------------------------------------------
    //
    // EnterPause is the single entry point for Pause: both the HUD's
    // Pause button and Escape (when no buy/move action is pending)
    // route here via visibleHud.EscRequested. GetTree().Paused = true
    // halts SceneTreeTimer-based AI pacing — the EscMenu and
    // SettingsPanel both set ProcessMode.WhenPaused so they stay
    // interactive while the rest of the tree is frozen.
    //
    // Save Game and Settings act as sub-screens of pause: they close
    // the EscMenu (which calls Hide() before invoking the option
    // callback) but leave _isPaused true, then re-show the pause menu
    // when the sub-screen closes. Resume and Exit Game explicitly call
    // ExitPause so the tree unpauses cleanly. The EscMenu.EscapeClosed
    // hook covers the "user pressed Escape on the pause menu" path.

    private void EnterPause()
    {
        if (_isPaused) return;
        _isPaused = true;
        GetTree().Paused = true;
        ShowPauseMenu();
    }

    private void ExitPause()
    {
        if (!_isPaused) return;
        _isPaused = false;
        GetTree().Paused = false;
    }

    private void ShowPauseMenu()
    {
        _escMenu.Show("Paused", new[]
        {
            new EscMenu.Option("Resume", ExitPause),
            new EscMenu.Option("Save Game", OpenSaveDialogFromPause),
            new EscMenu.Option("Load Game", OpenLoadDialogFromPause),
            new EscMenu.Option("Settings", OpenSettingsFromPause),
            new EscMenu.Option("Exit Game", ExitGameFromPause),
        });
    }

    private void OpenSaveDialogFromPause()
    {
        if (_saveModal == null)
        {
            // No modal available (shouldn't happen post-BuildSaveDialog).
            // Re-show the pause menu so the user isn't stranded with no UI.
            ShowPauseMenu();
            return;
        }
        // Closing the modal (cancel, or a Close() after a successful save)
        // brings the user back to the pause menu. The actual save runs via
        // the Confirmed subscription wired in BuildSaveDialog; a failed save
        // keeps the modal open (no Closed) so the user can fix the name.
        _saveModal.Closed += OnSaveDialogClosedDuringPause;
        OpenSaveDialog();
    }

    private void OnSaveDialogClosedDuringPause()
    {
        if (_saveModal != null)
        {
            _saveModal.Closed -= OnSaveDialogClosedDuringPause;
        }
        ShowPauseMenu();
    }

    private void OpenLoadDialogFromPause()
    {
        if (_loadDialog == null)
        {
            ShowPauseMenu();
            return;
        }
        _loadDialogPicked = false;
        // VisibilityChanged fires both on show and on hide. The
        // handler filters to the hide transition, then re-shows the
        // pause menu unless a slot was picked (in which case the
        // scene swap takes over).
        _loadDialog.VisibilityChanged += OnLoadDialogClosedDuringPause;
        _loadDialog.ShowSlots(
            _saveStore.ListSlots(),
            "No save files found.",
            info => info.IsAutosave
                ? $"[Autosave] turn {info.TurnNumber} — {SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)}"
                : $"{info.SlotName} — turn {info.TurnNumber} — {SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)}",
            OnLoadSlotPressedFromPause,
            thumbnailStore: _saveStore);
    }

    private void OnLoadDialogClosedDuringPause()
    {
        if (_loadDialog == null) return;
        // VisibilityChanged fires on both show and hide; only react to
        // the close transition.
        if (_loadDialog.Visible) return;
        _loadDialog.VisibilityChanged -= OnLoadDialogClosedDuringPause;
        if (_loadDialogPicked) return;
        ShowPauseMenu();
    }

    private void OnLoadSlotPressedFromPause(string slotName)
    {
        try
        {
            LoadedSave loaded = _saveStore.LoadSlot(slotName);
            LoadRequest.Pending = loaded;
            // Mirror the saved roster into GameSettings so a subsequent
            // Play Again preserves kinds — same pattern the main menu's
            // load uses.
            for (int i = 0; i < loaded.Players.Count && i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = loaded.Players[i].Kind;
                GameSettings.Difficulties[i] = loaded.Players[i].Difficulty;
            }
            _loadDialogPicked = true;
            // Drop any in-flight AI step so a stale SceneTreeTimer can't
            // fire against disposed nodes after the scene swap (same
            // rationale as AbandonAndReturnToMenu), then unpause before
            // the swap since GetTree().Paused persists across scenes.
            _controller?.AbandonGame();
            ExitPause();
            GetTree().ChangeSceneToFile("res://scenes/main.tscn");
        }
        catch (System.Exception ex)
        {
            _loadDialog?.ShowError($"Could not load '{slotName}': {ex.Message}");
            // Picker stays open; user can retry or close to return to
            // the pause menu via OnLoadDialogClosedDuringPause.
        }
    }

    private void BuildLoadDialog()
    {
        _loadDialog = new SlotPickerDialog("Load Game", "Load failed");
        _loadDialog.Attach(this);
    }

    private void OpenSettingsFromPause()
    {
        _settingsPanel.Closed += OnSettingsClosedDuringPause;
        _settingsPanel.Open();
    }

    private void OnSettingsClosedDuringPause()
    {
        _settingsPanel.Closed -= OnSettingsClosedDuringPause;
        ShowPauseMenu();
    }

    private void ExitGameFromPause()
    {
        // Unpause before the scene swap — GetTree().Paused is
        // process-wide, so leaving it true would freeze the next scene.
        ExitPause();
        AbandonAndReturnToMenu();
    }
}
