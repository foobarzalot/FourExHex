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
        _controller = new GameController(
            state,
            session,
            _map,
            new HeadlessHudView(),
            seed: loaded.MasterSeed,
            aiPacer: new GodotAiPacer(new SceneTreeTimerFactory(GetTree())),
            previewMode: true,
            loadedReplay: tutorial.Replay,
            replayIsInstantMode: () => false,
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

    // Loop: hold the final frame briefly, then rewind and replay. The
    // generation guard drops the restart if Stop/Play happened while the
    // pause timer was pending.
    private void OnReplayEnded()
    {
        if (!_running) return;
        int generation = _generation;
        GetTree().CreateTimer(LoopPauseSec).Timeout += () =>
        {
            if (!_running || generation != _generation || _controller == null) return;
            Log.Info(Log.LogCategory.Tutorial, "[instr] demo loop restart");
            _controller.BeginReplay();
        };
    }

    // Offscreen render target sized to the board's nominal pixel aspect
    // (content-independent, like MapThumbnailView), long edge clamped.
    private void SizeViewportToGrid()
    {
        Vector2 grid = _map!.PixelSize;
        float scale = MaxRenderPx / Mathf.Max(grid.X, grid.Y);
        _viewport.Size = new Vector2I(
            Mathf.Max(1, Mathf.RoundToInt(grid.X * scale)),
            Mathf.Max(1, Mathf.RoundToInt(grid.Y * scale)));
    }
}
