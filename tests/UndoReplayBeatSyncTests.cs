using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the three-stack sync invariant between the session undo stack
/// and the recorder's parallel beat bookkeeping: at every quiescent
/// point (after any input handler returns),
/// <c>SessionState.Undo.UndoCount == GameController.UndoBeatBatchDepth</c>
/// and <c>SessionState.Undo.RedoCount == GameController.RedoBeatBatchDepth</c>.
/// A divergence means a subsequent undo pops a phantom beat count and
/// silently trims the wrong tail of the replay log — these tests turn
/// that silent corruption into a red test. Replay round-trip integrity
/// after undo/redo churn is covered in <see cref="ReplayPlaybackTests"/>.
/// </summary>
public class UndoReplayBeatSyncTests
{
    /// <summary>
    /// 5x2 fixture identical to <c>ReplayRecordingTests.Fixture</c>:
    /// Red owns (0,1)/(1,1), Blue owns the rest, both human so every
    /// click goes through <c>TrackHandler</c>. Claim-victory prompts are
    /// pre-dismissed so End Turn is never hijacked by the overlay.
    /// </summary>
    private class Fixture
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public Fixture(int maxTurnNumber = 50)
        {
            Red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
            Blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Human);
            var players = new List<Player> { Red, Blue };

            HexGrid grid = TestHelpers.BuildRectGrid(5, 2, Blue.Id);
            grid.Get(HexCoord.FromOffset(0, 1))!.Owner = Red.Id;
            grid.Get(HexCoord.FromOffset(1, 1))!.Owner = Red.Id;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Session.ClaimVictoryPromptedHighestThreshold[Red.Id] = 90;
            Session.ClaimVictoryPromptedHighestThreshold[Blue.Id] = 90;
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(
                State, Session, Map, Hud,
                seed: 1,
                aiChooser: null,
                aiPacer: new SynchronousAiPacer(),
                maxTurnNumber: maxTurnNumber);
            Controller.StartGame();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;

        public HexCoord RedCapital =>
            State.Territories.First(t => t.Owner == Red.Id).Capital!.Value;

        public void AssertInSync(string context)
        {
            Assert.True(
                Session.Undo.UndoCount == Controller.UndoBeatBatchDepth,
                $"Undo-side desync after {context}: session UndoCount=" +
                $"{Session.Undo.UndoCount}, recorder UndoBeatBatchDepth=" +
                $"{Controller.UndoBeatBatchDepth}");
            Assert.True(
                Session.Undo.RedoCount == Controller.RedoBeatBatchDepth,
                $"Redo-side desync after {context}: session RedoCount=" +
                $"{Session.Undo.RedoCount}, recorder RedoBeatBatchDepth=" +
                $"{Controller.RedoBeatBatchDepth}");
        }
    }

    [Fact]
    public void FreshGame_AllStacksEmpty_AndInSync()
    {
        var f = new Fixture();
        Assert.Equal(0, f.Session.Undo.UndoCount);
        Assert.Equal(0, f.Controller.UndoBeatBatchDepth);
        Assert.Equal(0, f.Controller.RedoBeatBatchDepth);
        f.AssertInSync("StartGame");
    }

    [Fact]
    public void ScriptedActions_KeepStacksInSyncAfterEachStep()
    {
        var f = new Fixture();
        f.State.Treasury.SetGold(f.RedCapital, 50);

        f.Map.SimulateClick(f.Tile(0, 1));   // select Red territory
        f.AssertInSync("select");
        f.Hud.ClickBuyRecruit();              // enter buy mode
        f.AssertInSync("enter buy mode");
        f.Map.SimulateClick(f.Tile(1, 1));   // place recruit
        f.AssertInSync("place recruit");
        f.Map.SimulateClick(f.Tile(1, 1));   // pick up the recruit
        f.AssertInSync("pick up unit");
        f.Map.SimulateClick(f.Tile(2, 1));   // move (captures Blue tile)
        f.AssertInSync("capture move");
    }

    [Fact]
    public void UndoLastThenRedoLast_StaysInSync()
    {
        var f = new Fixture();
        f.State.Treasury.SetGold(f.RedCapital, 50);
        f.Map.SimulateClick(f.Tile(0, 1));
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.Tile(1, 1));

        f.Hud.ClickUndoLast();
        f.AssertInSync("undo last");
        f.Hud.ClickUndoLast();
        f.AssertInSync("undo last x2");
        f.Hud.ClickRedoLast();
        f.AssertInSync("redo last");
        f.Hud.ClickRedoLast();
        f.AssertInSync("redo last x2");
    }

    [Fact]
    public void UndoTurn_MultiPop_StaysInSync()
    {
        var f = new Fixture();
        f.State.Treasury.SetGold(f.RedCapital, 50);
        f.Map.SimulateClick(f.Tile(0, 1));
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.Tile(1, 1));
        Assert.True(f.Session.Undo.UndoCount >= 2, "need multi-entry stack");

        f.Hud.ClickUndoTurn();
        f.AssertInSync("undo turn");
        Assert.Equal(0, f.Session.Undo.UndoCount);

        f.Hud.ClickRedoAll();
        f.AssertInSync("redo all");
        Assert.Equal(0, f.Session.Undo.RedoCount);
    }

    [Fact]
    public void NewActionAfterUndo_InvalidatesRedoOnBothSides()
    {
        var f = new Fixture();
        f.State.Treasury.SetGold(f.RedCapital, 50);
        f.Map.SimulateClick(f.Tile(0, 1));
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.Tile(1, 1));
        f.Hud.ClickUndoLast();
        Assert.True(f.Session.Undo.RedoCount > 0, "need redo history");

        // Fresh action: re-place via buy mode (still active after undo or
        // re-enter). Both redo sides must drop to zero together.
        f.Map.SimulateClick(f.Tile(0, 1));
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.Tile(1, 1));
        Assert.Equal(0, f.Session.Undo.RedoCount);
        f.AssertInSync("fresh action after undo");
    }

    [Fact]
    public void EndTurn_ClearsBothSidesTogether()
    {
        var f = new Fixture();
        f.State.Treasury.SetGold(f.RedCapital, 50);
        f.Map.SimulateClick(f.Tile(0, 1));
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.Tile(1, 1));
        Assert.True(f.Session.Undo.UndoCount > 0);

        f.Hud.ClickEndTurn();
        Assert.Equal(0, f.Session.Undo.UndoCount);
        Assert.Equal(0, f.Session.Undo.RedoCount);
        f.AssertInSync("end turn");
    }

    /// <summary>
    /// Deterministic stress: interleave several hundred random inputs
    /// (clicks, buys, tower builds, undo/redo in all four flavors,
    /// cancels, occasional end-turns) and assert the invariant after
    /// every single one. Illegal/no-op inputs are part of the point —
    /// they must leave both sides untouched together.
    /// </summary>
    [Fact]
    public void StressInterleavedRandomOps_StaysInSyncAfterEveryOp()
    {
        var f = new Fixture(maxTurnNumber: 200);
        var rng = new Random(42);

        // Keep both capitals funded so buys/towers regularly succeed.
        foreach (Territory t in f.State.Territories.Where(t => t.HasCapital))
        {
            f.State.Treasury.SetGold(t.Capital!.Value, 100);
        }

        UnitLevel[] levels =
        {
            UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain, UnitLevel.Commander,
        };

        for (int i = 0; i < 400; i++)
        {
            if (f.Session.IsGameOver) break;
            int op = rng.Next(12);
            string context;
            switch (op)
            {
                case 0:
                case 1:
                case 2:
                case 3: // random tile click (legal and illegal alike)
                    int col = rng.Next(5);
                    int row = rng.Next(2);
                    context = $"op {i}: click ({col},{row})";
                    f.Map.SimulateClick(f.Tile(col, row));
                    break;
                case 4:
                    context = $"op {i}: buy recruit";
                    f.Hud.ClickBuyRecruit();
                    break;
                case 5:
                    UnitLevel level = levels[rng.Next(levels.Length)];
                    context = $"op {i}: buy {level}";
                    f.Hud.ClickBuyUnit(level);
                    break;
                case 6:
                    context = $"op {i}: build tower";
                    f.Hud.ClickBuildTower();
                    break;
                case 7:
                    context = $"op {i}: undo last";
                    f.Hud.ClickUndoLast();
                    break;
                case 8:
                    context = $"op {i}: redo last";
                    f.Hud.ClickRedoLast();
                    break;
                case 9:
                    context = $"op {i}: undo turn";
                    f.Hud.ClickUndoTurn();
                    break;
                case 10:
                    context = $"op {i}: redo all";
                    f.Hud.ClickRedoAll();
                    break;
                default:
                    context = $"op {i}: end turn";
                    f.Hud.ClickEndTurn();
                    break;
            }
            f.AssertInSync(context);
        }
    }
}
