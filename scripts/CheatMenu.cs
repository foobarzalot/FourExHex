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
            // EscMenu hides itself before invoking the callback, so
            // Close is just a logged no-op.
            new(Strings.Get(StringKeys.CheatClose), () => Log.Debug(Log.LogCategory.Cheat, "CheatMenu: closed (Close button).")),
        });
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
