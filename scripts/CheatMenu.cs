// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
#if DEBUG
using Godot;

/// <summary>
/// Debug-only cheat menu: a button modal summoned over any
/// screen with backquote (desktop) or a 3-finger tap (touch), reusing
/// <see cref="EscMenu"/> for the modal chrome. Scene roots opt in with
/// <see cref="Attach"/> inside their own <c>#if DEBUG</c> block, so
/// Release builds contain no listener, no menu, and no call sites —
/// the whole file compiles away (no autoload registration to leak).
///
/// Input runs in <see cref="_Input"/> (not <c>_UnhandledInput</c>) so
/// the summon gesture works even when a focused Control would otherwise
/// swallow the event — a deliberate dev-tool tradeoff: typing a
/// backquote into a text field will open the menu instead.
/// </summary>
public sealed partial class CheatMenu : Node
{
    /// <summary>Create and add the overlay under <paramref name="sceneRoot"/>.</summary>
    public static void Attach(Node sceneRoot)
    {
        // Compile-time gate above, runtime gate here: an exported build
        // that somehow defines DEBUG without being a debug template
        // still gets no cheat menu.
        if (!OS.IsDebugBuild()) return;
        sceneRoot.AddChild(new CheatMenu());
        Log.Debug(Log.LogCategory.Cheat, $"CheatMenu: attached to {sceneRoot.Name}.");
    }

    private readonly MultiTouchTapDetector _tapDetector = new();
    private EscMenu _menu = null!;

    public override void _Ready()
    {
        // Always — the gesture must summon the menu even while the host
        // scene is paused (gameplay pause coordinator), same reasoning
        // as EscMenu's own ProcessMode.
        ProcessMode = ProcessModeEnum.Always;
        _menu = new EscMenu();
        AddChild(_menu);
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Quoteleft }:
                Toggle("backquote");
                GetViewport().SetInputAsHandled();
                break;
            case InputEventScreenTouch touch when touch.Pressed:
                if (_tapDetector.Press(touch.Index)) Toggle("3-finger tap");
                break;
            case InputEventScreenTouch touch:
                _tapDetector.Release(touch.Index);
                break;
        }
    }

    private void Toggle(string gesture)
    {
        if (_menu.IsOpen)
        {
            Log.Debug(Log.LogCategory.Cheat, $"CheatMenu: closed ({gesture}).");
            _menu.Hide();
            return;
        }
        Log.Debug(Log.LogCategory.Cheat, $"CheatMenu: opened ({gesture}).");
        _menu.Show(Strings.Get(StringKeys.CheatTitle), new EscMenu.Option[]
        {
            new(Strings.Get(StringKeys.CheatTutorialBuilder), OpenTutorialBuilder),
            // Label names the ACTION: "Toggle Recording Mode On" while the
            // mode is off, and vice versa (issue #156 clean-capture mode —
            // HudView/HexMapView hide their promo-noisy chrome while active).
            new(Strings.Get(RecordingMode.Active
                    ? StringKeys.CheatRecordingOff
                    : StringKeys.CheatRecordingOn),
                RecordingMode.Toggle),
            new(Strings.Get(StringKeys.CheatDeterminism), RunDeterminismCheck),
            // EscMenu hides itself before invoking the callback, so
            // Close is just a logged no-op.
            new(Strings.Get(StringKeys.CheatClose), () => Log.Debug(Log.LogCategory.Cheat, "CheatMenu: closed (Close button).")),
        });
    }

    /// <summary>
    /// Issue #59's in-app cross-platform proof: run the seeded all-AI
    /// quick game inline (same fingerprint as a FOUREXHEX_6AI_QUICK
    /// headless run) and show the digest triple. Works from any scene
    /// the menu attaches to; on iOS/Android — where env vars can't be
    /// set — this is the only way to capture the fingerprint. Blocks
    /// the UI thread for a few seconds, fine for a dev tool.
    /// </summary>
    private void RunDeterminismCheck()
    {
        Log.Debug(Log.LogCategory.Cheat, "CheatMenu: Determinism Check pressed.");
        DeterminismProbeResult r = DeterminismProbe.Run(
            new HeadlessHexMapView(), new HeadlessHudView());
        // One grep-able line for stdout / logcat (tag "godot") / os_log.
        // GD.Print, not Log.Debug: must survive any build that can
        // reach the cheat menu regardless of log-category config.
        GD.Print(
            $"[determinism-probe] seed={r.Seed} mapgen={r.MapGenRngStreamHash:X16} " +
            $"rng={r.RngStreamDigest:X16} final={r.FinalChecksum} " +
            $"turns={r.Turns} winner={r.WinnerIndex}");

        var layer = new CanvasLayer { Layer = 100 };
        var (backdrop, panel, _, body) = ModalChrome.BuildErrorOverlay(
            GetViewport().GetVisibleRect().Size,
            panelW: Mathf.Min(GetViewport().GetVisibleRect().Size.X * 0.9f, 560f),
            panelH: 340f,
            Strings.Get(StringKeys.CheatDeterminismTitle),
            onOk: () => layer.QueueFree());
        string winner = r.WinnerIndex < 0 ? "(none)" : $"player {r.WinnerIndex}";
        body.Text =
            $"seed {r.Seed} · {r.Turns} turns · winner {winner}\n\n" +
            $"mapgen  {r.MapGenRngStreamHash:X16}\n" +
            $"rng     {r.RngStreamDigest:X16}\n\n" +
            $"final checksum\n" +
            $"{r.FinalChecksum[..32]}\n{r.FinalChecksum[32..]}";
        layer.AddChild(backdrop);
        layer.AddChild(panel);
        backdrop.Visible = true;
        panel.Visible = true;
        AddChild(layer);
    }

    private void OpenTutorialBuilder()
    {
        // No in-progress-game guard, by design: this is a dev tool and
        // the scene change is the requested action wherever you are.
        Log.Debug(Log.LogCategory.Cheat, "CheatMenu: Tutorial Builder pressed.");
        GetTree().ChangeSceneToFile("res://scenes/tutorial_builder.tscn");
    }
}
#endif
