// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Godot;

/// <summary>
/// Hands-free demo playback scene, reached only from the cheat menu's
/// Demo Replays picker: plays a bundled <c>demo_*</c> tutorial from
/// <c>res://tutorials/</c> as a paced replay with Recording Mode forced
/// on (promo chrome hidden) — for clean promotional captures.
///
/// <para>
/// Structure mirrors <see cref="PlayTutorialScene"/> (a
/// <see cref="MapEditorPanel"/> as the map host) but the controller is a
/// pure replay-playback engine like <see cref="InstructionDemoView"/>:
/// real <see cref="HudView"/>, <c>previewMode</c>, no AI chooser, pinned
/// to the paced track, driven solely by <c>BeginReplay</c>. When the
/// replay ends the scene holds the final frame briefly, restores
/// Recording Mode to its prior state, and returns to the main menu.
/// </para>
/// </summary>
public partial class DemoReplayScene : Node2D
{
    /// <summary>One-shot handoff from <see cref="CheatMenu"/> across
    /// <c>ChangeSceneToFile</c> — the demo slot name to play. Mirrors the
    /// static-state idiom of <see cref="LoadRequest"/>.</summary>
    public static string? PendingName { get; set; }

    // Hold the final frame before leaving, so the demo's end state is
    // readable in a capture (same idea as InstructionDemoView's loop pause).
    private const float EndHoldSec = 1.5f;

    private MapEditorPanel _panel = null!;
    private HudView? _hud;
    private GameController? _controller;
    private EscMenu _escMenu = null!;
    // True iff this scene turned Recording Mode on (and so must turn it
    // back off on exit); a session that arrived with it already active
    // keeps it active.
    private bool _forcedRecording;
    private bool _exited;

    public override void _Ready()
    {
        string? name = PendingName;
        PendingName = null;

        _panel = new MapEditorPanel { Players = Player.BuildAllHumanRoster() };
        AddChild(_panel);
        _panel.PaintingEnabled = false;

        _escMenu = new EscMenu();
        AddChild(_escMenu);

        if (name == null)
        {
            Log.Error(Log.LogCategory.Tutorial, "[demo] no pending demo name — returning to menu");
            ExitToMenu();
            return;
        }

        LoadedSave loaded;
        try
        {
            loaded = new SaveStore().LoadBundledTutorial(name);
        }
        catch (System.Exception ex)
        {
            Log.Error(Log.LogCategory.Tutorial,
                $"[demo] could not load bundled demo '{name}': {ex.Message}");
            ExitToMenu();
            return;
        }

        if (loaded.Tutorial?.Replay == null)
        {
            Log.Error(Log.LogCategory.Tutorial,
                $"[demo] bundled save '{name}' has no tutorial/replay payload");
            ExitToMenu();
            return;
        }

        // Mirrors PlayTutorialScene: load the demo's map, trim the roster
        // to the colors that own land, rewind to the recording's start.
        _panel.LoadFromMap(loaded);
        _panel.Players = MapRosterRules.ActivePlayersForTerritories(
            _panel.Players, loaded.State.Territories);
        _panel.ResetToTutorialStart(loaded.Tutorial.Replay.InitialSnapshot);

        // Player 0 Human, rest Computer (mirrors PreviewPane.Start): fog
        // only projects for a roster with exactly one human
        // (VisibilityRules.BuildProjection), so an all-Human roster would
        // play a Fog Of War demo fully revealed. Kinds don't affect
        // playback — the replay log drives every player.
        var roster = new List<Player>(_panel.Players.Count);
        for (int i = 0; i < _panel.Players.Count; i++)
        {
            Player src = _panel.Players[i];
            roster.Add(new Player(src.Name, src.Id,
                i == 0 ? PlayerKind.Human : PlayerKind.Computer));
        }

        GameState state = _panel.BuildLiveStateWith(roster);
        PreviewSetup.Apply(_panel.Map, state, loaded.Tutorial);

        _hud = new HudView();
        // Relay the HUD's reserved insets to the map so it frames the play
        // area and reflows on orientation flips — mirrors PreviewPane.
        // Subscribe BEFORE AddChild so the _Ready-time publish is caught.
        HexMapView mapForInsets = _panel.Map;
        _hud.MapInsetsChanged += (top, bottom) => mapForInsets.SetMapInsets(top, bottom);
        AddChild(_hud);
        _hud.EscRequested += OpenPauseMenu;
        _hud.SetUndoRedoLocked(true);

        // Replay-playback engine only (the InstructionDemoView construction,
        // with the real HUD): no AI chooser, preview mode (no divergence
        // checksum, no beat recording), pinned paced so the demo always
        // animates regardless of the user's Replay Speed setting.
        _controller = new GameController(
            state,
            new SessionState(),
            _panel.Map,
            _hud,
            seed: loaded.MasterSeed,
            // Null chooser: the Computer slots exist only for the fog
            // perspective (roster above); if the recording ends short of
            // game over they must idle out the end-hold, not free-play.
            aiChooser: (s, c, v, ru, r) => null,
            aiPacer: new GodotAiPacer(new SceneTreeTimerFactory(GetTree())),
            previewMode: true,
            loadedReplay: loaded.Tutorial.Replay,
            replayIsInstantMode: () => false,
            replayFastForwardsIdleTurns: true,
            autoSelectFirstTerritory: false);
        _controller.ReplayEnded += OnReplayEnded;
        // Camera follow (demo playback only): as each beat previews, pan
        // to its effect tile if it sits outside the comfort zone. Zoom is
        // untouched — whatever the operator sets frames the capture.
        HexMapView mapForFollow = _panel.Map;
        _controller.ReplayBeatPreviewing += beat =>
        {
            if (ReplayFocus.FocusCoord(beat) is HexCoord focus)
            {
                mapForFollow.CenterOnCoordIfOffscreen(focus);
            }
        };

        _forcedRecording = !RecordingMode.Active;
        if (_forcedRecording) RecordingMode.Toggle();
        Log.Info(Log.LogCategory.Tutorial,
            $"[demo] recording mode {(_forcedRecording ? "forced on" : "already on")}");

        _panel.Map.DragMode = HexDragMode.Pan;
        _panel.Map.Init(state);

        Log.Info(Log.LogCategory.Tutorial,
            $"[demo] start '{name}' ({loaded.Tutorial.Replay.Beats.Count} beats)");
        _controller.BeginReplay();

#if DEBUG
        CheatMenu.Attach(this);
#endif
    }

    // Hold the final frame, then leave. The _exited flag also guards the
    // timer firing after an early ESC exit already tore the session down.
    private void OnReplayEnded()
    {
        Log.Info(Log.LogCategory.Tutorial, "[demo] replay ended — holding final frame");
        GetTree().CreateTimer(EndHoldSec).Timeout += () =>
        {
            if (!_exited) ExitToMenu();
        };
    }

    private void OpenPauseMenu()
    {
        Log.Info(Log.LogCategory.Tutorial, "[demo] pause menu opened");
        _escMenu.Show(Strings.Get(StringKeys.PauseTitle), new List<EscMenu.Option>
        {
            new(Strings.Get(StringKeys.MenuResume), () => { }),
            new(Strings.Get(StringKeys.HudButtonMainMenu), ExitToMenu),
        });
    }

    private void ExitToMenu()
    {
        if (_exited) return;
        _exited = true;

        if (_controller != null)
        {
            _controller.ReplayEnded -= OnReplayEnded;
            _controller.AbandonGame();
        }
        if (_forcedRecording && RecordingMode.Active)
        {
            RecordingMode.Toggle();
            Log.Info(Log.LogCategory.Tutorial, "[demo] recording mode restored off");
        }
        Log.Info(Log.LogCategory.Tutorial, "[demo] exit to main menu");
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
