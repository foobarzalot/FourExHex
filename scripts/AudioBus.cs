using Godot;

/// <summary>
/// Autoload-registered audio singleton. Owns shared one-shot SFX players
/// that survive scene changes — needed because Godot frees the current
/// scene (and any AudioStreamPlayer inside it) on ChangeSceneToFile,
/// which would cut off short clicks triggered by buttons that navigate
/// away (Map Editor, Start Game, load-slot, etc.).
///
/// Registered as an autoload in project.godot under the name "AudioBus".
/// Godot's autoload mechanism instantiates this node before the first
/// scene loads, so <see cref="Instance"/> is always non-null by the time
/// any scene's _Ready runs.
/// </summary>
public partial class AudioBus : Node
{
    public static AudioBus Instance { get; private set; } = null!;

    private AudioStreamPlayer _clickPlayer = null!;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        _clickPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/click.wav"),
            VolumeDb = -6f,
        };
        AddChild(_clickPlayer);
    }

    /// <summary>
    /// Stop()+Play() retriggers from sample 0 so rapid clicks don't
    /// smear into each other's tails.
    /// </summary>
    public void PlayClick()
    {
        _clickPlayer.Stop();
        _clickPlayer.Play();
    }

    /// <summary>
    /// Subscribe a button (or any BaseButton — Button, CheckBox, the
    /// auto-buttons inside ConfirmationDialog, etc.) to play the
    /// shared click on every press. Safe to call on buttons in scenes
    /// that get freed by ChangeSceneToFile — the autoload outlives them.
    /// </summary>
    public static void AttachClick(BaseButton button)
    {
        button.Pressed += Instance.PlayClick;
    }

    /// <summary>
    /// Overload for the editor's palette swatches. They're plain
    /// <see cref="Control"/>s with their own custom Pressed event, so
    /// they don't share BaseButton's signal. The discard lambda swallows
    /// the event's argument.
    /// </summary>
    public static void AttachClick(HexPaletteButton button)
    {
        button.Pressed += _ => Instance.PlayClick();
    }
}
