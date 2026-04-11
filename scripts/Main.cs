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
    private static readonly (string Name, string Hex)[] PlayerConfig =
    {
        ("Red",    "e53935"),
        ("Blue",   "1e88e5"),
        ("Green",  "43a047"),
        ("Yellow", "fdd835"),
        ("Purple", "8e24aa"),
        ("Orange", "fb8c00"),
    };

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

        // --- Controller takes over from here -----------------------------
        _controller = new GameController(state, session, map, hud);
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
        foreach ((string name, string hex) in PlayerConfig)
        {
            players.Add(new Player(name, new Color(hex)));
        }
        return players;
    }
}
