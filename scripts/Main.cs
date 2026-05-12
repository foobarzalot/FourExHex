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
            _players = BuildPlayers();
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
            _players = BuildPlayers();
            var turnState = new TurnState(_players);
            var treasury = new Treasury();
            MapGenResult mapGen = MapGenerator.BuildInitialGrid(cols, rows, _players, seed);
            HexGrid grid = mapGen.Grid;
            IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(grid);
            IReadOnlyList<Territory> territories = CapitalReconciler.Reconcile(
                raw, new List<Territory>(), grid);
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
            // map. Main Menu swaps back to the menu scene so the
            // player can reassign roles.
            visibleHud.NewGameClicked += RestartCurrentGame;
            visibleHud.MainMenuClicked += () =>
            {
                // Drop any pending AI step before tearing down the scene
                // so an in-flight SceneTreeTimer can't fire StepAiExecute
                // against disposed Polygon2D nodes after the swap.
                _controller?.AbandonGame();
                GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
            };

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
            _controller.HumanTurnStarted += OnHumanTurnStartedAutosave;
            if (visibleHud != null)
            {
                visibleHud.SaveGameClicked += OpenSaveDialog;
            }
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

    private static List<Player> BuildPlayers()
    {
        var players = new List<Player>();
        // Player roles come from GameSettings, which the main menu
        // writes before switching to this scene. StartGame
        // auto-drives any AI players at the front of the turn order
        // so human input is only needed on a human's turn.
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            AiKind kind = i < GameSettings.PlayerKinds.Length
                ? GameSettings.PlayerKinds[i]
                : AiKind.Heuristic;
            players.Add(new Player(name, new Color(hex), kind));
        }
        return players;
    }
}
