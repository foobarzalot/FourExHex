using System;
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
    /// <summary>Campaign level this game plays, or null for
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
    // _helpPauseActive is the Help family's independent hold on the
    // pause (menu / tour / Instructions freeze the game like the pause
    // menu, minus its UI); the tree is paused while EITHER flag is set.
    private bool _isPaused;
    private bool _helpPauseActive;
    private EscMenu _escMenu = null!;
    private SettingsPanel _settingsPanel = null!;

    public override void _Ready()
    {
        // Diagnostic launches (headless regression harness): FOUREXHEX_6AI
        // (full, 30×20, cap 500) or FOUREXHEX_6AI_QUICK (smoke, 18×13, cap
        // 100; full wins if both are set) force all six slots to Computer,
        // enable verbose AI logging, run synchronously, and auto-quit on
        // game over. FOUREXHEX_SEED=<int> locks the master seed for
        // byte-identical determinism reruns. Log.Sink + FOUREXHEX_LOG are
        // wired by the LogBootstrap autoload; the block below adds the
        // diagnostic level overrides on top. See CLAUDE.md for the full list.
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
            // per-slot difficulty levels. Difficulty is a purchase-cost
            // HANDICAP, so a commander slot pays 1.5× for units/towers and
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
            // Force verbose AI/turn stdout. Set AFTER Configure so the
            // headless regression harness can't be silenced by a stray
            // FOUREXHEX_LOG=*:Off.
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Debug);
            Log.SetLevel(Log.LogCategory.Turn, Log.LogLevel.Info);
            Log.SetLevel(Log.LogCategory.Capture, Log.LogLevel.Debug);
            Log.SetLevel(Log.LogCategory.Tree, Log.LogLevel.Debug);
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
            // full 100-turn 6AI game finishes in seconds. Lacks the
            // AI-economic-bug signal (the doom-spiral bankruptcy
            // pattern needs the full 30×20 to surface), so use full
            // mode for AI-behavior checks, quick mode for crash-only
            // smoke + determinism reruns.
            cols = 18;
            rows = 13;
        }
        else if (fullDiagMode)
        {
            // Match HexMapView's [Export] defaults so the two launch paths
            // can't drift. Pull dimensions from a throwaway HexMapView
            // instance.
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
        //   1. FOUREXHEX_SEED env var (locks the seed for the determinism
        //      rerun check).
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

        // Diagnostic terrain overrides (mirror FOUREXHEX_SEED): lock map-gen
        // densities for headless verification runs — e.g. exercising the
        // gold/mountain AI scoring under FOUREXHEX_6AI without editing
        // code or visiting the menu. Each, when set to a valid non-negative
        // int, wins over the menu/default GameSettings value; absent vars
        // (empty string → TryParse false) are no-ops. The effective densities
        // are reported by the Log.Info(MapGen) line below. Only the freeform/
        // diagnostic path reads these GameSettings (campaign levels derive
        // their own densities), which is exactly the 6AI path.
        if (int.TryParse(OS.GetEnvironment("FOUREXHEX_TREE_DENSITY"), out int envTree) && envTree >= 0)
            GameSettings.TreeDensity = envTree;
        if (int.TryParse(OS.GetEnvironment("FOUREXHEX_MTN_DENSITY"), out int envMtn) && envMtn >= 0)
            GameSettings.MountainDensity = envMtn;
        if (int.TryParse(OS.GetEnvironment("FOUREXHEX_GOLD_DENSITY"), out int envGold) && envGold >= 0)
            GameSettings.GoldDensity = envGold;
        if (int.TryParse(OS.GetEnvironment("FOUREXHEX_CLUMP_FACTOR"), out int envClump) && envClump >= 0)
            GameSettings.ClumpingFactor = envClump;
        // Diagnostic game-mode override: FOUREXHEX_MODE=RisingTides
        // launches the headless 6AI run in Rising Tides so the flood / last-
        // player-standing flow can be regression-tested. Absent/unknown → no-op.
        if (System.Enum.TryParse(
                OS.GetEnvironment("FOUREXHEX_MODE"), ignoreCase: true, out GameMode envMode))
            GameSettings.Mode = envMode;

        int seed = envSeed
                ?? pendingLoad?.MasterSeed
                ?? GameSettings.MasterSeed
                ?? SeedFormat.NextSeed(System.Random.Shared);

        // A loaded "starting map" is distinguished by TurnNumber == 0 on
        // disk — the editor never advances the turn counter, while in-
        // progress saves are always at turn 1+. We branch on this so the
        // play scene can do the right thing for each case.
        bool isStartingMap = pendingLoad != null && pendingLoad.State.Turns.TurnNumber == 0;

        // Campaign pointer. A fresh menu launch arrives with
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
            // map. A map with baked kinds (kinds + difficulty, None excluded
            // on load) plays that roster; a map without baked kinds falls
            // back to the default 6-player roster (Red human, rest Computer,
            // all Soldier). Turn state starts at turn 1, player 0 (red first)
            // and the treasury is empty.
            _players = pendingLoad.MapHasBakedKinds
                ? pendingLoad.Players
                : LegacyDefaultRoster();
            _state = new GameState(
                pendingLoad.State.Grid,
                pendingLoad.State.Territories,
                _players,
                new TurnState(_players),
                new Treasury(),
                pendingLoad.State.WaterCoords,
                // Carry the authored game mode so a Rising Tides starting map
                // plays as Rising Tides, not Freeform.
                pendingLoad.State.Mode,
                // A starting map launches a fresh game, so it gets the
                // randomized capital/tide selection and the origin-capital
                // merge rule like any new game. The map's pre-placed capitals
                // are unchanged; only mid-game captures and tide tie-breaks
                // pick up the new-era behavior from here.
                useRandomizedSelection: true,
                useOriginMergeCapital: true);
            Log.Info(Log.LogCategory.Tide,
                $"Main: starting map \"{pendingLoad.SlotName}\" mode={pendingLoad.State.Mode}");
            _maxTurnNumber = quickDiagMode ? 200
                : fullDiagMode ? 500
                : int.MaxValue;
            _originMapName = pendingLoad.SlotName;
        }
        else
        {
            // A campaign game's roster comes from the level (human at the
            // level's slot, rest Computer), never the freeform PlayerKinds —
            // so playing a campaign level can't change the New Game default.
            // Freeform games read the menu's selection.
            _players = _campaignLevel is int campLevel
                ? Player.BuildCampaignRoster(campLevel)
                : Player.BuildRoster();
            // Campaign levels derive their terrain densities from the level number
            // (fixed + reproducible, independent of the freeform New Game
            // steppers); freeform games use the player's chosen densities.
            MapGenOptions mapGenOptions = _campaignLevel is int featureLvl
                ? CampaignProgress.MapGenOptionsForLevel(featureLvl)
                : new MapGenOptions(
                    TreeDensity: GameSettings.TreeDensity,
                    MountainDensity: GameSettings.MountainDensity,
                    GoldDensity: GameSettings.GoldDensity,
                    ClumpingFactor: GameSettings.ClumpingFactor);
            Log.Info(Log.LogCategory.MapGen,
                $"Main: map-gen densities trees={mapGenOptions.TreeDensity} " +
                $"mtn={mapGenOptions.MountainDensity} gold={mapGenOptions.GoldDensity} " +
                $"clump={mapGenOptions.ClumpingFactor} " +
                $"(campaign={_campaignLevel?.ToString() ?? "no"})");
            // A campaign level derives its mode from the level (Rising Tides is a
            // rare Soldier+ complication); freeform/diagnostic games
            // honor the menu's (or FOUREXHEX_MODE's) selection.
            GameMode mode = _campaignLevel is int campaignModeLevel
                ? CampaignProgress.ModeForLevel(campaignModeLevel)
                : GameSettings.Mode;
            Log.Info(Log.LogCategory.Campaign,
                $"Main: game mode = {mode} (campaign={_campaignLevel?.ToString() ?? "no"})");
            _state = ProceduralGame.Build(cols, rows, _players, seed, mapGenOptions, mode);
            _maxTurnNumber = quickDiagMode ? 200
                : fullDiagMode ? 500
                : int.MaxValue;
            _originMapName = null;
        }
        // Roster summary: which colors are active and which slots
        // were dropped as None. Active count drives turn order / capitals / win.
        Log.Info(Log.LogCategory.MapGen,
            $"Main: roster {_players.Count} player(s) ["
            + string.Join(",", _players.Select(p => GameSettings.PlayerConfig[p.Id.Index].Name))
            + "]");
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

            // The Help family (menu / tour / Instructions, behind the "?"
            // button) freezes the game like the pause menu while it's up.
            visibleHud.HelpSessionChanged += OnHelpSessionChanged;

            // Choosing the guided UI tour from the Help menu auto-selects a
            // territory (if none is) so the profit/loss readout renders; the
            // tour overlay itself lives view-side in HudView/HudTour.
            visibleHud.TourStartRequested += () => _controller.EnsureTerritorySelectedForTour();

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
                    : controllerRef?.IsAutomating == true
                        // Automate has its own speed setting, independent of
                        // AI turn speed. Instant never reaches this
                        // multiplier — the loop routes it to the unscaled
                        // chunked InstantAutomateTick (automateIsInstantMode).
                        ? UserSettings.SpeedMultiplierPercent(UserSettings.AutomateSpeed)
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
            replayIsInstantMode: () => UserSettings.ReplaySpeed == PlaybackSpeed.Instant,
            // Re-read at every between-moves automate dispatch: Instant
            // selects the silent chunked InstantAutomateTick track, so a
            // mid-run Settings change switches tracks at the next beat.
            automateIsInstantMode: () => UserSettings.AutomateSpeed == PlaybackSpeed.Instant);
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
            // Campaign result: record before the controller's
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
        void BeginPlay()
        {
            if (pendingLoad != null && !isStartingMap)
            {
                _controller.Resume();
            }
            else
            {
                _controller.StartGame();
            }
        }

        // One-time first-encounter intros: the first time the player starts a
        // game featuring something the board doesn't self-explain, show a short
        // tappable overlay over the just-loaded board and defer the first turn
        // until it's dismissed, so nothing advances while they read.
        //   * Game modes (#96) — Rising Tides / Fog Of War change the rules.
        //   * Terrain (#53) — the first map containing gold / a mountain teaches
        //     it and eases the camera to a representative tile to draw the eye.
        // All of these funnel through this single Main._Ready seam so one code
        // flow covers every launch path (campaign, custom, next-unbeaten,
        // starting map, resume). When several apply they chain in order: mode →
        // gold → mountain. Skipped in diagnostic mode (headless, no tap input).
        GameMode resolvedMode = _state.Mode;
        var intros = new List<(string text, HexCoord? focus, string label)>();
        if (!diagnosticMode)
        {
            if (GameModeIntro.ShouldShow(resolvedMode))
            {
                UserSettings.MarkModeIntroSeen(resolvedMode);
                intros.Add((GameModeIntro.TextFor(resolvedMode)!, null,
                    $"{resolvedMode} mode"));
            }
            foreach (TerrainFeature feature in
                new[] { TerrainFeature.Gold, TerrainFeature.Mountain })
            {
                if (TerrainIntro.ShouldShow(
                    feature, MapFeatures.Contains(_state.Grid, feature)))
                {
                    UserSettings.MarkTerrainIntroSeen(feature);
                    intros.Add((TerrainIntro.TextFor(feature)!,
                        MapFeatures.FirstTile(_state.Grid, feature),
                        $"{feature} terrain"));
                }
            }
        }

        if (intros.Count == 0)
        {
            if (!diagnosticMode)
            {
                Log.Debug(Log.LogCategory.Campaign,
                    $"Main: no first-encounter intros (mode={resolvedMode}, " +
                    $"gold={MapFeatures.Contains(_state.Grid, TerrainFeature.Gold)}, " +
                    $"mtn={MapFeatures.Contains(_state.Grid, TerrainFeature.Mountain)})");
            }
            BeginPlay();
        }
        else
        {
            // Drive the queue one step at a time: paint the board once (so Fog
            // Of War applies its cover and the board shows its real start state
            // underneath), pan to the focus tile if any, show the overlay, and
            // advance on tap. BeginPlay stays deferred until the queue drains.
            void ShowIntro(int i)
            {
                (string text, HexCoord? focus, string label) = intros[i];
                Log.Info(Log.LogCategory.Campaign,
                    $"Main: showing {label} intro ({i + 1}/{intros.Count}, focus={focus})");
                _controller.RefreshViewsForTutorial();
                if (focus is HexCoord c) map.CenterOnCoord(c);
                // Pulse the taught tile to draw the eye (no-op for the mode
                // intro, which has no focus tile).
                map.ShowTerrainFocusPulse(focus);
                hud.ShowTappableTutorialMessage(text);
                Action onTap = null!;
                onTap = () =>
                {
                    hud.TutorialMessageTapped -= onTap;
                    hud.HideTutorialMessage();
                    map.ShowTerrainFocusPulse(null);
                    if (i + 1 < intros.Count)
                    {
                        Log.Info(Log.LogCategory.Campaign,
                            $"Main: {label} intro dismissed → next intro");
                        ShowIntro(i + 1);
                    }
                    else
                    {
                        Log.Info(Log.LogCategory.Campaign,
                            $"Main: {label} intro dismissed → starting play");
                        BeginPlay();
                    }
                };
                hud.TutorialMessageTapped += onTap;
            }
            ShowIntro(0);
        }

        // Games descended from a starting map identify by name; procedural
        // games show the seed driving the per-turn RNG.
        string mapLabel = _originMapName != null
            ? Strings.Get(StringKeys.MainMapLabel, ("name", _originMapName))
            : Strings.Get(StringKeys.MainSeedLabel,
                ("seed", SeedFormat.ToHex(_controller.MasterSeed)));
        hud.SetMapLabel(mapLabel);
        Log.Info(Log.LogCategory.Turn,
            $"Main: master seed {SeedFormat.ToHex(_controller.MasterSeed)}");

#if DEBUG
        CheatMenu.Attach(this);
#endif
    }

    /// <summary>Default roster for a starting map that baked no kinds: Red
    /// human, the rest Computer, all Soldier. Painted colors that own no tiles
    /// simply start eliminated.</summary>
    private static List<Player> LegacyDefaultRoster()
    {
        var players = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            players.Add(new Player(
                GameSettings.PlayerConfig[i].Name,
                PlayerId.FromIndex(i),
                i == 0 ? PlayerKind.Human : PlayerKind.Computer,
                Difficulty.Soldier));
        }
        return players;
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
    /// GameEnded hook for campaign games: if the human won,
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
            _saveModal.ShowError(Strings.Get(StringKeys.SaveReservedName));
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
            _saveModal.ShowError(Strings.Get(StringKeys.SaveCouldNotSave,
                ("error", ex.Message)));
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
        ApplyPauseState();
        ShowPauseMenu();
    }

    private void ExitPause()
    {
        if (!_isPaused) return;
        _isPaused = false;
        ApplyPauseState();
    }

    // The Help family (menu / guided tour / Instructions) holds the same
    // freeze as the pause menu while it's up — opponent turns, replay,
    // and automation stop while the player reads. Raised by HudView on
    // session start/end transitions.
    private void OnHelpSessionChanged(bool active)
    {
        _helpPauseActive = active;
        ApplyPauseState();
    }

    // Single writer for GetTree().Paused: paused while either the pause
    // menu or a help session holds it, so the two modal families can't
    // clobber each other's freeze (they're mutually exclusive in the UI,
    // but the pause state must not depend on that).
    private void ApplyPauseState()
    {
        GetTree().Paused = _isPaused || _helpPauseActive;
    }

    private void ShowPauseMenu()
    {
        _escMenu.Show(Strings.Get(StringKeys.PauseTitle), new[]
        {
            new EscMenu.Option(Strings.Get(StringKeys.MenuResume), ExitPause),
            new EscMenu.Option(Strings.Get(StringKeys.SaveTitleGame), OpenSaveDialogFromPause),
            new EscMenu.Option(Strings.Get(StringKeys.MenuLoadGame), OpenLoadDialogFromPause),
            new EscMenu.Option(Strings.Get(StringKeys.MenuSettings), OpenSettingsFromPause),
            new EscMenu.Option(Strings.Get(StringKeys.PauseExitGame), ExitGameFromPause),
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
            Strings.Get(StringKeys.MenuNoSavesFound),
            info => info.IsAutosave
                ? Strings.Get(StringKeys.SaveAutosaveRow,
                    ("turn", info.TurnNumber.ToString()),
                    ("time", SlotPickerDialog.FormatTimestamp(info.SavedAtUnix)))
                : Strings.Get(StringKeys.SaveSlotRow,
                    ("name", info.SlotName),
                    ("turn", info.TurnNumber.ToString()),
                    ("time", SlotPickerDialog.FormatTimestamp(info.SavedAtUnix))),
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
            GameSettings.AdoptRosterFrom(loaded);
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
            _loadDialog?.ShowError(Strings.Get(StringKeys.MenuCouldNotLoad,
                ("name", slotName), ("error", ex.Message)));
            // Picker stays open; user can retry or close to return to
            // the pause menu via OnLoadDialogClosedDuringPause.
        }
    }

    private void BuildLoadDialog()
    {
        _loadDialog = new SlotPickerDialog(
            Strings.Get(StringKeys.MenuLoadGame), Strings.Get(StringKeys.MenuLoadFailed));
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
