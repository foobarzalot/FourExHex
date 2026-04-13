using System.Collections.Generic;
using Godot;

/// <summary>
/// Scene root. Constructs the model (<see cref="GameState"/> +
/// <see cref="SessionState"/>), the two views (<see cref="HexMapView"/>
/// and <see cref="HudView"/>), hands them to a
/// <see cref="GameController"/>, and kicks off the game. Holds no game
/// logic itself — all orchestration lives in the controller.
/// </summary>
public partial class Main : Node2D
{
    private GameController _controller = null!;

    public override void _Ready()
    {
        // Build the map view shell first so we can read its layout
        // dimensions (Cols/Rows/HexSize) before the grid is constructed.
        var map = new HexMapView();

        // --- Model construction ------------------------------------------
        List<Player> players = BuildPlayers();
        var turnState = new TurnState(players);
        var treasury = new Treasury();

        HexGrid grid = BuildInitialGrid(map.Cols, map.Rows, players);
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(grid);
        IReadOnlyList<Territory> territories = CapitalReconciler.Reconcile(
            raw, new List<Territory>(), grid);

        var state = new GameState(grid, territories, players, turnState, treasury);
        var session = new SessionState();

        // --- Views into the scene tree -----------------------------------
        map.Init(state);
        AddChild(map);

        Vector2 viewport = GetViewportRect().Size;
        float x = (viewport.X - map.PixelSize.X) * 0.5f;
        float y = HudView.HudHeight + (viewport.Y - HudView.HudHeight - map.PixelSize.Y) * 0.5f;
        map.Position = new Vector2(x, y);

        var hud = new HudView();
        AddChild(hud);

        // Scene-level actions: Play Again reloads the whole scene
        // (keeping the current GameSettings.PlayerIsAi config), and
        // Main Menu swaps back to the menu scene so the player can
        // reassign roles. Both are simpler than resetting GameState
        // in place and guarantee we don't leak stale references
        // across games.
        hud.NewGameClicked += () => GetTree().ReloadCurrentScene();
        hud.MainMenuClicked += () => GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");

        // --- Controller takes over from here -----------------------------
        // Use a Godot-backed pacer so AI turns visibly play out over
        // time instead of resolving in one synchronous burst.
        var pacer = new GodotAiPacer(GetTree());
        _controller = new GameController(state, session, map, hud, aiPacer: pacer);
        _controller.StartGame();
    }

    private static HexGrid BuildInitialGrid(int cols, int rows, IReadOnlyList<Player> players)
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();

        var grid = new HexGrid();
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                HexCoord coord = HexCoord.FromOffset(col, row);
                Color color = players[rng.RandiRange(0, players.Count - 1)].Color;
                grid.Add(new HexTile(coord, color));
            }
        }

        // Seed a handful of initial trees — roughly 5% of tiles — so
        // the board has visible forest at game start (Slay does this).
        // CapitalPlacer already skips tree-occupied tiles, so capital
        // assignment on the downstream pipeline handles this correctly.
        int treeTarget = (cols * rows) / 20;
        var allCoords = new List<HexCoord>();
        foreach (HexTile tile in grid.Tiles) allCoords.Add(tile.Coord);
        for (int i = 0; i < treeTarget; i++)
        {
            int idx = rng.RandiRange(0, allCoords.Count - 1);
            HexCoord pick = allCoords[idx];
            allCoords.RemoveAt(idx);
            HexTile? t = grid.Get(pick);
            if (t != null && t.Occupant == null)
            {
                t.Occupant = new Tree();
            }
        }
        return grid;
    }

    private static List<Player> BuildPlayers()
    {
        var players = new List<Player>();
        // Player roles come from GameSettings, which the main menu
        // writes before switching to this scene. StartGame
        // auto-drives any AI players at the front of the turn order
        // so human input is only needed on a human's turn.
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            bool isAi = i < GameSettings.PlayerIsAi.Length && GameSettings.PlayerIsAi[i];
            players.Add(new Player(name, new Color(hex), isAi: isAi));
        }
        return players;
    }
}
