using System;
using System.Collections.Generic;

namespace FourExHex.Tests;

/// <summary>
/// The pieces a controller test drives: the model state, the UI-scoped
/// session, the recording mock views, the controller under test, and the
/// player roster. Returned by <see cref="TestHelpers.BuildControllerGame"/>
/// so fixtures don't re-derive them.
/// </summary>
public sealed record ControllerHarness(
    GameState State,
    SessionState Session,
    MockHexMapView Map,
    MockHudView Hud,
    GameController Controller,
    IReadOnlyList<Player> Players);

/// <summary>
/// Shared helpers for building grids, territories, and fully-reconciled
/// game state in tests. Keeps test fixtures DRY so grid-shape changes in
/// one place instead of five.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Build a grid consisting of exactly the given coords, each tile
    /// owned by <paramref name="owner"/>. Used for hand-crafted topologies
    /// (e.g., testing a specific adjacency or a split/merge).
    /// </summary>
    public static HexGrid BuildSpotGrid(PlayerId owner, params HexCoord[] coords)
    {
        var grid = new HexGrid();
        foreach (HexCoord c in coords)
        {
            grid.Add(new HexTile(c, owner));
        }
        return grid;
    }

    /// <summary>
    /// Build a rectangular odd-r offset grid of size <paramref name="cols"/>
    /// x <paramref name="rows"/>, every tile owned by <paramref name="owner"/>.
    /// </summary>
    public static HexGrid BuildRectGrid(int cols, int rows, PlayerId owner)
    {
        var grid = new HexGrid();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                grid.Add(new HexTile(HexCoord.FromOffset(c, r), owner));
            }
        }
        return grid;
    }

    /// <summary>
    /// Run <see cref="TerritoryFinder.FindAll"/> followed by
    /// <see cref="CapitalReconciler.Reconcile"/> against no prior
    /// territories, producing a territory list with capitals placed.
    /// Mutates <paramref name="grid"/> by adding Capital occupants.
    /// </summary>
    public static IReadOnlyList<Territory> BuildTerritoriesFromGrid(HexGrid grid)
    {
        var raw = TerritoryFinder.FindAll(grid);
        return CapitalReconciler.Reconcile(raw, new List<Territory>(), grid);
    }

    /// <summary>
    /// Build a fully-started <see cref="GameController"/> over a cols×rows
    /// rectangular grid and return the whole harness. The single
    /// parameterized fixture behind <c>GameControllerTests.TestGame</c> and
    /// <c>ReplayPlaybackTests.Fixture</c>, so the ~12-line
    /// grid→territories→state→controller→StartGame block lives in one place.
    ///
    /// Defaults reproduce the canonical fixture: a 5×2 grid, a 2-tile Red
    /// (players[0]) territory at (0,1)/(1,1), Blue (players[1]) everywhere
    /// else, and the End-Turn claim-victory prompt suppressed for both
    /// colors (Blue otherwise owns 80% of the board and interrupts every
    /// turn-cycling test). Pass knobs to vary one thing without rebuilding
    /// the rest.
    /// </summary>
    /// <param name="players">Roster; defaults to Red(0)/Blue(1), both Human.</param>
    /// <param name="defaultOwner">Owner of every tile not in
    /// <paramref name="ownerOverrides"/>; defaults to players[1].</param>
    /// <param name="ownerOverrides">(col,row,owner) tiles to re-own after the
    /// fill; defaults to (0,1) and (1,1) → players[0].</param>
    /// <param name="suppressClaimVictory">When true (default) sets every
    /// player's claim-victory threshold to 90 so End Turn doesn't prompt.</param>
    /// <param name="startGame">When false, constructs the controller but skips
    /// StartGame — for tests asserting what the constructor told the views.</param>
    /// <param name="beforeTerritories">Runs on the grid after owner overrides
    /// but before territory/capital reconciliation; place occupants here when
    /// their presence must influence capital placement.</param>
    /// <param name="beforeStart">Runs on the state after construction but
    /// before StartGame; place occupants / seed treasury for the first upkeep.</param>
    public static ControllerHarness BuildControllerGame(
        IReadOnlyList<Player>? players = null,
        int cols = 5,
        int rows = 2,
        PlayerId? defaultOwner = null,
        IEnumerable<(int col, int row, PlayerId owner)>? ownerOverrides = null,
        int currentPlayerIndex = 0,
        int turnNumber = 1,
        int? seed = null,
        Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null,
        Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, Random, AiAction?>? automateChooser = null,
        Func<bool>? automateIsInstantMode = null,
        IAiPacer? aiPacer = null,
        int maxTurnNumber = int.MaxValue,
        bool autoSelect = false,
        bool suppressClaimVictory = true,
        bool recordingMode = false,
        bool previewMode = false,
        bool startGame = true,
        Func<bool>? aiSilentMode = null,
        Func<bool>? replayIsInstantMode = null,
        IReadOnlySet<HexCoord>? waterCoords = null,
        Action<HexGrid>? beforeTerritories = null,
        Action<GameState>? beforeStart = null,
        bool useOriginMergeCapital = false)
    {
        players ??= new List<Player>
        {
            new Player("Red", PlayerId.FromIndex(0)),
            new Player("Blue", PlayerId.FromIndex(1)),
        };
        PlayerId fillOwner = defaultOwner ?? players[players.Count - 1].Id;
        ownerOverrides ??= new[]
        {
            (0, 1, players[0].Id),
            (1, 1, players[0].Id),
        };

        HexGrid grid = BuildRectGrid(cols, rows, fillOwner);
        foreach ((int col, int row, PlayerId owner) in ownerOverrides)
        {
            grid.Get(HexCoord.FromOffset(col, row))!.Owner = owner;
        }

        // Occupants that must be on the board while capitals are reconciled
        // (capital placement avoids occupied tiles) go in here — matches the
        // hand-rolled fixtures that set occupants before BuildTerritoriesFromGrid.
        beforeTerritories?.Invoke(grid);

        IReadOnlyList<Territory> territories = BuildTerritoriesFromGrid(grid);
        var state = new GameState(
            grid, territories, players,
            new TurnState(players, currentPlayerIndex, turnNumber),
            new Treasury(), waterCoords,
            useOriginMergeCapital: useOriginMergeCapital);
        var session = new SessionState();
        if (suppressClaimVictory)
        {
            foreach (Player p in players)
            {
                session.ClaimVictoryPromptedHighestThreshold[p.Id] = 90;
            }
        }

        // Pre-StartGame hook: place occupants / seed treasury exactly as the
        // hand-rolled fixtures did (occupants present when StartGame runs its
        // first upkeep). Runs after territory/capital reconciliation, so only
        // place occupants on non-capital tiles if the test asserts a capital.
        beforeStart?.Invoke(state);

        var map = new MockHexMapView();
        var hud = new MockHudView();
        var controller = new GameController(
            state, session, map, hud,
            seed: seed,
            aiChooser: aiChooser,
            automateChooser: automateChooser,
            automateIsInstantMode: automateIsInstantMode,
            aiPacer: aiPacer,
            maxTurnNumber: maxTurnNumber,
            recordingMode: recordingMode,
            previewMode: previewMode,
            aiSilentMode: aiSilentMode,
            replayIsInstantMode: replayIsInstantMode,
            autoSelectFirstTerritory: autoSelect);
        // startGame:false is for the construct-only tests that assert what the
        // constructor told the views, isolating it from StartGame's effects.
        if (startGame) controller.StartGame();
        return new ControllerHarness(state, session, map, hud, controller, players);
    }
}
