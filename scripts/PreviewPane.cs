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
    private HexDragMode _savedDragMode;
    private bool _running;

    public override void _Ready()
    {
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Ignore;
        Size = GetViewport().GetVisibleRect().Size;
        GetViewport().SizeChanged += () => Size = GetViewport().GetVisibleRect().Size;
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
        GD.Print($"[PreviewPane] Start: beats={tutorial.Replay.Beats.Count}, initialTurn={tutorial.Replay.InitialTurnNumber}, initialPlayer={tutorial.Replay.InitialCurrentPlayerIndex}");
        if (_running) Pause();

        // Player roster: player 0 Human (the dev plays Red);
        // players 1-5 Heuristic so the AI step machine schedules
        // their turns. The actual action choice comes from
        // ReplayDrivenAi (overrides the chooser delegate).
        var roster = new List<Player>(_panel.Players.Count);
        for (int i = 0; i < _panel.Players.Count; i++)
        {
            Player src = _panel.Players[i];
            AiKind kind = i == 0 ? AiKind.Human : AiKind.Heuristic;
            roster.Add(new Player(src.Name, src.Color, kind));
        }

        _previewState = _panel.BuildLiveStateWith(roster);
        // Snapshot restore + turn reset + view-overlay clears live
        // in a pure-C# helper so xUnit can verify the visual reset
        // (PreviewPane itself is Godot-coupled and test-excluded).
        // See PreviewSetupTests for the regression coverage.
        PreviewSetup.Apply(_panel.Map, _previewState, tutorial);

        _hud = new HudView();
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
        // constructor arg, but TutorialPreviewCues needs the controller
        // (for SelectTerritoryForTutorial). We close over a local that
        // is assigned after both are constructed; the controller's
        // StartGame call then triggers the first onAfterRefresh, by
        // which time the local points at the real cues object.
        TutorialPreviewCues? cuesRef = null;
        _controller = new GameController(
            _previewState,
            previewSession,
            _panel.Map,
            _hud,
            seed: _panel.CurrentSeed,
            aiChooser: _replayAi.ChooseNextAction,
            aiPacer: new GodotAiPacer(new SceneTreeTimerFactory(GetTree())),
            humanActionValidator: _preview.TryAccept,
            previewMode: true,
            onAfterRefresh: () => cuesRef?.Apply());

        _cues = new TutorialPreviewCues(
            _preview,
            _previewState,
            previewSession,
            _hud,
            _panel.Map,
            roster[0].Color,
            t => _controller!.SelectTerritoryForTutorial(t),
            () => _controller!.CancelActionForTutorial());
        cuesRef = _cues;

        _savedDragMode = _panel.Map.DragMode;
        _panel.Map.DragMode = HexDragMode.Pan;
        _panel.Map.Init(_previewState);
        _controller.StartGame();
        GD.Print($"[PreviewPane] post-StartGame: turn={_previewState.Turns.TurnNumber}, currentPlayer={_previewState.Turns.CurrentPlayerIndex} ({_previewState.Turns.CurrentPlayer.Name})");

        _running = true;
    }

    public void Pause()
    {
        if (!_running) return;

        _controller?.AbandonGame();
        if (_preview != null)
        {
            _preview.PlayerActionRejected -= OnRejected;
            _preview.TutorialFinished -= OnFinished;
        }
        if (_hud != null)
        {
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
        _running = false;
    }

    private void OnRejected(ReplayBeat? expected, string reason)
    {
        _hud?.ShowTutorialMessage(reason);
    }

    private void OnFinished()
    {
        _hud?.ShowTutorialMessage("Tutorial complete.");
    }
}
