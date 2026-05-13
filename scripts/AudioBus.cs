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
    private AudioStreamPlayer _unitCombinedPlayer = null!;
    private AudioStreamPlayer _unitDestroyedPlayer = null!;
    private AudioStreamPlayer _towerDestroyedPlayer = null!;
    private AudioStreamPlayer _treeClearedPlayer = null!;
    private AudioStreamPlayer _capitalDestroyedPlayer = null!;
    private AudioStreamPlayer _bankruptcyPlayer = null!;
    private AudioStreamPlayer _gameWonPlayer = null!;
    private AudioStreamPlayer _rallyPlayer = null!;
    private AudioStreamPlayer _playerDefeatedPlayer = null!;

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

        _unitCombinedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/unit_combine.wav"),
            // Combine fires on a routine action, so the chime sits
            // below the place/tower hits — it should feel like a small
            // reward, not an event.
            VolumeDb = -8f,
        };
        AddChild(_unitCombinedPlayer);

        _unitDestroyedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/unit_destroyed.wav"),
            VolumeDb = -6f,
        };
        AddChild(_unitDestroyedPlayer);

        _towerDestroyedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/tower_destroyed.wav"),
            // Stone sources tend to be hot — keep parity with the
            // tower-place gain so the destroy/place pair sit at a
            // matched perceived loudness.
            VolumeDb = -8f,
        };
        AddChild(_towerDestroyedPlayer);

        _treeClearedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/tree_cleared.wav"),
            // Source clip's transient is hot — sit well under the
            // unit-place thud so the chop doesn't dominate.
            VolumeDb = -18f,
        };
        AddChild(_treeClearedPlayer);

        _capitalDestroyedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/capital_destroyed.wav"),
            // Capital "destruction" is really a relocation — the
            // territory shrinks and CapitalReconciler picks a new
            // capital tile. Sound sits at routine-event level, not
            // milestone level.
            VolumeDb = -10f,
        };
        AddChild(_capitalDestroyedPlayer);

        _bankruptcyPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/bankruptcy.wav"),
            // Somber, not loud — the player should hear it but it
            // shouldn't startle. Bell tolls have long natural sustain
            // so a moderate gain reads as substantial without dominating.
            VolumeDb = -10f,
        };
        AddChild(_bankruptcyPlayer);

        _gameWonPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/game_won.wav"),
            // Terminal moment — the player just won, the screen is
            // showing the game-over panel and they're absorbing the
            // result. Sits a touch above bankruptcy so the win lands
            // bigger than the loss bell, but not so hot that it startles.
            VolumeDb = -6f,
        };
        AddChild(_gameWonPlayer);

        _rallyPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/rally.wav"),
            // Whoosh is naturally airy and a bit broad — sit it well
            // under the unit-place thud so a rally doesn't dominate the
            // mix when it bundles several units' worth of motion.
            // (-6 dB from the previous -8 dB = roughly half amplitude.)
            VolumeDb = -14f,
        };
        AddChild(_rallyPlayer);

        _playerDefeatedPlayer = new AudioStreamPlayer
        {
            Stream = GD.Load<AudioStream>("res://assets/audio/player_defeated.wav"),
            // Heavy gong — naturally hot transient and long sustain.
            // Sits at game-won level so the elimination of an enemy
            // reads as a major moment, but not louder than the win
            // fanfare.
            VolumeDb = -10f,
        };
        AddChild(_playerDefeatedPlayer);
    }

    /// <summary>
    /// Stop()+Play() retriggers from sample 0 so rapid clicks don't
    /// smear into each other's tails.
    /// </summary>
    public void PlayClick()
    {
        if (!UserSettings.SfxEnabled) return;
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
        if (!UserSettings.SfxEnabled) return;
        _unitPlacedPlayer.Stop();
        _unitPlacedPlayer.Play();
    }

    /// <summary>
    /// Stone-on-stone clack played when a tower is built. Heavier and
    /// grittier than the unit-place thud so the two are audibly distinct.
    /// </summary>
    public void PlayTowerPlaced()
    {
        if (!UserSettings.SfxEnabled) return;
        _towerPlacedPlayer.Stop();
        _towerPlacedPlayer.Play();
    }

    /// <summary>
    /// Bright "level-up" chime played when two same-color units merge
    /// into a higher-level one. Replaces the place-thud for that action.
    /// </summary>
    public void PlayUnitCombined()
    {
        if (!UserSettings.SfxEnabled) return;
        _unitCombinedPlayer.Stop();
        _unitCombinedPlayer.Play();
    }

    /// <summary>Soft squelch for crushing an enemy unit.</summary>
    public void PlayUnitDestroyed()
    {
        if (!UserSettings.SfxEnabled) return;
        _unitDestroyedPlayer.Stop();
        _unitDestroyedPlayer.Play();
    }

    /// <summary>Bursting stone for capturing/destroying an enemy tower.</summary>
    public void PlayTowerDestroyed()
    {
        if (!UserSettings.SfxEnabled) return;
        _towerDestroyedPlayer.Stop();
        _towerDestroyedPlayer.Play();
    }

    /// <summary>Single axe chop for clearing a tree or burying a grave.</summary>
    public void PlayTreeCleared()
    {
        if (!UserSettings.SfxEnabled) return;
        _treeClearedPlayer.Stop();
        _treeClearedPlayer.Play();
    }

    /// <summary>
    /// Heavy collapse + bell for capturing/destroying an enemy capital.
    /// The heaviest cue in the library; reserved for the rarest event.
    /// </summary>
    public void PlayCapitalDestroyed()
    {
        if (!UserSettings.SfxEnabled) return;
        _capitalDestroyedPlayer.Stop();
        _capitalDestroyedPlayer.Play();
    }

    /// <summary>
    /// Single low somber bell toll for upkeep bankruptcy at turn-start.
    /// </summary>
    public void PlayBankruptcy()
    {
        if (!UserSettings.SfxEnabled) return;
        _bankruptcyPlayer.Stop();
        _bankruptcyPlayer.Play();
    }

    /// <summary>
    /// Joyful peal of bells when a human wins the game. The longest
    /// SFX in the library — game-over is one of the few places a
    /// 1.5s sound is appropriate.
    /// </summary>
    public void PlayGameWon()
    {
        if (!UserSettings.SfxEnabled) return;
        _gameWonPlayer.Stop();
        _gameWonPlayer.Play();
    }

    /// <summary>
    /// Short whoosh played once per long-press rally that actually
    /// shifted at least one unit. One sound per gesture, not per unit
    /// — many small movements should read as one swept rally.
    /// </summary>
    public void PlayRally()
    {
        if (!UserSettings.SfxEnabled) return;
        _rallyPlayer.Stop();
        _rallyPlayer.Play();
    }

    /// <summary>
    /// Single deep gong played when a capture eliminates a player —
    /// their last capital just fell. Marks the strategic finality of
    /// kicking someone out of the game.
    /// </summary>
    public void PlayPlayerDefeated()
    {
        if (!UserSettings.SfxEnabled) return;
        _playerDefeatedPlayer.Stop();
        _playerDefeatedPlayer.Play();
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
