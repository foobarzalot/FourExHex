using System.Collections.Generic;
using Godot;

/// <summary>
/// Record-mode chrome. The dev plays the game as all six humans;
/// every state-mutating action is captured automatically by the
/// <see cref="GameController"/>'s replay-recording machinery via the
/// normal TrackHandler / StepAi* pipeline.
///
/// <para>
/// On entering Record (via <see cref="StartRecording"/>), this pane:
/// builds an all-Human roster (every slot <c>AiKind.Human</c>), spins
/// up a transient real <see cref="HudView"/> + <see cref="GameController"/>
/// against the panel's painted draft, forces drag-mode to Pan so tile
/// clicks fire, and calls <c>StartGame</c>. The controller's normal
/// recording sites populate <c>_replayBeats</c> as the dev plays.
/// </para>
///
/// <para>
/// <see cref="CurrentTutorial"/> is the live in-memory tutorial
/// captured by the most recent (or current) recording session. The
/// TutorialBuilder reads it when the dev clicks Save Tutorial in the
/// topbar.
/// </para>
/// </summary>
public sealed partial class RecordPane : Control
{
    private MapEditorPanel _panel = null!;
    private HudView? _hud;
    private GameController? _controller;
    private GameState? _recordState;
    private HexDragMode _savedDragMode;
    private bool _running;
    // Pure-C# captor for the captured tutorial. Lives separately from
    // _controller so the snapshot survives StopRecording (which nulls
    // the controller). Without this the live bug was: SetMode runs
    // StopRecording then reads CurrentTutorial → null → Preview
    // never starts. See RecordingCapture + RecordPaneCaptureTests.
    private readonly RecordingCapture _capture = new();

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

    /// <summary>One-time wire-up from the scene root.</summary>
    public void SetPanel(MapEditorPanel panel)
    {
        _panel = panel;
    }

    /// <summary>
    /// In-memory tutorial captured by the most recent (or current)
    /// recording session. Null only before any recording has begun.
    /// Survives <see cref="StopRecording"/> — the underlying
    /// <see cref="RecordingCapture"/> preserves the snapshot + beat
    /// list after the controller is torn down.
    /// </summary>
    public Tutorial? CurrentTutorial
    {
        get
        {
            // Refresh the beat list snapshot from the live controller
            // while recording is in progress, so a mid-recording Save
            // sees the latest beats.
            if (_running && _controller != null)
            {
                _capture.SetBeats(new List<ReplayBeat>(_controller.ReplayBeats));
            }
            return _capture.Snapshot();
        }
    }

    /// <summary>
    /// Enter Record mode. Builds the transient controller + HUD over
    /// the panel's draft with all six slots forced Human. Idempotent —
    /// a second call without StopRecording first tears down the prior
    /// session.
    /// </summary>
    public void StartRecording()
    {
        GD.Print($"[RecordPane] StartRecording (was running={_running})");
        if (_running) StopRecording();

        // All-Human roster: keep the panel's colors/names so the grid
        // partition matches, but force every slot to Human so no AI
        // ever takes a turn.
        var roster = new List<Player>(_panel.Players.Count);
        foreach (Player p in _panel.Players)
        {
            roster.Add(new Player(p.Name, p.Color, AiKind.Human));
        }

        _recordState = _panel.BuildLiveStateWith(roster);
        _hud = new HudView();
        AddChild(_hud);

        _controller = new GameController(
            _recordState,
            new SessionState(),
            _panel.Map,
            _hud,
            seed: _panel.CurrentSeed,
            aiChooser: null,
            aiPacer: new SynchronousAiPacer());

        _savedDragMode = _panel.Map.DragMode;
        _panel.Map.DragMode = HexDragMode.Pan;
        _panel.Map.Init(_recordState);
        _controller.StartGame();

        // After StartGame the controller has captured its initial
        // snapshot. Begin the capture session with those values so
        // CurrentTutorial works from this point forward.
        if (_controller.InitialReplaySnapshot != null)
        {
            _capture.Begin(
                _controller.InitialReplaySnapshot,
                _controller.InitialReplayTurnNumber,
                _controller.InitialReplayCurrentPlayerIndex);
            _capture.SetBeats(new List<ReplayBeat>(_controller.ReplayBeats));
            GD.Print($"[RecordPane] Capture.Begin: turn={_controller.InitialReplayTurnNumber}, player={_controller.InitialReplayCurrentPlayerIndex}");
        }
        else
        {
            GD.Print("[RecordPane] WARNING: controller has no initial snapshot after StartGame");
        }

        _running = true;
    }

    /// <summary>
    /// Exit Record mode. Tears down the transient controller and HUD;
    /// restores drag mode; re-applies the panel's draft so the map
    /// shows the authored terrain (not the play-through state). The
    /// captured tutorial survives in <see cref="CurrentTutorial"/>
    /// for later Save / Preview consumption.
    /// </summary>
    public void StopRecording()
    {
        GD.Print($"[RecordPane] StopRecording (running={_running}, beats={_controller?.ReplayBeats.Count ?? -1})");
        if (!_running) return;

        // Snapshot the final beat list into the capture before nulling
        // the controller — Stop() itself doesn't touch the existing
        // snapshot fields.
        if (_controller != null)
        {
            _capture.SetBeats(new List<ReplayBeat>(_controller.ReplayBeats));
            _capture.Stop();
        }

        _controller?.AbandonGame();
        if (_hud != null)
        {
            RemoveChild(_hud);
            _hud.QueueFree();
            _hud = null;
        }

        _panel.Map.DragMode = _savedDragMode;
        _panel.Map.Init(_panel.BuildLiveState());

        _controller = null;
        _recordState = null;
        _running = false;
    }
}
