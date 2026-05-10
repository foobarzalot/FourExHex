using System.Collections.Generic;
using Godot;

/// <summary>
/// Preview mode chrome. Phase 3c: builds a transient GameController +
/// fresh HudView + gated view wrappers when entered (<see cref="Start"/>),
/// tears it all down when exited (<see cref="Pause"/>). The panel's
/// HexMapView is shared but re-pointed at a cloned GameState via
/// <c>HexMapView.Init</c> so the controller's mutations don't touch
/// the editor's draft. On Pause the panel re-pushes its draft state
/// to repaint the map back to authored view.
///
/// Phase 13 adds the scrubber chrome on top of this.
/// </summary>
public sealed partial class PreviewPane : Control
{
    private MapEditorPanel _panel = null!;

    private TutorialPlayer? _player;
    private HudView? _hud;
    private TutorialGatedHexMapView? _gatedMap;
    private TutorialGatedHudView? _gatedHud;
    private GameController? _controller;
    private GameState? _previewState;
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
        GetViewport().SizeChanged += OnViewportResized;
    }

    private void OnViewportResized()
    {
        Size = GetViewport().GetVisibleRect().Size;
    }

    /// <summary>
    /// Called once by <see cref="TutorialBuilderScene._Ready"/> to hand
    /// the pane the panel it should bind to. The panel exposes its
    /// own <c>Players</c> roster — PreviewPane uses
    /// <c>_panel.BuildLiveState()</c> which threads them in.
    /// </summary>
    public void Configure(MapEditorPanel panel)
    {
        _panel = panel;
    }

    /// <summary>
    /// Enter Preview mode with the given Tutorial. Builds the transient
    /// controller graph, repaints the shared HexMapView from the cloned
    /// state, and calls <c>controller.StartGame()</c>. Idempotent — a
    /// second Start without a Pause in between tears down the previous
    /// session first.
    /// </summary>
    public void Start(Tutorial tutorial)
    {
        if (_running) Pause();

        // 1. Build a fresh GameState from the panel's draft. Cloning
        //    happens implicitly: BuildLiveState wraps the panel's grid
        //    in a new GameState pointing at the same HexGrid object.
        //    For 3c minimal scope we accept that the controller mutates
        //    the panel's grid directly during Preview; on Pause we
        //    re-init HexMapView with the panel's draft re-pushed to
        //    snap visuals back. (Mutations are limited to End Turn,
        //    which only advances TurnState — no tile changes — so
        //    nothing actually needs reverting in 3c. Phase 4+ will
        //    revisit cloning when Move/BuyPeasant beats land.)
        _previewState = _panel.BuildLiveState();
        var session = new SessionState();

        // 2. Build the transient HudView. Position at the default
        //    y=0..60 strip; TutorialBuilderScene hides the topbar
        //    while in Preview to avoid overlap.
        _hud = new HudView();
        AddChild(_hud);

        // 3. Build TutorialPlayer + gated wrappers.
        _player = new TutorialPlayer(tutorial);
        _gatedMap = new TutorialGatedHexMapView(_panel.Map, _player);
        _gatedHud = new TutorialGatedHudView(_hud, _player);

        // 4. Toast on rejection / completion.
        _player.PlayerActionRejected += OnPlayerActionRejected;
        _player.TutorialFinished += OnTutorialFinished;

        // 5. Construct the transient GameController. AiChooser is the
        //    player's (3c always falls through to AiDispatcher).
        //    SynchronousAiPacer per spec — Preview wants snappy
        //    step-by-step.
        _controller = new GameController(
            _previewState,
            session,
            _gatedMap,
            _gatedHud,
            seed: _panel.CurrentSeed,
            aiChooser: _player.AiChooser,
            aiPacer: new SynchronousAiPacer());

        // 6. Force HexMapView's DragMode to Pan so tile clicks fire
        //    TileClicked (Paint mode would route them into the paint-
        //    stroke path, which never raises TileClicked — so the
        //    gated wrapper would never see the click and no soft-
        //    reject toast would appear). Restored on Pause.
        _savedDragMode = _panel.Map.DragMode;
        _panel.Map.DragMode = HexDragMode.Pan;

        // 7. Re-point the shared HexMapView at the preview state and
        //    fire StartGame. The controller's RefreshViews call inside
        //    StartGame repaints everything from the new state.
        _panel.Map.Init(_previewState);
        _controller.StartGame();

        _running = true;
    }

    /// <summary>
    /// Exit Preview mode. Unsubscribes the wrappers from the panel's
    /// real HexMapView (so stale handlers don't fire when the dev
    /// clicks during Map Edit / Build), removes the transient HudView
    /// from the scene, and restores the panel's view of its draft.
    /// </summary>
    public void Pause()
    {
        if (!_running) return;

        _gatedMap?.Unbind();
        _gatedHud?.Unbind();

        if (_player != null)
        {
            _player.PlayerActionRejected -= OnPlayerActionRejected;
            _player.TutorialFinished -= OnTutorialFinished;
        }

        if (_hud != null)
        {
            RemoveChild(_hud);
            _hud.QueueFree();
            _hud = null;
        }

        // Restore the panel's view of its draft. SnapshotDraft +
        // RestoreDraft round-trips the panel's grid/water/territories
        // and re-pushes them to HexMapView via PushState — so even if
        // the controller mutated tile state, the panel's authored
        // view is back on screen.
        EditorSnapshot snap = _panel.SnapshotDraft();
        _panel.RestoreDraft(snap);

        // Restore the DragMode that was active before Preview started.
        _panel.Map.DragMode = _savedDragMode;

        _controller = null;
        _gatedMap = null;
        _gatedHud = null;
        _player = null;
        _previewState = null;
        _running = false;
    }

    private void OnPlayerActionRejected(Beat? expected, string reason)
    {
        // The HUD shows the toast. PlayerActionRejected may also fire
        // when the gated wrapper rejects a HUD click — the wrapper's
        // ShowTutorialMessage path calls back into _hud anyway.
        _hud?.ShowTutorialMessage(reason);
    }

    private void OnTutorialFinished()
    {
        _hud?.ShowTutorialMessage("Tutorial complete.");
    }
}
