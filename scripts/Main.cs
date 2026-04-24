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
        // Diagnostic launch: setting the FOUREXHEX_6AI environment
        // variable before starting Godot forces all six slots to
        // Heuristic AI, enables verbose AI logging to stdout, runs the
        // game synchronously (no pacing delays), caps turns at 500
        // so stasis runs terminate, and auto-quits on game over.
        // Intended for Claude to run headless and read the logs.
        bool diagnosticMode = OS.GetEnvironment("FOUREXHEX_6AI").Length > 0;
        if (diagnosticMode)
        {
            for (int i = 0; i < GameSettings.PlayerKinds.Length; i++)
            {
                GameSettings.PlayerKinds[i] = AiKind.Heuristic;
            }
            AiLog.Enabled = true;
            GD.Print("=== FOUREXHEX_6AI diagnostic mode ===");
        }

        // --- Model construction ------------------------------------------
        // In normal mode we read grid dimensions off a HexMapView
        // shell (its [Export] Cols/Rows); in diagnostic mode we
        // hardcode the defaults so we can skip constructing the
        // view altogether — real HexMapView / HudView do a lot of
        // layout and rendering work we don't want in a headless
        // tight loop.
        int cols, rows;
        HexMapView? visibleMap = null;
        if (diagnosticMode)
        {
            cols = 18;
            rows = 13;
        }
        else
        {
            visibleMap = new HexMapView();
            cols = visibleMap.Cols;
            rows = visibleMap.Rows;
        }

        List<Player> players = BuildPlayers();
        var turnState = new TurnState(players);
        var treasury = new Treasury();

        HexGrid grid = BuildInitialGrid(cols, rows, players);
        IReadOnlyList<Territory> raw = TerritoryFinder.FindAll(grid);
        IReadOnlyList<Territory> territories = CapitalReconciler.Reconcile(
            raw, new List<Territory>(), grid);

        var state = new GameState(grid, territories, players, turnState, treasury);
        var session = new SessionState();

        // --- Views --------------------------------------------------------
        IHexMapView map;
        IHudView hud;
        if (diagnosticMode)
        {
            map = new HeadlessHexMapView(state);
            hud = new HeadlessHudView();
        }
        else
        {
            visibleMap!.Init(state);
            AddChild(visibleMap);

            Vector2 viewport = GetViewportRect().Size;
            float x = (viewport.X - visibleMap.PixelSize.X) * 0.5f;
            float y = HudView.HudHeight + (viewport.Y - HudView.HudHeight - visibleMap.PixelSize.Y) * 0.5f;
            visibleMap.Position = new Vector2(x, y);

            var visibleHud = new HudView();
            AddChild(visibleHud);

            // Scene-level actions: Play Again reloads the whole
            // scene (keeping the current GameSettings.PlayerKinds
            // config), and Main Menu swaps back to the menu scene
            // so the player can reassign roles.
            visibleHud.NewGameClicked += () => GetTree().ReloadCurrentScene();
            visibleHud.MainMenuClicked += () => GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");

            map = visibleMap;
            hud = visibleHud;
        }

        // --- Controller takes over from here -----------------------------
        // Normal launch: Godot-backed pacer so AI turns visibly play
        // out over time. Diagnostic launch: synchronous pacer so the
        // whole game runs inline and we can read the full log, plus
        // a hard turn cap so stasis runs terminate instead of
        // looping forever.
        IAiPacer pacer = diagnosticMode
            ? new SynchronousAiPacer()
            : new GodotAiPacer(GetTree());
        int maxTurns = diagnosticMode ? 500 : int.MaxValue;
        _controller = new GameController(
            state, session, map, hud,
            aiPacer: pacer,
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: maxTurns);

        if (diagnosticMode)
        {
            // Defer the quit so Godot finishes setting up the
            // scene tree before we tear it down. Calling Quit()
            // synchronously from inside _Ready() / StartGame()
            // races with scene init and can crash on exit.
            _controller.GameEnded += () => GetTree().CallDeferred("quit");
        }

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
            AiKind kind = i < GameSettings.PlayerKinds.Length
                ? GameSettings.PlayerKinds[i]
                : AiKind.Random;
            players.Add(new Player(name, new Color(hex), kind));
        }
        return players;
    }
}
