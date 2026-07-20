// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
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
/// builds an all-Human roster (every slot <c>PlayerKind.Human</c>), spins
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
    /// <summary>
    /// Forwarded from the inner HudView. Fires whenever the player asks
    /// for the pause modal (ESC with no pending action, or the End Game
    /// button). The scene root subscribes and shows its EscMenu.
    /// </summary>
    public event Action? EscRequested;

    private MapEditorPanel _panel = null!;
    private HudView? _hud;
    private GameController? _controller;
    private GameState? _recordState;
    // Kept so the Add Select button can read the current selection —
    // the controller doesn't expose it.
    private SessionState? _recordSession;
    private HexDragMode _savedDragMode;
    private bool _running;
    private CanvasLayer? _addTextDialog;
    // Pure-C# captor for the captured tutorial. Lives separately from
    // _controller so the captured snapshot survives StopRecording (which
    // nulls the controller).
    private readonly RecordingCapture _capture = new();

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
    /// True iff there's a non-empty recording the dev would lose by
    /// switching back to Map Edit. Drives the TutorialBuilder's
    /// "Discard recording?" confirm path.
    /// </summary>
    public bool HasRecording
    {
        get
        {
            Tutorial? t = CurrentTutorial;
            return t != null && t.Replay.Beats.Count > 0;
        }
    }

    /// <summary>
    /// Erase the captured recording. Caller must already have torn
    /// down the live recording session (call <see cref="StopRecording"/>
    /// before this, or invoke from a state where no controller is
    /// live). Used by the TutorialBuilder after the dev confirms
    /// "switch to Map Edit and clear the recording".
    /// </summary>
    public void DiscardRecording()
    {
        _capture.Reset();
    }

    /// <summary>
    /// Pre-populate <see cref="CurrentTutorial"/> from a loaded Tutorial
    /// without starting a recording session. Used by the TutorialBuilder
    /// after Load Tutorial so the subsequent SetMode(Record) sees the
    /// loaded beats and continues recording instead of starting fresh.
    /// </summary>
    public void PrimeForContinue(Tutorial loaded)
    {
        if (_running) StopRecording();
        _capture.Begin(
            loaded.Replay.InitialSnapshot,
            loaded.Replay.InitialTurnNumber,
            loaded.Replay.InitialCurrentPlayerIndex);
        _capture.SetBeats(new System.Collections.Generic.List<ReplayBeat>(loaded.Replay.Beats));
    }

    /// <summary>
    /// Enter Record mode. Builds the transient controller + HUD over
    /// the panel's draft with all six slots forced Human. Idempotent —
    /// a second call without StopRecording first tears down the prior
    /// session.
    /// </summary>
    public void StartRecording()
    {
        Log.Debug(Log.LogCategory.Tutorial, $"[RecordPane] StartRecording (was running={_running})");
        if (_running) StopRecording();

        // All-Human roster: keep the panel's colors/names so the grid
        // partition matches, but force every slot to Human so no AI
        // ever takes a turn.
        var roster = new List<Player>(_panel.Players.Count);
        foreach (Player p in _panel.Players)
        {
            roster.Add(new Player(p.Name, p.Id, PlayerKind.Human));
        }

        _recordState = _panel.BuildLiveStateWith(roster);
        _hud = new HudView();
        // Authoring exemption: the controller below runs recordingMode, whose
        // fog undo gates are open — unlock the buttons to match.
        _hud.SetFogUndoExempt(true);
        AddChild(_hud);
        _hud.EscRequested += () => EscRequested?.Invoke();
        _hud.AddTextClicked += OpenAddTextDialog;
        _hud.SetAddTextButtonVisible(true);
        _hud.AddSelectClicked += RecordSelectBeat;
        _hud.SetAddSelectButtonVisible(true);
        _hud.AddDemoStartClicked += RecordDemoStartBeat;
        _hud.SetAddDemoStartButtonVisible(true);

        _recordSession = new SessionState();
        _controller = new GameController(
            _recordState,
            _recordSession,
            _panel.Map,
            _hud,
            seed: _panel.CurrentSeed,
            aiChooser: null,
            aiPacer: new SynchronousAiPacer(),
            recordingMode: true,
            // Tutorial authoring drives its own selections; keep the
            // turn-start auto-selection (#94) out of the recorded session.
            autoSelectFirstTerritory: false);

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
            Log.Debug(Log.LogCategory.Tutorial, $"[RecordPane] Capture.Begin: turn={_controller.InitialReplayTurnNumber}, player={_controller.InitialReplayCurrentPlayerIndex}");
        }
        else
        {
            Log.Warn(Log.LogCategory.Tutorial, "[RecordPane] controller has no initial snapshot after StartGame");
        }

        _running = true;
    }

    /// <summary>
    /// Re-enter Record mode atop an existing recording. Builds a
    /// transient controller seeded with the captured Replay, calls
    /// <see cref="GameController.BeginReplay"/> so the synchronous
    /// pacer drains every recorded beat inline and leaves the state
    /// at the recording's end — and <c>_replayMode = false</c>, so the
    /// dev's subsequent inputs append new beats to the existing list.
    /// Used when the dev returns to Record after Preview.
    /// </summary>
    public void ContinueRecording(Tutorial previous)
    {
        Log.Debug(Log.LogCategory.Tutorial, $"[RecordPane] ContinueRecording (was running={_running}, beats={previous.Replay.Beats.Count})");
        if (_running) StopRecording();

        var roster = new List<Player>(_panel.Players.Count);
        foreach (Player p in _panel.Players)
        {
            roster.Add(new Player(p.Name, p.Id, PlayerKind.Human));
        }

        _recordState = _panel.BuildLiveStateWith(roster);
        _hud = new HudView();
        // Authoring exemption: the controller below runs recordingMode, whose
        // fog undo gates are open — unlock the buttons to match.
        _hud.SetFogUndoExempt(true);
        AddChild(_hud);
        _hud.EscRequested += () => EscRequested?.Invoke();
        _hud.AddTextClicked += OpenAddTextDialog;
        _hud.SetAddTextButtonVisible(true);
        _hud.AddSelectClicked += RecordSelectBeat;
        _hud.SetAddSelectButtonVisible(true);
        _hud.AddDemoStartClicked += RecordDemoStartBeat;
        _hud.SetAddDemoStartButtonVisible(true);

        _recordSession = new SessionState();
        _controller = new GameController(
            _recordState,
            _recordSession,
            _panel.Map,
            _hud,
            seed: _panel.CurrentSeed,
            aiChooser: null,
            aiPacer: new SynchronousAiPacer(),
            loadedReplay: previous.Replay,
            recordingMode: true,
            autoSelectFirstTerritory: false);

        _savedDragMode = _panel.Map.DragMode;
        _panel.Map.DragMode = HexDragMode.Pan;
        _panel.Map.Init(_recordState);

        // BeginReplay rewinds _recordState to the snapshot, replays
        // every recorded beat under the synchronous pacer (drains
        // inline thanks to the SynchronousAiPacer trampoline), and
        // calls EndReplay so _replayMode is false on exit. The beat
        // list is preserved; further user inputs append.
        _controller.BeginReplay();

        if (_controller.InitialReplaySnapshot != null)
        {
            _capture.Begin(
                _controller.InitialReplaySnapshot,
                _controller.InitialReplayTurnNumber,
                _controller.InitialReplayCurrentPlayerIndex);
            _capture.SetBeats(new List<ReplayBeat>(_controller.ReplayBeats));
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
        Log.Debug(Log.LogCategory.Tutorial, $"[RecordPane] StopRecording (running={_running}, beats={_controller?.ReplayBeats.Count ?? -1})");
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

        TearDownAddTextButton();

        _panel.Map.DragMode = _savedDragMode;
        _panel.Map.Init(_panel.BuildLiveState());

        _controller = null;
        _recordState = null;
        _recordSession = null;
        _running = false;
    }

    /// <summary>
    /// The Add Select button: capture the currently selected territory
    /// as a <see cref="ReplaySelectTerritoryBeat"/> (anchored at its
    /// capital), so hands-free demo playback re-selects it. No-op with
    /// a log when nothing is selected.
    /// </summary>
    private void RecordSelectBeat()
    {
        if (_controller == null || _recordSession == null) return;
        Territory? selected = _recordSession.SelectedTerritory;
        if (selected is not { HasCapital: true })
        {
            Log.Debug(Log.LogCategory.Tutorial,
                "[RecordPane] Add Select pressed with no selected territory — ignored");
            return;
        }

        HexCoord anchor = selected.Capital!.Value;
        _controller.RecordTutorialOnlyBeat(new ReplaySelectTerritoryBeat { Anchor = anchor });
        _capture.SetBeats(new List<ReplayBeat>(_controller.ReplayBeats));
        Log.Debug(Log.LogCategory.Tutorial,
            $"[RecordPane] Authored SelectTerritory beat: anchor={anchor}");
    }

    /// <summary>
    /// The Demo Start button: mark this point in the script as where
    /// instruction playback begins — everything recorded before it
    /// fast-forwards instantly on every loop. Playback honors the FIRST
    /// marker; pressing it again later is recorded but has no effect.
    /// </summary>
    private void RecordDemoStartBeat()
    {
        if (_controller == null) return;
        _controller.RecordTutorialOnlyBeat(new ReplayDemoStartBeat());
        _capture.SetBeats(new List<ReplayBeat>(_controller.ReplayBeats));
        Log.Debug(Log.LogCategory.Tutorial,
            $"[RecordPane] Authored DemoStart beat at index {_controller.ReplayBeats.Count - 1}");
    }

    /// <summary>
    /// Tear down any open Add Text dialog. The button itself lives on
    /// the HUD now (HudIcon.AddText, revealed via
    /// <see cref="HudView.SetAddTextButtonVisible"/>) and is freed with
    /// the HUD in <see cref="StopRecording"/>; only the modal dialog,
    /// which parents to RecordPane, needs explicit cleanup here.
    /// </summary>
    private void TearDownAddTextButton()
    {
        if (_addTextDialog != null)
        {
            RemoveChild(_addTextDialog);
            _addTextDialog.QueueFree();
            _addTextDialog = null;
        }
    }

    private void OpenAddTextDialog()
    {
        if (_addTextDialog != null) return;
        if (_controller == null) return;

        // Match the ModalChrome dialog family (Save Game / Settings / slot
        // picker). Those are CanvasLayer modals, and that's exactly why their
        // dim backdrop + centered slate panel anchor correctly: a CanvasLayer's
        // Control children resolve their anchors against the viewport. RecordPane
        // is a plain Control whose subtree doesn't resolve full-rect/center
        // anchors here (the panel collapsed offscreen), so we host the dialog on
        // its own throwaway CanvasLayer and tear the whole layer down on close.
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        var layer = new CanvasLayer { Layer = 100 };

        ColorRect backdrop = ModalChrome.BuildBackdrop(viewport);
        layer.AddChild(backdrop);

        // Sibling of the backdrop on the layer (as in SaveNameModal), centered
        // via its own anchors — no CenterContainer needed on a CanvasLayer.
        PanelContainer panel = ModalChrome.BuildCenteredPanel();
        layer.AddChild(panel);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(460, 0),
        };
        vbox.AddThemeConstantOverride("separation", 18);
        panel.AddChild(vbox);

        vbox.AddChild(ModalChrome.BuildSerifTitle("Add Narration"));

        var label = new Label
        {
            Text = Strings.Get(StringKeys.BuilderNarrationPrompt),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", UiPalette.InkSoft);
        vbox.AddChild(label);

        var lineEdit = new LineEdit
        {
            PlaceholderText = Strings.Get(StringKeys.BuilderNarrationPlaceholder),
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        lineEdit.AddThemeFontSizeOverride("font_size", 22);
        vbox.AddChild(lineEdit);

        var buttonRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        buttonRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(buttonRow);

        var cancelButton = new Button
        {
            Text = Strings.Get(StringKeys.ButtonCancel),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        cancelButton.AddThemeFontSizeOverride("font_size", 24);
        AudioBus.AttachClick(cancelButton);
        buttonRow.AddChild(cancelButton);

        var insertButton = new Button
        {
            Text = Strings.Get(StringKeys.BuilderInsert),
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        insertButton.AddThemeFontSizeOverride("font_size", 24);
        AudioBus.AttachClick(insertButton);
        buttonRow.AddChild(insertButton);

        void Close()
        {
            if (_addTextDialog != null)
            {
                RemoveChild(_addTextDialog);
                _addTextDialog.QueueFree();
                _addTextDialog = null;
            }
        }

        void Submit()
        {
            string text = lineEdit.Text;
            if (!string.IsNullOrEmpty(text) && _controller != null)
            {
                _controller.RecordTutorialOnlyBeat(
                    new ReplayDisplayTextBeat { Text = text });
                _capture.SetBeats(new List<ReplayBeat>(_controller.ReplayBeats));
                Log.Debug(Log.LogCategory.Tutorial, $"[RecordPane] Authored DisplayText beat: \"{text}\"");
            }
            Close();
        }

        insertButton.Pressed += Submit;
        cancelButton.Pressed += Close;
        lineEdit.TextSubmitted += _ => Submit();
        // Backdrop click closes (modal-family contract). The panel eats its
        // own clicks (PanelContainer MouseFilter Stop), so only empty
        // backdrop area reaches here.
        backdrop.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                backdrop.AcceptEvent();
                Close();
            }
        };

        AddChild(layer);
        _addTextDialog = layer;
        lineEdit.GrabFocus();
    }
}
