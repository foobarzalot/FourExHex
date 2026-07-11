using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The killer save/load test. Runs a controller A from turn 1, snapshots
/// mid-game, deserializes that snapshot into a fresh controller B, and
/// continues both. They must produce identical states. Proves that
/// (a) the save format captures every gameplay-relevant bit, and
/// (b) the per-turn RNG reseed makes future AI choices independent of
/// how many random numbers prior turns consumed (so the save records
/// only the master seed).
/// </summary>
public class SaveLoadEquivalenceTests
{
    private class GameWithObserver
    {
        public GameController Controller { get; }
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHudView Hud { get; }
        public IReadOnlyList<Player> Players { get; }

        public GameWithObserver(
            GameController c, GameState s, SessionState ss,
            MockHudView h, IReadOnlyList<Player> p)
        {
            Controller = c; State = s; Session = ss; Hud = h; Players = p;
        }
    }

    /// <summary>
    /// 14x3 grid. Red owns the leftmost 5 columns (15 tiles); Blue owns
    /// the right 9 columns (27 tiles). To keep the test stable across
    /// many turns, the chooser caps Blue's actions to 1 per turn —
    /// otherwise default ComputerAi can run dozens of captures in T1
    /// (134 starting gold + adjacent Red tiles) and end the game.
    /// </summary>
    private static GameWithObserver BuildHumanVsAi(int seed, GameState? loadedState = null)
    {
        IReadOnlyList<Player> players;
        GameState state;

        if (loadedState != null)
        {
            state = loadedState;
            players = loadedState.Players;
        }
        else
        {
            var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
            var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
            players = new List<Player> { red, blue };
            HexGrid grid = TestHelpers.BuildRectGrid(14, 3, blue.Id);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 5; col++)
                {
                    grid.Get(HexCoord.FromOffset(col, row))!.Owner = red.Id;
                }
            }
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        }

        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();

        // Wrapping chooser: cap to 1 ComputerAi action per (turn, player)
        // so the game spans many turns without an early elimination.
        // The cap is keyed to (turn, player) so captures across save/
        // load boundaries don't accidentally let the AI exceed it.
        int currentTurnKey = -1;
        int actionsThisTurn = 0;
        AiAction? CappedChooser(GameState s, PlayerId c, HashSet<HexCoord> visited, HashSet<HexCoord> ru, Random rng)
        {
            int key = s.Turns.TurnNumber * 100 + s.Turns.CurrentPlayerIndex;
            if (key != currentTurnKey)
            {
                currentTurnKey = key;
                actionsThisTurn = 0;
            }
            if (actionsThisTurn >= 1) return null;
            AiAction? action = ComputerAi.ChooseNextAction(s, c, visited, ru, rng);
            if (action != null) actionsThisTurn++;
            return action;
        }

        var controller = new GameController(
            state, session, map, hud, seed: seed, aiChooser: CappedChooser);
        return new GameWithObserver(controller, state, session, hud, players);
    }

    [Fact]
    public void DeserializedStateImmediatelyEqualsPreSaveState()
    {
        // Sanity check: serialize then deserialize must produce a
        // state that's structurally identical to the pre-save state,
        // BEFORE any further gameplay runs. If this fails, the save
        // format is incomplete.
        const int seed = 4242;
        GameWithObserver g = BuildHumanVsAi(seed);
        g.Controller.StartGame();
        for (int i = 0; i < 2; i++) g.Hud.ClickEndTurn();

        string saved = SaveSerializer.Serialize(
            g.State, g.Controller.MasterSeed, g.Players, "x", maxTurnNumber: int.MaxValue);
        LoadedSave loaded = SaveSerializer.Deserialize(saved);

        AssertGameStatesEqual(g.State, loaded.State);
    }

    [Fact]
    public void SerializeMidGame_DeserializeAndContinue_FinalStateMatchesUninterrupted()
    {
        // Plan:
        // 1. Run "uninterrupted" (control A) for 2+1 = 3 human turns
        //    and snapshot final state.
        // 2. Run "interrupted" (control B): play 2 turns, serialize,
        //    deserialize into a fresh controller, play 1 more turn,
        //    snapshot final state.
        // Final states must be identical. Any missing field (RNG seed,
        // gold, occupant level, moved-flag) shows up as a divergence.
        const int seed = 4242;
        const int turnsBeforeSave = 2;
        const int turnsAfterSave = 1;

        // --- Control A: uninterrupted -------------------------------
        GameWithObserver a = BuildHumanVsAi(seed);
        a.Controller.StartGame();
        for (int i = 0; i < turnsBeforeSave + turnsAfterSave; i++)
        {
            if (a.Session.IsGameOver) break;
            a.Hud.ClickEndTurn();
        }

        // --- Control B: interrupted with serialize/deserialize ------
        GameWithObserver b1 = BuildHumanVsAi(seed);
        b1.Controller.StartGame();
        for (int i = 0; i < turnsBeforeSave; i++)
        {
            if (b1.Session.IsGameOver) break;
            b1.Hud.ClickEndTurn();
        }
        Assert.False(b1.Session.IsGameOver,
            "Test fixture flaw: game ended before save point. " +
            "Use a wider grid or fewer turns.");

        string saved = SaveSerializer.Serialize(
            b1.State, b1.Controller.MasterSeed, b1.Players, "mid", maxTurnNumber: int.MaxValue);

        LoadedSave loaded = SaveSerializer.Deserialize(saved);
        GameWithObserver b2 = BuildHumanVsAi(seed: loaded.MasterSeed, loadedState: loaded.State);
        b2.Controller.Resume();
        for (int i = 0; i < turnsAfterSave; i++)
        {
            if (b2.Session.IsGameOver) break;
            b2.Hud.ClickEndTurn();
        }

        // --- Final states must match -------------------------------
        AssertGameStatesEqual(a.State, b2.State);
    }

    private static void AssertGameStatesEqual(GameState x, GameState y)
    {
        Assert.Equal(x.Turns.TurnNumber, y.Turns.TurnNumber);
        Assert.Equal(x.Turns.CurrentPlayerIndex, y.Turns.CurrentPlayerIndex);

        // Tiles
        var xTiles = new Dictionary<HexCoord, HexTile>();
        foreach (HexTile t in x.Grid.Tiles) xTiles[t.Coord] = t;
        int yCount = 0;
        foreach (HexTile yt in y.Grid.Tiles)
        {
            yCount++;
            Assert.True(xTiles.ContainsKey(yt.Coord), $"y has extra coord {yt.Coord}");
            HexTile xt = xTiles[yt.Coord];
            Assert.Equal(xt.Owner, yt.Owner);
            AssertOccupantsEqual(xt.Coord, xt.Occupant, yt.Occupant);
        }
        Assert.Equal(xTiles.Count, yCount);

        // Treasury (per capital)
        foreach (Territory t in x.Territories)
        {
            if (!t.HasCapital) continue;
            HexCoord cap = t.Capital!.Value;
            Assert.Equal(x.Treasury.GetGold(cap), y.Treasury.GetGold(cap));
        }

        // Territories — same partition (count + per-coord owner + capital)
        Assert.Equal(x.Territories.Count, y.Territories.Count);
        Dictionary<HexCoord, Territory> xIndex = x.Territories.BuildTileIndex();
        Dictionary<HexCoord, Territory> yIndex = y.Territories.BuildTileIndex();
        foreach (KeyValuePair<HexCoord, Territory> kvp in xIndex)
        {
            Assert.True(yIndex.ContainsKey(kvp.Key));
            Assert.Equal(kvp.Value.Owner, yIndex[kvp.Key].Owner);
            Assert.Equal(kvp.Value.Capital, yIndex[kvp.Key].Capital);
        }
    }

    private static void AssertOccupantsEqual(HexCoord coord, HexOccupant? a, HexOccupant? b)
    {
        if (a == null && b == null) return;
        Assert.True(a != null && b != null,
            $"Occupant mismatch at {coord}: a={a?.GetType().Name ?? "null"} b={b?.GetType().Name ?? "null"}");
        Assert.Equal(a!.GetType(), b!.GetType());
        if (a is Unit ua && b is Unit ub)
        {
            Assert.Equal(ua.Owner, ub.Owner);
            Assert.Equal(ua.Level, ub.Level);
            Assert.Equal(ua.HasMovedThisTurn, ub.HasMovedThisTurn);
        }
    }
}
