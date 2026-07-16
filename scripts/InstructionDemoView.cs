// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// The Instructions dialog's graphics panel: a miniature second game
/// board playing a recorded tutorial script hands-free, on a loop, like
/// a short movie demonstrating a rule (issue #134).
///
/// <para>
/// Architecture: a complete second (model, controller, view) stack —
/// its own <see cref="GameState"/>/<see cref="SessionState"/>, its own
/// <see cref="GameController"/> used purely as a replay-playback engine
/// (<c>BeginReplay</c> over the tutorial's beats; no input wired, no AI),
/// and its own <see cref="HexMapView"/> rendered inside an input-isolated
/// <see cref="SubViewport"/>. The HUD is a no-op <see cref="HeadlessHudView"/>.
/// The live game behind the modal shares no mutable state with any of it.
/// Audio is pinned off (<see cref="HexMapView.SetMutePinned"/>) so looping
/// playback never sounds over the live game.
/// </para>
///
/// <para>
/// Display: the SubViewport renders continuously and a
/// <see cref="TextureRect"/> shows its <see cref="ViewportTexture"/> live,
/// letterboxed into this control — the <c>MapThumbnailView</c> hosting
/// pattern, minus the snapshot (this one animates).
/// </para>
/// </summary>
public sealed partial class InstructionDemoView : Control
{
    // Pause on the final frame before the loop rewinds, so the demo's
    // end state is readable before it snaps back to the start.
    private const float LoopPauseSec = 1.5f;
    // Render-resolution clamp for the offscreen viewport (long edge).
    private const float MaxRenderPx = 1024f;

    private SubViewport _viewport = null!;
    private TextureRect _display = null!;

    private HexMapView? _map;
    private GameController? _controller;
    private bool _running;
    // Generation counter: bumped by every Stop so a loop-pause timer
    // armed by an older session can't restart a torn-down replay.
    private int _generation;
    // Frozen: display holds its last rendered frame (viewport stops
    // updating) and replay playback parks via the controller's
    // isReplayPaused hook — the swipe carousel freezes the outgoing
    // demo during a drag and thaws it seamlessly on spring-back.
    private bool _frozen;
    // A loop restart that came due while frozen; runs on thaw.
    private bool _pendingLoopRestart;

    public override void _Ready()
    {
        _display = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
            TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps,
        };
        _display.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_display);

        _viewport = new SubViewport
        {
            Size = new Vector2I(256, 256),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            TransparentBg = true,
            // Display-only: never route any input into the demo world.
            GuiDisableInput = true,
            HandleInputLocally = false,
        };
        AddChild(_viewport);

        GetViewport().SizeChanged += OnHostResized;
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= OnHostResized;
    }

    /// <summary>
    /// Build the demo stack for <paramref name="loaded"/> (a bundled
    /// tutorial save: starting map + roster + recorded beats) and start
    /// looping playback. Idempotent — a second Play tears down the prior
    /// session first. No-op (logged) if the save carries no tutorial.
    /// </summary>
    public void Play(LoadedSave loaded)
    {
        Stop();
        Tutorial? tutorial = loaded.Tutorial;
        if (tutorial == null)
        {
            Log.Warn(Log.LogCategory.Tutorial,
                "[instr] demo save has no tutorial block — nothing to play");
            return;
        }

        GameState state = loaded.State;
        var session = new SessionState();

        _frozen = false;
        _pendingLoopRestart = false;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

        _map = new HexMapView();
        _map.Init(state);
        SizeViewportToGrid();
        _viewport.AddChild(_map);
        _map.SetMapInsets(0, 0);
        _map.SetMutePinned(true);
        _display.Texture = _viewport.GetTexture();

        // Replay-playback engine only: all-headless HUD, paced pacer,
        // preview mode (no divergence checksum against the starting map,
        // no beat recording), pinned to the paced track so the demo
        // always animates regardless of the user's Replay Speed setting.
        // processAlways: the Help family pauses the tree while it's up —
        // the demo must keep animating behind that freeze.
        _controller = new GameController(
            state,
            session,
            _map,
            new HeadlessHudView(),
            seed: loaded.MasterSeed,
            aiPacer: new GodotAiPacer(new SceneTreeTimerFactory(GetTree(), processAlways: true)),
            previewMode: true,
            loadedReplay: tutorial.Replay,
            replayIsInstantMode: () => false,
            isReplayPaused: () => _frozen,
            autoSelectFirstTerritory: false);
        _controller.ReplayEnded += OnReplayEnded;

        _running = true;
        Log.Info(Log.LogCategory.Tutorial,
            $"[instr] demo start: \"{tutorial.Title}\", beats={tutorial.Replay.Beats.Count}");
        _controller.BeginReplay();
    }

    /// <summary>Tear down the demo stack (mirrors <c>PreviewPane.Pause</c>).
    /// Safe to call when nothing is playing.</summary>
    public void Stop()
    {
        _generation++;
        if (!_running && _controller == null && _map == null) return;

        if (_controller != null)
        {
            _controller.ReplayEnded -= OnReplayEnded;
            _controller.AbandonGame();
            _controller = null;
        }
        if (_map != null)
        {
            _display.Texture = null;
            _viewport.RemoveChild(_map);
            _map.QueueFree();
            _map = null;
        }
        if (_running)
        {
            Log.Info(Log.LogCategory.Tutorial, "[instr] demo stop");
        }
        _running = false;
    }

    /// <summary>
    /// Freeze/thaw the demo in place: frozen, the display keeps its last
    /// rendered frame and playback parks (via the controller's pause
    /// hook); thawed, rendering and playback resume exactly where they
    /// stopped. Used by the swipe carousel while pages are in motion.
    /// </summary>
    public void SetFrozen(bool frozen)
    {
        if (_frozen == frozen || !_running) return;
        _frozen = frozen;
        _viewport.RenderTargetUpdateMode = frozen
            ? SubViewport.UpdateMode.Disabled
            : SubViewport.UpdateMode.Always;
        if (frozen) return;

        _controller?.ResumeReplayAfterPause();
        if (_pendingLoopRestart && _controller != null)
        {
            _pendingLoopRestart = false;
            Log.Info(Log.LogCategory.Tutorial, "[instr] demo loop restart");
            _controller.BeginReplay();
        }
    }

    // Loop: hold the final frame briefly, then rewind and replay. The
    // generation guard drops the restart if Stop/Play happened while the
    // pause timer was pending; a restart that comes due while frozen
    // waits for the thaw.
    private void OnReplayEnded()
    {
        if (!_running) return;
        int generation = _generation;
        GetTree().CreateTimer(LoopPauseSec).Timeout += () =>
        {
            if (!_running || generation != _generation || _controller == null) return;
            if (_frozen)
            {
                _pendingLoopRestart = true;
                return;
            }
            Log.Info(Log.LogCategory.Tutorial, "[instr] demo loop restart");
            _controller.BeginReplay();
        };
    }

    // Offscreen render target sized to the board's nominal pixel aspect
    // (content-independent, like MapThumbnailView), long edge clamped.
    // In portrait the aspect is swapped tall, which makes the HexMapView
    // inside rotate the board −90° — the same rule as the in-game map.
    private void SizeViewportToGrid()
    {
        if (_map == null) return;
        Vector2 grid = _map.PixelSize;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        bool portrait = ScreenLayout.Resolve(vp.X, vp.Y) == ScreenOrientation.Portrait;
        (float w, float h) = ThumbnailLayout.OrientedFit(
            grid.X, grid.Y, portrait, MaxRenderPx, MaxRenderPx);
        var size = new Vector2I(
            Mathf.Max(1, Mathf.RoundToInt(w)),
            Mathf.Max(1, Mathf.RoundToInt(h)));
        if (_viewport.Size != size) _viewport.Size = size;
    }

    // Track host-window resizes/rotations: re-shape the offscreen
    // viewport, and the HexMapView inside re-resolves its own rotation
    // and framing from its viewport's SizeChanged.
    private void OnHostResized() => SizeViewportToGrid();
}
