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
    private AudioStreamPlayer _unitPlacedPlayer = null!;
    private AudioStreamPlayer _towerPlacedPlayer = null!;

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

        _unitPlacedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/place.wav"),
            VolumeDb = -4f,
        };
        AddChild(_unitPlacedPlayer);

        _towerPlacedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/tower_place.wav"),
            // Stone material is naturally bright and the source clip
            // tends to come back hot, so we sit a touch under the
            // unit-place gain.
            VolumeDb = -8f,
        };
        AddChild(_towerPlacedPlayer);
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
    /// Soft thud played when a unit is moved or bought-and-placed in a
    /// way that consumes its move action. Same Stop()+Play() retrigger
    /// pattern as PlayClick.
    /// </summary>
    public void PlayUnitPlaced()
    {
        _unitPlacedPlayer.Stop();
        _unitPlacedPlayer.Play();
    }

    /// <summary>
    /// Stone-on-stone clack played when a tower is built. Heavier and
    /// grittier than the unit-place thud so the two are audibly distinct.
    /// </summary>
    public void PlayTowerPlaced()
    {
        _towerPlacedPlayer.Stop();
        _towerPlacedPlayer.Play();
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
