using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Preview-mode chrome. The dev plays as player 0 (Red); a
/// <see cref="ReplayDrivenAi"/> chooser plays the other five players'
/// recorded moves through the standard AI step machine. A
/// <see cref="TutorialPreview"/> validates every player-0 input
/// against the next expected scripted beat — mismatches surface a
/// tutorial-message toast and the action is aborted.
///
/// <para>
/// On <see cref="Start"/>: applies the tutorial's <c>InitialSnapshot</c>
/// to the panel's grid so playback begins from the recorded start
/// state; builds a real <see cref="HudView"/> + <see cref="GameController"/>
/// with the validator hook wired in; forces drag-mode to Pan; calls
/// <c>StartGame</c>. The controller's <c>_previewMode</c> flag
/// suppresses every <c>RecordBeat</c> call so the script isn't
/// polluted by the dev's playthrough.
/// </para>
/// </summary>
public sealed partial class PreviewPane : Control
{
    /// <summary>
    /// Forwarded from the inner HudView. Fires whenever the player asks
    /// for the pause modal (ESC with no pending action, or the End Game
    /// button). The scene root subscribes and shows its EscMenu.
    /// </summary>
    public event Action? EscRequested;

    private MapEditorPanel _panel = null!;
    private HudView? _hud;
    private TutorialPreview? _preview;
    private ReplayDrivenAi? _replayAi;
    private GameController? _controller;
    private GameState? _previewState;
    private TutorialPreviewCues? _cues;
    private TutorialNarrationDriver? _narration;
    private HexDragMode _savedDragMode;
    private bool _running;
    // The tutorial currently being previewed, kept so the victory
    // overlay's "Play Again" can restart this same tutorial.
    private Tutorial? _tutorial;

    public override void _Ready()
    {
        // Full-rect against the viewport (parent is a Node2D, so anchors
        // resolve to the viewport). The preset sets anchors AND offsets,
        // so the pane fills and auto-resizes with the viewport — no
        // explicit Size assignment, which is what triggered the
        // "non-equal opposite anchors" warning on _Ready.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetPanel(MapEditorPanel panel)
    {
        _panel = panel;
    }

    /// <summary>
    /// Enter Preview mode with <paramref name="tutorial"/>. Idempotent
    /// — a second Start without an intervening Pause tears down the
    /// prior session first.
    /// </summary>
    public void Start(Tutorial tutorial)
    {
        Log.Debug(Log.LogCategory.Tutorial, $"[PreviewPane] Start: beats={tutorial.Replay.Beats.Count}, initialTurn={tutorial.Replay.InitialTurnNumber}, initialPlayer={tutorial.Replay.InitialCurrentPlayerIndex}");
        if (_running) Pause();
        _tutorial = tutorial;

        // Player roster: player 0 Human (the dev plays Red);
        // players 1-5 Computer so the AI step machine schedules
        // their turns. The actual action choice comes from
        // ReplayDrivenAi (overrides the chooser delegate).
        var roster = new List<Player>(_panel.Players.Count);
        for (int i = 0; i < _panel.Players.Count; i++)
        {
            Player src = _panel.Players[i];
            PlayerKind kind = i == 0 ? PlayerKind.Human : PlayerKind.Computer;
            roster.Add(new Player(src.Name, src.Id, kind));
        }

        _previewState = _panel.BuildLiveStateWith(roster);
        // Snapshot restore + turn reset + view-overlay clears live
        // in a pure-C# helper so xUnit can verify the visual reset
        // (PreviewPane itself is Godot-coupled and test-excluded).
        // See PreviewSetupTests for the regression coverage.
        PreviewSetup.Apply(_panel.Map, _previewState, tutorial);

        _hud = new HudView();
        // Relay the HUD's reserved top/bottom insets to the map so it frames
        // the play area (and reflows on orientation flips) — mirrors Main.cs.
        // Without this the map keeps its landscape default insets (top=96,
        // bottom=0) even in portrait, pushing content down. Subscribe BEFORE
        // AddChild so the HUD's _Ready-time publish is caught.
        HexMapView mapForInsets = _panel.Map;
        _hud.MapInsetsChanged += (top, bottom) => mapForInsets.SetMapInsets(top, bottom);
        AddChild(_hud);
        _hud.EscRequested += () => EscRequested?.Invoke();
        // Undo/Redo aren't recorded as beats and would desync the
        // tutorial cursor from player actions — keep them disabled
        // throughout the preview session.
        _hud.SetUndoRedoLocked(true);

        // Shared cursor so the human-side TutorialPreview and AI-side
        // ReplayDrivenAi consume from the same totally-ordered log.
        // Without this the AI never sees the cursor advance past the
        // beats the dev plays as Red, and every non-Red turn no-ops.
        var cursor = new ScriptCursor();
        _replayAi = new ReplayDrivenAi(tutorial.Replay.Beats, roster, cursor);
        _preview = new TutorialPreview(tutorial.Replay.Beats, _previewState, cursor);
        _preview.PlayerActionRejected += OnRejected;
        _preview.TutorialFinished += OnFinished;

        var previewSession = new SessionState();

        // Forward-reference: the controller takes onAfterRefresh as a
        // constructor arg, but TutorialPreviewCues + TutorialNarrationDriver
        // need the controller (for SelectTerritoryForTutorial /
        // RefreshViewsForTutorial). We close over locals that are
        // assigned after all three are constructed; the controller's
        // StartGame call then triggers the first onAfterRefresh, by
        // which time the locals point at the real objects. The driver
        // ticks first so its IsPresenting flag is set before cues
        // check it (cues short-circuit while narration is showing).
        TutorialNarrationDriver? narrationRef = null;
        TutorialPreviewCues? cuesRef = null;
        _controller = new GameController(
            _previewState,
            previewSession,
            _panel.Map,
            _hud,
            seed: _panel.CurrentSeed,
            // While the script has beats, opponents replay their recorded
            // moves; once it's exhausted the session graduates to free
            // play and real AI (AiDispatcher) takes over the opponents.
            aiChooser: (s, c, v, r) => _preview!.IsComplete
                ? AiDispatcher.ChooseForCurrentPlayer(s, c, v, r)
                : _replayAi!.ChooseNextAction(s, c, v, r),
            aiPacer: new GodotAiPacer(new SceneTreeTimerFactory(GetTree())),
            humanActionValidator: _preview.TryAccept,
            buyLevelValidator: _preview.AllowBuyLevel,
            previewMode: true,
            // Seed the recorded tutorial as replay data so the victory
            // overlay's Replay button can auto-play it hands-free via
            // BeginReplay (RecordBeat stays suppressed in preview, so the
            // interactive playthrough doesn't pollute it).
            loadedReplay: tutorial.Replay,
            // Hold the paced AI run while a narration beat is on screen,
            // so opponents don't drain their turns past the parked replay
            // cursor while the player reads/taps. Resumed below.
            isReplayPaused: () => narrationRef?.IsPresenting == true,
            onAfterRefresh: () =>
            {
                narrationRef?.Tick();
                cuesRef?.Apply();
            });

        _narration = new TutorialNarrationDriver(
            tutorial.Replay.Beats,
            cursor,
            _hud,
            // On dismissal: refresh (advances cursor visuals / next Tick),
            // then re-kick the AI in case the narration was blocking an
            // opponent's turn (no-op if it's the human's turn).
            () =>
            {
                _controller!.RefreshViewsForTutorial();
                _controller!.ResumeAiTurnsAfterReplayPause();
            });
        narrationRef = _narration;

        _cues = new TutorialPreviewCues(
            _preview,
            _previewState,
            previewSession,
            _hud,
            _panel.Map,
            roster[0].Id,
            t => _controller!.SelectTerritoryForTutorial(t),
            () => _controller!.CancelActionForTutorial());
        _cues.SetNarrationDriver(_narration);
        cuesRef = _cues;

        // Victory-overlay buttons. The overlay only appears once the
        // tutorial graduates to free play and the game is won (see
        // GraduateFromTutorialScripting); these wire its three actions.
        _hud.NewGameClicked += OnPlayAgainPressed;
        _hud.MainMenuClicked += OnMainMenuPressed;
        _hud.ReplayClicked += OnReplayPressed;
        _controller.GameEnded += OnPreviewGameEnded;

        _savedDragMode = _panel.Map.DragMode;
        _panel.Map.DragMode = HexDragMode.Pan;
        _panel.Map.Init(_previewState);
        _controller.StartGame();
        Log.Debug(Log.LogCategory.Tutorial, $"[PreviewPane] post-StartGame: turn={_previewState.Turns.TurnNumber}, currentPlayer={_previewState.Turns.CurrentPlayerIndex} ({_previewState.Turns.CurrentPlayer.Name})");

        _running = true;
    }

    public void Pause()
    {
        if (!_running) return;

        if (_controller != null) _controller.GameEnded -= OnPreviewGameEnded;
        _controller?.AbandonGame();
        if (_preview != null)
        {
            _preview.PlayerActionRejected -= OnRejected;
            _preview.TutorialFinished -= OnFinished;
        }
        if (_hud != null)
        {
            _hud.NewGameClicked -= OnPlayAgainPressed;
            _hud.MainMenuClicked -= OnMainMenuPressed;
            _hud.ReplayClicked -= OnReplayPressed;
            RemoveChild(_hud);
            _hud.QueueFree();
            _hud = null;
        }

        _panel.Map.DragMode = _savedDragMode;
        _panel.Map.Init(_panel.BuildLiveState());

        _controller = null;
        _previewState = null;
        _replayAi = null;
        _preview = null;
        _cues = null;
        _narration = null;
        _running = false;
    }

    private void OnRejected(ReplayBeat? expected, string reason)
    {
        // Off-script input is a silent no-op — don't surface a rejection
        // toast. Pressing the wrong button / hotkey / tile just aborts
        // (the handler already bailed); the cue's positive instruction
        // ("Press the Buy Recruit button.") stays on screen to guide the
        // player. This matches how mode-entry actions (Build Tower, a
        // wrong Buy level) already no-op via the cue's auto-cancel.
        // TutorialPreview.Reject still logs the reason for diagnostics.
    }

    private void OnFinished()
    {
        // Don't announce "tutorial complete" — hand back to ordinary
        // gameplay rules. The controller lifts the preview's scripted
        // suppressions (win overlay, claim-victory prompt); the validators
        // and AI chooser go permissive on their own once IsComplete.
        _controller?.GraduateFromTutorialScripting();
    }

    private void OnPreviewGameEnded()
    {
        // Enable the victory overlay's Replay button — the seeded tutorial
        // replay is complete from start, so BeginReplay can auto-play it.
        _hud?.SetReplayAvailable(_controller?.ReplayDataIsCompleteFromStart ?? false);
    }

    // Victory overlay: "Play Again" → restart the interactive tutorial from
    // the beginning (resetting the map). Deferred because Start() tears down
    // the HudView that raised this signal — re-entrant teardown mid-emit is
    // unsafe.
    private void OnPlayAgainPressed()
    {
        Log.Info(Log.LogCategory.Tutorial, "[PreviewPane] Play Again — restarting tutorial preview");
        Tutorial? t = _tutorial;
        if (t != null) Callable.From(() => Start(t)).CallDeferred();
    }

    // Victory overlay: "Main Menu" → leave the builder for the main menu.
    private void OnMainMenuPressed()
    {
        Log.Info(Log.LogCategory.Tutorial, "[PreviewPane] Main Menu — leaving to main menu");
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }

    // Victory overlay: "Replay" → auto-play the whole tutorial hands-free
    // via the standard replay step machine (rewinds to the recorded start,
    // executes every action beat; narration beats are skipped).
    private void OnReplayPressed()
    {
        Log.Info(Log.LogCategory.Tutorial, "[PreviewPane] Replay — auto-playing tutorial");
        _controller?.BeginReplay();
    }
}
