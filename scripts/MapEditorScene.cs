using System.Collections.Generic;
using Godot;

/// <summary>
/// Map editor scene root. Boots into a water-only stub map and lets the
/// user generate a fresh procedural map (HUD seed entry + Generate) or
/// paint individual tiles by clicking them with a palette swatch
/// selected. Owns a mutable <see cref="HexGrid"/> + water set as the
/// editor's draft — every push rebuilds a fresh <see cref="GameState"/>
/// from those, calls <see cref="HexMapView.ReloadState"/> on the live
/// view (preserving zoom/pan), then reapplies occupant visuals.
/// </summary>
public partial class MapEditorScene : Node2D
{
    private MapEditorHudView _hud = null!;
    private HexMapView _map = null!;
    private List<Player> _players = null!;
    private HexGrid _grid = new HexGrid();
    private HashSet<HexCoord> _water = new HashSet<HexCoord>();
    private IReadOnlyList<Territory> _territories = new List<Territory>();

    public override void _Ready()
    {
        _players = BuildPlayers();

        _map = new HexMapView();
        InitWaterOnly(_map.Cols, _map.Rows);
        _map.Init(BuildState());
        AddChild(_map);
        _map.CoordClicked += OnCoordClicked;

        _hud = new MapEditorHudView();
        AddChild(_hud);
        _hud.ExitClicked += ReturnToMainMenu;
        _hud.GenerateRequested += OnGenerateRequested;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;
        if (keyEvent.Keycode != Key.Escape) return;
        GetViewport()?.SetInputAsHandled();
        ReturnToMainMenu();
    }

    private void OnGenerateRequested(int seed)
    {
        MapGenResult mapGen = MapGenerator.BuildInitialGrid(_map.Cols, _map.Rows, _players, seed);
        _grid = mapGen.Grid;
        _water = new HashSet<HexCoord>(mapGen.WaterCoords);
        // Reset the territory thread on regen — the previous list points
        // at coords from the old grid, so reconcile from scratch.
        _territories = new List<Territory>();
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(_grid);
        _territories = CapitalReconciler.Reconcile(raw, _territories, _grid);
        // Animate seeded trees in on a fresh map.
        PushState(animateNewOccupants: true);
    }

    private void OnCoordClicked(HexCoord coord)
    {
        int idx = _hud.SelectedPaletteIndex;
        bool isWaterPalette = idx >= GameSettings.PlayerConfig.Length;

        if (isWaterPalette)
        {
            _territories = MapEditPaint.PaintWater(
                _grid, _water, _territories, _map.Cols, _map.Rows, coord);
        }
        else
        {
            Color color = new Color(GameSettings.PlayerConfig[idx].Hex);
            _territories = MapEditPaint.PaintLand(
                _grid, _water, _territories, _map.Cols, _map.Rows, coord, color);
        }
        // Per-paint rebuild: existing trees + graves should appear instantly,
        // not re-grow on every click.
        PushState(animateNewOccupants: false);
    }

    private void InitWaterOnly(int cols, int rows)
    {
        _grid = new HexGrid();
        _water = new HashSet<HexCoord>();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                _water.Add(HexCoord.FromOffset(col, row));
            }
        }
        _territories = new List<Territory>();
    }

    private GameState BuildState() =>
        new GameState(
            _grid, _territories, _players, new TurnState(_players), new Treasury(), _water);

    private void PushState(bool animateNewOccupants)
    {
        GameState state = BuildState();
        _map.ReloadState(state, animateNewOccupants);
        // ReloadState rebuilds tile fills, water, borders — but trees +
        // capitals come from RefreshOccupantVisuals, which is normally
        // driven by GameController. Pass null currentPlayerColor so no
        // CTA pulsing fires (no "current player" exists in the editor).
        _map.RefreshOccupantVisuals(currentPlayerColor: null, state.Treasury);
    }

    private static List<Player> BuildPlayers()
    {
        var players = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            players.Add(new Player(name, new Color(hex), AiKind.Human));
        }
        return players;
    }

    private void ReturnToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
