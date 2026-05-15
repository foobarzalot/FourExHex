using System.Collections.Generic;
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
    private AcceptDialog? _saveDialog;
    private LineEdit? _saveDialogLineEdit;
    private AcceptDialog? _saveErrorDialog;
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
        // Diagnostic launch: setting the FOUREXHEX_6AI environment
        // variable before starting Godot forces all six slots to
        // Heuristic AI, enables verbose AI logging to stdout, runs the
        // game synchronously (no pacing delays), caps turns at 500
        // so stasis runs terminate, and auto-quits on game over.
        // Intended for Claude to run headless and read the logs.
        bool diagnosticMode = OS.GetEnvironment("FOUREXHEX_6AI").Length > 0;
        if (diagnosticMode)
        {
            for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = AiKind.Heuristic;
            }
            AiLog.Enabled = true;
            GD.Print("=== FOUREXHEX_6AI diagnostic mode ===");
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
        if (diagnosticMode)
        {
            cols = 18;
            rows = 13;
        }
        else
        {
            visibleMap = new HexMapView();
            cols = visibleMap.Cols;
            rows = visibleMap.Rows;
        }

        // One seed drives both the initial grid and the GameController's
        // per-turn RNG, so the menu's "Map Seed" field is reproducible
        // end-to-end. Load wins (replays the saved game), then the menu's
        // selection, and finally a fresh random seed for the diagnostic
        // path (FOUREXHEX_6AI never visits the menu so MasterSeed is null).
        int seed = pendingLoad?.MasterSeed
                ?? GameSettings.MasterSeed
                ?? System.Random.Shared.Next();

        // A loaded "starting map" is distinguished by TurnNumber == 0 on
        // disk — the editor never advances the turn counter, while in-
        // progress saves are always at turn 1+. We branch on this so the
        // play scene can do the right thing for each case.
        bool isStartingMap = pendingLoad != null && pendingLoad.State.Turns.TurnNumber == 0;

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
            _maxTurnNumber = diagnosticMode ? 500 : int.MaxValue;
            _originMapName = pendingLoad.SlotName;
        }
        else
        {
            _players = Player.BuildRoster();
            var turnState = new TurnState(_players);
            var treasury = new Treasury();
            MapGenResult mapGen = MapGenerator.BuildInitialGrid(cols, rows, _players, seed);
            HexGrid grid = mapGen.Grid;
            IReadOnlyList<Territory> territories = TerritoryFinder.Recompute(
                grid, new List<Territory>());
            _state = new GameState(grid, territories, _players, turnState, treasury, mapGen.WaterCoords);
            _maxTurnNumber = diagnosticMode ? 500 : int.MaxValue;
            _originMapName = null;
        }
        _session = new SessionState();
        // Resumed games carry the per-color highest claim-victory tier
        // already dismissed — persist across save/load so the prompt
        // won't re-fire on a tier the player has already seen.
        if (pendingLoad != null)
        {
            foreach (KeyValuePair<Color, int> kvp in pendingLoad.ClaimVictoryPromptedHighestThreshold)
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
            map = new HeadlessHexMapView(_state);
            hud = new HeadlessHudView();
        }
        else
        {
            visibleMap!.Init(_state);
            AddChild(visibleMap);
            // HexMapView._Ready owns its initial Position now (clamped
            // pan, supports maps larger than the viewport).

            visibleHud = new HudView();
            AddChild(visibleHud);

            // Scene-level actions: Play Again reloads the whole
            // scene with the same play config — same player roster,
            // same master seed (so a procedural map regenerates
            // identically), and for starting-map games the same
            // map. MainMenuClicked still fires from the Victory and
            // Defeat overlays' "Main Menu" buttons.
            visibleHud.NewGameClicked += RestartCurrentGame;
            visibleHud.MainMenuClicked += AbandonAndReturnToMenu;

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
        IAiPacer pacer = diagnosticMode
            ? new SynchronousAiPacer()
            : new GodotAiPacer(new SceneTreeTimerFactory(GetTree()));
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
            loadedReplay: loadedReplay);

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
            : $"Seed: {_controller.MasterSeed}";
        hud.SetMapLabel(mapLabel);
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
                replay: BuildReplaySnapshot());
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
    /// Build (once) the AcceptDialog used by the HUD's Save button.
    /// Reused across every save action.
    /// </summary>
    private void BuildSaveDialog()
    {
        _saveDialog = new AcceptDialog
        {
            Title = "Save Game",
            OkButtonText = "Save",
            // Allow the user to dismiss with the OS close button.
            Exclusive = false,
            // Always — Save Game is reached from the pause menu where
            // GetTree().Paused is true, so the default Inherit would
            // freeze the dialog. Always works in both states.
            ProcessMode = ProcessModeEnum.Always,
        };

        // AcceptDialog's built-in DialogText label and any added child
        // nodes share the same content area; setting both causes them
        // to overlap. Build a VBox with our own label + LineEdit
        // instead, leaving DialogText empty.
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(280, 0) };
        content.AddThemeConstantOverride("separation", 8);
        var label = new Label { Text = "Slot name:" };
        content.AddChild(label);
        _saveDialogLineEdit = new LineEdit
        {
            Text = "save",
            CustomMinimumSize = new Vector2(260, 30),
        };
        content.AddChild(_saveDialogLineEdit);
        _saveDialog.AddChild(content);
        _saveDialog.RegisterTextEnter(_saveDialogLineEdit);
        _saveDialog.Confirmed += OnSaveDialogConfirmed;

        AddChild(_saveDialog);

        _saveErrorDialog = new AcceptDialog
        {
            Title = "Save failed",
            OkButtonText = "OK",
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(_saveErrorDialog);
    }

    private void OpenSaveDialog()
    {
        if (_saveDialog == null || _saveDialogLineEdit == null) return;
        _saveDialogLineEdit.Text = $"save_t{_state.Turns.TurnNumber}";
        _saveDialog.PopupCentered();
        _saveDialogLineEdit.GrabFocus();
        _saveDialogLineEdit.SelectAll();
    }

    private void OnSaveDialogConfirmed()
    {
        if (_saveDialogLineEdit == null) return;
        string name = SaveStore.SanitizeSlotName(_saveDialogLineEdit.Text);
        if (name == SaveStore.AutosaveSlotName)
        {
            // Don't let the user manually overwrite the autosave slot
            // — its purpose is to stay an automated checkpoint.
            ShowSaveError("'autosave' is reserved. Please pick a different name.");
            return;
        }
        try
        {
            _saveStore.WriteSlot(name, _state, _controller.MasterSeed, _players,
                _maxTurnNumber, _originMapName, _session.ClaimVictoryPromptedHighestThreshold,
                replay: BuildReplaySnapshot());
        }
        catch (System.Exception ex)
        {
            ShowSaveError($"Could not save: {ex.Message}");
        }
    }

    private void ShowSaveError(string message)
    {
        if (_saveErrorDialog == null)
        {
            GD.PushError(message);
            return;
        }
        _saveErrorDialog.DialogText = message;
        _saveErrorDialog.PopupCentered();
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
        if (_saveDialog == null)
        {
            // No dialog available (shouldn't happen post-BuildSaveDialog).
            // Re-show the pause menu so the user isn't stranded with no UI.
            ShowPauseMenu();
            return;
        }
        // Both Confirmed and Canceled bring the user back to the pause
        // menu. Existing OnSaveDialogConfirmed runs the actual save via
        // its own (already-wired) Confirmed subscription from
        // BuildSaveDialog — we just chain a return-to-pause hop.
        _saveDialog.Confirmed += OnSaveDialogClosedDuringPause;
        _saveDialog.Canceled += OnSaveDialogClosedDuringPause;
        OpenSaveDialog();
    }

    private void OnSaveDialogClosedDuringPause()
    {
        if (_saveDialog != null)
        {
            _saveDialog.Confirmed -= OnSaveDialogClosedDuringPause;
            _saveDialog.Canceled -= OnSaveDialogClosedDuringPause;
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
            OnLoadSlotPressedFromPause);
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
