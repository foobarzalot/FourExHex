using Godot;

/// <summary>
/// Autoload that makes the Godot-free <see cref="Log"/> usable from any
/// scene. It runs before the first scene loads (Godot instantiates
/// autoloads ahead of <c>main_scene</c>), wiring the sink to
/// <c>GD.Print</c> and applying the <c>FOUREXHEX_LOG</c> spec exactly
/// once.
///
/// Previously this lived only in <see cref="Main"/>, so <see cref="Log"/>
/// calls from menu/editor scenes (MainMenuScene, MapEditorScene,
/// TutorialBuilderScene) and their modals were silently dropped — the
/// sink was null and no category was configured until the in-game scene
/// ran. Hoisting it here closes that gap: instrumentation works
/// everywhere, including the main menu's Settings/Credits modals.
///
/// Registered as an autoload in project.godot under the name
/// "LogBootstrap".
/// </summary>
public partial class LogBootstrap : Node
{
    public override void _EnterTree()
    {
        // Route the Godot-free Log to GD.Print (stdout in headless).
        Log.Sink ??= GD.Print;
        // Runtime config, e.g. FOUREXHEX_LOG="Ai:Debug,Turn:Info,*:Warn".
        // Best-effort parse; unknown tokens ignored; empty = silent
        // (every category defaults to Off).
        Log.Configure(OS.GetEnvironment("FOUREXHEX_LOG"));
    }
}
