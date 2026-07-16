// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Autoload that makes the Godot-free <see cref="Log"/> usable from any
/// scene. It runs before the first scene loads (Godot instantiates
/// autoloads ahead of <c>main_scene</c>), wiring the sink to
/// <c>GD.Print</c> and applying the <c>FOUREXHEX_LOG</c> spec exactly
/// once. Runs before any scene so <see cref="Log"/> works in menu/editor
/// scenes (MainMenuScene, MapEditorScene, TutorialBuilderScene) and their
/// modals too.
///
/// Registered as an autoload in project.godot under the name
/// "LogBootstrap".
/// </summary>
public partial class LogBootstrap : Node
{
    public override void _EnterTree()
    {
        // Route the Godot-free Log to GD.Print (stdout in headless). On iOS
        // GD.Print → printf to stdout, which the device's unified log doesn't
        // capture, so additionally mirror through libc's syslog (see IosLog)
        // — that's what idevicesyslog / Console.app / xcrun devicectl read.
        if (OS.HasFeature("ios"))
        {
            Log.Sink ??= msg => { GD.Print(msg); IosLog.Write(msg); };
        }
        else
        {
            Log.Sink ??= GD.Print;
        }
        // Runtime config, e.g. FOUREXHEX_LOG="Ai:Debug,Turn:Info,*:Warn".
        // Best-effort parse; unknown tokens ignored; empty = silent
        // (every category defaults to Off).
        Log.Configure(OS.GetEnvironment("FOUREXHEX_LOG"));

        bool isMobile = OS.HasFeature("mobile");

        // User-facing string table: the Godot-free Strings store can't read
        // res:// itself, so load assets/strings/en.json here and push the
        // text in — the same configure-at-boot pattern as Log above, and it
        // must happen before any scene builds instructional / tooltip text.
        // isMobile selects the Tap/Click verb variants (data-driven). res://
        // resolves into the PCK in exported builds and the project tree in
        // the editor (the file must be in export_presets.cfg include_filter
        // — plain .json isn't a Godot-imported resource). A missing or
        // malformed file leaves the store empty, so every lookup renders
        // its key and the game still runs.
        using FileAccess? stringsFile =
            FileAccess.Open("res://assets/strings/en.json", FileAccess.ModeFlags.Read);
        if (stringsFile == null)
        {
            Log.Error(Log.LogCategory.Hud,
                $"[strings] failed to open res://assets/strings/en.json: {FileAccess.GetOpenError()}");
        }
        else
        {
            try
            {
                Strings.Configure(stringsFile.GetAsText(), isMobile);
                Log.Info(Log.LogCategory.Hud, $"[strings] loaded {Strings.Count} keys");
            }
            catch (System.Text.Json.JsonException e)
            {
                Log.Error(Log.LogCategory.Hud,
                    $"[strings] failed to parse en.json: {e.Message}");
            }
        }

        // Mobile (Android/iOS) can't set the FOUREXHEX_LOG env var, so turn
        // every category fully verbose for logcat. Desktop keeps env-var-driven
        // gating (above). Trace/Debug/Info are compiled out of release builds
        // regardless of level, so in a release build this only surfaces
        // Warn/Error in the field — intentional for device diagnostics.
        if (isMobile)
        {
            foreach (Log.LogCategory cat in System.Enum.GetValues<Log.LogCategory>())
            {
                Log.SetLevel(cat, Log.LogLevel.Debug);
            }
        }
    }
}
