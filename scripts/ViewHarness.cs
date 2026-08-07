// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// Drives the real scenes through the <see cref="ViewMatrix"/> sweep and audits
/// the resolved layout of every screen — the view-layer integration harness.
/// Inert unless <c>FOUREXHEX_VIEW_MATRIX</c> is set; <c>tools/view_matrix.sh</c>
/// is the entry point.
///
/// An autoload rather than a per-scene attach: <c>ChangeSceneToFile</c> frees
/// everything in the old scene, so a per-scene driver would have to park all of
/// its cross-scene state in statics anyway and then re-enter after each new
/// scene's _Ready — which is exactly what a surviving autoload's _Process gives
/// for free.
///
/// Cells are walked <b>inside</b> a live scene (scene outer, cell middle, screen
/// inner) for two reasons: cell-outer would reload every scene per cell, and
/// <c>ScreenLayout.IsCompact</c>'s dead band is path-dependent — its hold
/// states only exist when the same size is reached from two different prior
/// states, which a cold start per cell cannot produce.
///
/// Never runs headless: Godot's headless DisplayServer is a stub (window pinned
/// to 64x64, --resolution ignored, dpi 96, empty safe rect), so every layout
/// branch would be measured against fiction.
/// </summary>
public partial class ViewHarness : Node
{
    private const string EnableEnvVar = "FOUREXHEX_VIEW_MATRIX";

    /// <summary>Frames of a quiet layout epoch before a screen is measured.</summary>
    private const int StableFrames = 3;

    /// <summary>Cap on settle frames. Reaching it is a non-convergence finding,
    /// not a reason to measure anyway.</summary>
    private const int MaxSettleFrames = 30;

    /// <summary>Whole-run watchdog: no state transition for this long and the
    /// harness quits rather than hanging a CI job.</summary>
    private const double WatchdogSeconds = 90.0;

    private enum Step
    {
        AwaitScene,
        ApplyCell,
        SettleCell,
        ShowScreen,
        SettleScreen,
        NextScreen,
        NextCell,
        NextScene,
        Summarize,
    }

    private readonly List<string> _scenes = new()
    {
        "res://scenes/main_menu.tscn",
        "res://scenes/main.tscn",
        "res://scenes/map_editor.tscn",
        "res://scenes/tutorial_builder.tscn",
    };

    private IReadOnlyList<ViewMatrixCell> _cells = new List<ViewMatrixCell>();
    private readonly LayoutSettlePolicy _settle = new(StableFrames, MaxSettleFrames);
    private readonly List<string> _skipNotes = new();

    private Step _step = Step.AwaitScene;
    private int _sceneIndex;
    private int _cellIndex;
    private int _screenIndex;
    private double _sinceTransition;

    private bool _awaitingSwap;
    private int _visits;
    private int _skippedScreens;
    private int _skippedCells;
    private int _hardViolations;

    private IReadOnlyList<string> _screenIds = new List<string>();

    public override void _Ready()
    {
        if (OS.GetEnvironment(EnableEnvVar).Length == 0)
        {
            SetProcess(false);
            return;
        }

        // The pause menu sets GetTree().Paused, which would stop this node's
        // _Process — the sweep would hang on the first paused screen and the
        // watchdog, being itself in _Process, could never fire to report it.
        ProcessMode = ProcessModeEnum.Always;

        // The 6AI diagnostic path returns from MainMenuScene._Ready before any
        // panel is built and swaps HeadlessHexMapView/HeadlessHudView in for the
        // real views — a sweep under it would audit an empty tree and report a
        // clean run that proves nothing.
        if (OS.GetEnvironment("FOUREXHEX_6AI").Length > 0
            || OS.GetEnvironment("FOUREXHEX_6AI_QUICK").Length > 0)
        {
            Log.Error(Log.LogCategory.Layout,
                "[view-matrix] refusing to run with FOUREXHEX_6AI set — that path skips panel "
                + "construction and uses headless views, so the sweep would audit nothing");
            CallDeferred(nameof(QuitWith), 2);
            return;
        }

        // Pinned after LogBootstrap has run Log.Configure (autoload order), so a
        // stray FOUREXHEX_LOG can't silence the run's own verdict.
        Log.SetLevel(Log.LogCategory.Layout, Log.LogLevel.Debug);

        _cells = ViewMatrix.WithReverseSweep(
            ViewMatrix.Parse(OS.GetEnvironment("FOUREXHEX_VIEW_CELLS")));

        string sceneSpec = OS.GetEnvironment("FOUREXHEX_VIEW_SCENES");
        if (sceneSpec.Length > 0)
        {
            _scenes.RemoveAll(path => !sceneSpec.Contains(
                System.IO.Path.GetFileNameWithoutExtension(path),
                System.StringComparison.OrdinalIgnoreCase));
        }

        Log.Info(Log.LogCategory.Layout,
            $"[view-matrix] START scenes={_scenes.Count} cells={_cells.Count} "
            + $"fakeMobile={PlatformFlags.FakeMobileActive}");
    }

    public override void _Process(double delta)
    {
        _sinceTransition += delta;
        if (_sinceTransition > WatchdogSeconds)
        {
            Log.Error(Log.LogCategory.Layout,
                $"[view-matrix] watchdog: no progress for {WatchdogSeconds}s at step={_step} "
                + $"scene={CurrentScenePath} cell={CurrentCellName} screen={CurrentScreenId}");
            QuitWith(2);
            return;
        }

        switch (_step)
        {
            case Step.AwaitScene: AwaitScene(); break;
            case Step.ApplyCell: ApplyCell(); break;
            case Step.SettleCell: SettleCell(); break;
            case Step.ShowScreen: ShowScreen(); break;
            case Step.SettleScreen: SettleScreen(); break;
            case Step.NextScreen: NextScreen(); break;
            case Step.NextCell: NextCell(); break;
            case Step.NextScene: NextScene(); break;
            case Step.Summarize: Summarize(); break;
        }
    }

    private string CurrentScenePath =>
        _sceneIndex < _scenes.Count ? _scenes[_sceneIndex] : "(done)";

    private string CurrentCellName =>
        _cellIndex < _cells.Count ? _cells[_cellIndex].Name : "(none)";

    private string CurrentScreenId =>
        _screenIndex < _screenIds.Count ? _screenIds[_screenIndex] : "(none)";

    private IHarnessNavigable? Scene => GetTree()?.CurrentScene as IHarnessNavigable;

    private void Transition(Step next)
    {
        _step = next;
        _sinceTransition = 0.0;
    }

    private void AwaitScene()
    {
        Node? current = GetTree()?.CurrentScene;
        if (current == null) return;

        // Godot always boots into project.godot's main scene, which is not
        // necessarily the first scene in the sweep — with a scene filter it
        // usually isn't. Navigating here rather than assuming keeps the audit
        // tags honest; without it the harness audits the menu while labelling
        // the findings as another scene's.
        if (current.SceneFilePath != _scenes[_sceneIndex])
        {
            if (_awaitingSwap) return;      // swap already requested; wait for it
            _awaitingSwap = true;
            PrepareSceneEntry(_scenes[_sceneIndex]);
            GetTree().ChangeSceneToFile(_scenes[_sceneIndex]);
            return;
        }
        _awaitingSwap = false;

        if (Scene is not IHarnessNavigable navigable) return;   // scene still building

        _screenIds = navigable.HarnessScreenIds;
        _cellIndex = 0;
        Log.Info(Log.LogCategory.Layout,
            $"[view-matrix] scene {CurrentScenePath} screens={_screenIds.Count}");
        Transition(Step.ApplyCell);
    }

    private void ApplyCell()
    {
        ViewMatrixCell cell = _cells[_cellIndex];

        SafeArea.SetOverrideForHarness(
            cell.Insets == LogicalSafeInsets.Zero ? null : cell.Insets);
        GetWindow().ContentScaleFactor = cell.UiScale;
        DisplayServer.WindowSetSize(new Vector2I(cell.PhysicalWidth, cell.PhysicalHeight));

        _settle.Reset();
        Transition(Step.SettleCell);
    }

    private void SettleCell()
    {
        SettleState state = _settle.Observe(LayoutAudit.Epoch);
        if (state == SettleState.Waiting) return;

        ViewMatrixCell cell = _cells[_cellIndex];
        Vector2I actual = DisplayServer.WindowGetSize();

        // A window manager may clamp: macOS by screen bounds and Dock, xvfb by
        // its virtual screen. Auditing the wrong geometry while reporting the
        // requested one is the failure mode this check exists to prevent, so a
        // clamped cell is SKIPPED, never silently PASSED.
        if (actual.X != cell.PhysicalWidth || actual.Y != cell.PhysicalHeight)
        {
            Rect2I usable = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
            string note = $"cell '{cell.Name}' UNACHIEVABLE: requested "
                + $"{cell.PhysicalWidth}x{cell.PhysicalHeight} physical, got {actual.X}x{actual.Y} "
                + $"(screen usable {usable.Size.X}x{usable.Size.Y}) — {_screenIds.Count} screens not audited";
            Log.Warn(Log.LogCategory.Layout, $"[view-matrix] {note}");
            _skipNotes.Add(note);
            _skippedCells++;
            Transition(Step.NextCell);
            return;
        }

        if (state == SettleState.Stalled)
        {
            Log.Warn(Log.LogCategory.Layout,
                $"[view-matrix] cell '{cell.Name}' layout never settled after {MaxSettleFrames} "
                + "frames — the epoch is still churning");
        }

        _screenIndex = 0;
        Transition(Step.ShowScreen);
    }

    private void ShowScreen()
    {
        if (Scene is not IHarnessNavigable navigable)
        {
            Transition(Step.NextCell);
            return;
        }

        string id = _screenIds[_screenIndex];
        if (!navigable.ShowHarnessScreen(id))
        {
            Log.Debug(Log.LogCategory.Layout,
                $"[view-matrix] {CurrentCellName}/{id} SKIPPED (scene cannot satisfy it now)");
            _skippedScreens++;
            Transition(Step.NextScreen);
            return;
        }

        _settle.Reset();
        Transition(Step.SettleScreen);
    }

    private void SettleScreen()
    {
        if (_settle.Observe(LayoutAudit.Epoch) == SettleState.Waiting) return;

        ViewMatrixCell cell = _cells[_cellIndex];
        string tag = $"{cell.Name}/{_screenIds[_screenIndex]}";

        int violations = LayoutAudit.Sweep(GetTree().Root, tag);
        _visits++;

        _hardViolations += violations;

        Scene?.ResetHarnessScreen();
        Transition(Step.NextScreen);
    }

    private void NextScreen()
    {
        _screenIndex++;
        Transition(_screenIndex < _screenIds.Count ? Step.ShowScreen : Step.NextCell);
    }

    private void NextCell()
    {
        _cellIndex++;
        Transition(_cellIndex < _cells.Count ? Step.ApplyCell : Step.NextScene);
    }

    private void NextScene()
    {
        _sceneIndex++;
        if (_sceneIndex >= _scenes.Count)
        {
            Transition(Step.Summarize);
            return;
        }

        // Release the forced insets so the next scene builds against its own
        // cell's geometry, not the previous cell's. AwaitScene owns the actual
        // navigation, so there is one code path that decides what to load.
        SafeArea.SetOverrideForHarness(null);
        _awaitingSwap = false;
        Transition(Step.AwaitScene);
    }

    /// <summary>Set whatever a scene needs before it builds. The cross-scene
    /// handoff statics are the sanctioned mechanism, so the harness uses them
    /// rather than reaching into the scene after the fact.</summary>
    private static void PrepareSceneEntry(string scenePath)
    {
        if (!scenePath.EndsWith("main.tscn")) return;

        // A small, seeded, all-Computer-but-one game: fast to build, identical
        // run to run, and it leaves a real HudView over a real board. NOT the
        // FOUREXHEX_6AI path — that one swaps in headless views.
        GameSettings.CampaignLevel = null;
        GameSettings.MasterSeed = 42;
        for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
        {
            GameSettings.PlayerKinds[i] = i == 0 ? PlayerKind.Human : PlayerKind.Computer;
        }
    }

    private void Summarize()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[view-matrix] SUMMARY scenes={_scenes.Count} cells={_cells.Count} "
            + $"visits={_visits} skipped-screens={_skippedScreens} skipped-cells={_skippedCells} "
            + $"violations={_hardViolations}");
        foreach (string note in _skipNotes) sb.AppendLine($"[view-matrix]   skip: {note}");
        sb.Append("[view-matrix] DONE");
        Log.Info(Log.LogCategory.Layout, sb.ToString());

        QuitWith(_hardViolations == 0 ? 0 : 1);
    }

    private void QuitWith(int code)
    {
        SetProcess(false);
        GetTree().Quit(code);
    }
}
