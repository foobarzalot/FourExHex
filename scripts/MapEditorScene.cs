using System.Collections.Generic;
using Godot;

/// <summary>
/// Map editor scene root. Boots into a water-only stub map and lets the
/// user generate a fresh procedural map by entering a seed in the HUD and
/// pressing Generate. Generate calls <see cref="HexMapView.ReloadState"/>
/// on the live view so the user's zoom and pan are preserved across
/// regenerations — only the map data changes.
/// </summary>
public partial class MapEditorScene : Node2D
{
    private MapEditorHudView _hud = null!;
    private HexMapView _map = null!;
    private List<Player> _players = null!;

    public override void _Ready()
    {
        _players = BuildPlayers();

        _map = new HexMapView();
        _map.Init(BuildWaterOnlyState(_map.Cols, _map.Rows));
        AddChild(_map);

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
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(mapGen.Grid);
        IReadOnlyList<Territory> territories = CapitalReconciler.Reconcile(
            raw, new List<Territory>(), mapGen.Grid);

        var turnState = new TurnState(_players);
        var treasury = new Treasury();
        var state = new GameState(
            mapGen.Grid, territories, _players, turnState, treasury, mapGen.WaterCoords);

        _map.ReloadState(state);
        // ReloadState rebuilds tile fills, water, borders — but trees +
        // capitals come from RefreshOccupantVisuals, which is normally
        // driven by GameController. Pass null currentPlayerColor so no
        // CTA pulsing fires (no "current player" exists in the editor).
        _map.RefreshOccupantVisuals(currentPlayerColor: null, treasury);
    }

    private GameState BuildWaterOnlyState(int cols, int rows)
    {
        var grid = new HexGrid();
        var territories = new List<Territory>();
        var turnState = new TurnState(_players);
        var treasury = new Treasury();
        var water = new HashSet<HexCoord>();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                water.Add(HexCoord.FromOffset(col, row));
            }
        }
        return new GameState(grid, territories, _players, turnState, treasury, water);
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
