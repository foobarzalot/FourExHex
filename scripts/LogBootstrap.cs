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

        // Platform-aware interaction verb: "Tap" on mobile, "Click" on desktop.
        // Godot-free static (see InteractionVerb), so it must be pushed the
        // platform flag here at startup — before any scene's instructional /
        // tooltip text is built — the same way Log is configured above.
        InteractionVerb.Configure(isMobile);
        Log.Info(Log.LogCategory.Display,
            $"InteractionVerb: mobile={isMobile} -> \"{InteractionVerb.Capitalized}\"");

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
