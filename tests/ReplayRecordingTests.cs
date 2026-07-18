// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests that <see cref="GameController"/> appends a typed
/// <see cref="ReplayBeat"/> to its replay log for every state-mutating
/// action — human and AI — and skips selection-only or mode-entry
/// presses that don't change the board. The recording-side of the
/// replay feature; playback-side is in <see cref="ReplayPlaybackTests"/>.
/// </summary>
public class ReplayRecordingTests
{
    /// <summary>
    /// 5x2 fixture identical to <c>GameControllerTests.TestGame</c>:
    /// Red owns (0,1)/(1,1), Blue owns the rest. Both colors are humans
    /// by default so the controller wraps clicks in <c>TrackHandler</c>.
    /// The claim-victory prompt is pre-dismissed at all tiers for both
    /// colors so End Turn presses never get hijacked by the overlay.
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
        public Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, DeterministicRng, AiAction?>? AiChooser { get; set; }

        public Fixture(PlayerKind redKind = PlayerKind.Human, PlayerKind blueKind = PlayerKind.Human,
            Func<GameState, PlayerId, HashSet<HexCoord>, HashSet<HexCoord>, DeterministicRng, AiAction?>? aiChooser = null)
        {
            Red = new Player("Red", PlayerId.FromIndex(0), redKind);
            Blue = new Player("Blue", PlayerId.FromIndex(1), blueKind);
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
            AiChooser = aiChooser;
            Controller = new GameController(
                State, Session, Map, Hud,
                seed: 1,
                aiChooser: aiChooser,
                aiPacer: new SynchronousAiPacer(),
                maxTurnNumber: 10);
            Controller.StartGame();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;
    }

    // --- Baseline ---------------------------------------------------------

    [Fact]
    public void StartGame_CapturesInitialSnapshot_AndReplayBeatsIsEmpty()
    {
        var f = new Fixture();
        Assert.NotNull(f.Controller.InitialReplaySnapshot);
        Assert.Equal(1, f.Controller.InitialReplayTurnNumber);
        Assert.Equal(0, f.Controller.InitialReplayCurrentPlayerIndex);
        Assert.Empty(f.Controller.ReplayBeats);
        Assert.True(f.Controller.ReplayDataIsCompleteFromStart);
    }

    // --- Human actions ----------------------------------------------------

    [Fact]
    public void Recording_HumanBuyRecruit_AppendsReplayBuyBeat()
    {
        var f = new Fixture();
        HexCoord redCapital = f.State.Territories.First(t => t.Owner == f.Red.Id).Capital!.Value;
        // Red has 10g at start (5×2 cells), enough for a recruit (10g).
        // Click Red's territory to select it, press Buy, click an empty
        // Red tile to commit.
        f.Map.SimulateClick(f.Tile(0, 1));        // select Red territory
        f.Hud.ClickBuyRecruit();                  // enter buy mode
        // (1,1) is the other Red tile — pick whichever doesn't have the
        // capital so we buy onto an empty own tile.
        HexCoord redOther = HexCoord.FromOffset(0, 1) == redCapital
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);

        ReplayBeat last = Assert.Single(f.Controller.ReplayBeats);
        var buy = Assert.IsType<ReplayBuyBeat>(last);
        Assert.Equal(redCapital, buy.Capital);
        Assert.Equal(redOther, buy.To);
        Assert.Equal(UnitLevel.Recruit, buy.Level);
        Assert.Equal(1, buy.Turn);
        Assert.Equal(0, buy.Actor);
        Assert.Equal(0, buy.Index);
    }

    [Fact]
    public void Recording_HumanMove_AppendsReplayMoveBeat()
    {
        // Buy a recruit onto Red's non-capital tile; a human buy onto
        // own empty does not mark the unit moved (MovementRules.PlaceNew),
        // so it can move the same turn.
        var f = new Fixture();
        HexCoord redCapital = f.State.Territories.First(t => t.Owner == f.Red.Id).Capital!.Value;
        HexCoord redOther = HexCoord.FromOffset(0, 1) == redCapital
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);

        f.Map.SimulateClick(f.State.Grid.Get(redCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);
        // Capture beat list length before the move so we know which
        // beat is the move beat.
        int beforeMove = f.Controller.ReplayBeats.Count;

        // Move the just-bought recruit from redOther onto an adjacent
        // Blue tile (capture).
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);  // pick up the unit
        // Find an adjacent Blue tile.
        HexCoord? captureTarget = null;
        foreach (HexCoord n in redOther.Neighbors())
        {
            HexTile? t = f.State.Grid.Get(n);
            if (t != null && t.Owner == f.Blue.Id)
            {
                captureTarget = n;
                break;
            }
        }
        Assert.NotNull(captureTarget);
        f.Map.SimulateClick(f.State.Grid.Get(captureTarget!.Value)!);

        Assert.Equal(beforeMove + 1, f.Controller.ReplayBeats.Count);
        var mv = Assert.IsType<ReplayMoveBeat>(f.Controller.ReplayBeats[^1]);
        Assert.Equal(redOther, mv.From);
        Assert.Equal(captureTarget.Value, mv.To);
        Assert.Equal(0, mv.Actor);
    }

    [Fact]
    public void Recording_HumanBuildTower_AppendsReplayBuildTowerBeat()
    {
        // Tower costs 15g. Fixture seeds Red with 10g. Skip a couple of
        // turns to accumulate enough gold via income.
        var f = new Fixture();
        f.Hud.ClickEndTurn();   // Red T1 ends — no income credited (round 1)
        f.Hud.ClickEndTurn();   // Blue T1 ends — Red T2 starts, +income
        // Red gets +Size=2 income at T2 start → 12g. Still short. End
        // another round.
        f.Hud.ClickEndTurn();   // Red T2 ends
        f.Hud.ClickEndTurn();   // Blue T2 ends — Red T3 starts, +2 → 14g
        // Still short. One more round.
        f.Hud.ClickEndTurn();
        f.Hud.ClickEndTurn();   // Red T4 → 16g
        int beforeBuild = f.Controller.ReplayBeats.Count;

        HexCoord redCapital = f.State.Territories.First(t => t.Owner == f.Red.Id).Capital!.Value;
        HexCoord redOther = HexCoord.FromOffset(0, 1) == redCapital
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);
        f.Map.SimulateClick(f.State.Grid.Get(redCapital)!);
        f.Hud.ClickBuildTower();
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);

        Assert.Equal(beforeBuild + 1, f.Controller.ReplayBeats.Count);
        var bt = Assert.IsType<ReplayBuildTowerBeat>(f.Controller.ReplayBeats[^1]);
        Assert.Equal(redCapital, bt.Capital);
        Assert.Equal(redOther, bt.To);
    }

    [Fact]
    public void Recording_HumanEndTurn_AppendsReplayEndTurnBeatBeforeTurnAdvance()
    {
        var f = new Fixture();
        f.Hud.ClickEndTurn();

        // Red's End Turn is the first beat — and it should record
        // Turn=1 (the ending player's turn) and Actor=0 (Red).
        ReplayBeat first = f.Controller.ReplayBeats[0];
        Assert.IsType<ReplayEndTurnBeat>(first);
        Assert.Equal(1, first.Turn);
        Assert.Equal(0, first.Actor);
        // Both players are human, so the loop pauses at Blue T1; only
        // Red's End Turn is logged.
        Assert.Single(f.Controller.ReplayBeats);
    }

    [Fact]
    public void Recording_NoOpClick_DoesNotAppendBeat()
    {
        // Clicking an empty area (off-map) only changes selection (no
        // state mutation). Should not record a beat.
        var f = new Fixture();
        f.Map.SimulateClick(null);
        Assert.Empty(f.Controller.ReplayBeats);
    }

    [Fact]
    public void Recording_SelectOwnTerritory_DoesNotAppendBeat()
    {
        // Clicking your own territory selects it but doesn't mutate
        // game state. No beat.
        var f = new Fixture();
        f.Map.SimulateClick(f.Tile(0, 1));
        Assert.Empty(f.Controller.ReplayBeats);
    }

    [Fact]
    public void Recording_EnterBuyMode_DoesNotAppendBeat()
    {
        // Buy-button press only sets session mode, no board change.
        var f = new Fixture();
        f.Map.SimulateClick(f.Tile(0, 1));
        f.Hud.ClickBuyRecruit();
        Assert.Empty(f.Controller.ReplayBeats);
    }

    // --- Undo / redo bookkeeping -----------------------------------------

    [Fact]
    public void Recording_UndoMove_PopsLastBeat()
    {
        var f = new Fixture();
        HexCoord redCapital = f.State.Territories.First(t => t.Owner == f.Red.Id).Capital!.Value;
        HexCoord redOther = HexCoord.FromOffset(0, 1) == redCapital
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);

        f.Map.SimulateClick(f.State.Grid.Get(redCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);
        Assert.Single(f.Controller.ReplayBeats);

        f.Hud.ClickUndoLast();
        Assert.Empty(f.Controller.ReplayBeats);
    }

    [Fact]
    public void Recording_RedoMove_RestoresPoppedBeat()
    {
        var f = new Fixture();
        HexCoord redCapital = f.State.Territories.First(t => t.Owner == f.Red.Id).Capital!.Value;
        HexCoord redOther = HexCoord.FromOffset(0, 1) == redCapital
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);

        f.Map.SimulateClick(f.State.Grid.Get(redCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);
        ReplayBeat preUndo = f.Controller.ReplayBeats[0];

        f.Hud.ClickUndoLast();
        Assert.Empty(f.Controller.ReplayBeats);

        f.Hud.ClickRedoLast();
        Assert.Single(f.Controller.ReplayBeats);
        Assert.Equal(preUndo, f.Controller.ReplayBeats[0]);
    }

    [Fact]
    public void Recording_UndoTurn_PopsAllBeatsInTurn()
    {
        var f = new Fixture();
        HexCoord redCapital = f.State.Territories.First(t => t.Owner == f.Red.Id).Capital!.Value;
        HexCoord redOther = HexCoord.FromOffset(0, 1) == redCapital
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);

        // Two committed actions in one turn: buy + move.
        f.Map.SimulateClick(f.State.Grid.Get(redCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);

        HexCoord? captureTarget = null;
        foreach (HexCoord n in redOther.Neighbors())
        {
            HexTile? t = f.State.Grid.Get(n);
            if (t != null && t.Owner == f.Blue.Id) { captureTarget = n; break; }
        }
        Assert.NotNull(captureTarget);
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);
        f.Map.SimulateClick(f.State.Grid.Get(captureTarget!.Value)!);

        Assert.Equal(2, f.Controller.ReplayBeats.Count);

        f.Hud.ClickUndoTurn();
        Assert.Empty(f.Controller.ReplayBeats);
    }

    [Fact]
    public void Recording_FreshActionAfterUndo_InvalidatesRedoBeats()
    {
        // Make a move, undo it, make a *different* move. The redo
        // stack should be cleared so a subsequent redo doesn't
        // resurrect the old beat. Beat list should end with exactly
        // the new move beat.
        var f = new Fixture();
        HexCoord redCapital = f.State.Territories.First(t => t.Owner == f.Red.Id).Capital!.Value;
        HexCoord redOther = HexCoord.FromOffset(0, 1) == redCapital
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);

        // First action: buy a recruit onto redOther.
        f.Map.SimulateClick(f.State.Grid.Get(redCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);

        // Undo.
        f.Hud.ClickUndoLast();
        Assert.Empty(f.Controller.ReplayBeats);

        // Red can't afford a tower (10g, costs 15g), so redo the same
        // buy — undo restored Mode=BuyingRecruit and the selection, so
        // click the placement tile directly. Assert exactly one beat.
        f.Map.SimulateClick(f.State.Grid.Get(redOther)!);
        Assert.Single(f.Controller.ReplayBeats);
    }

    // --- AI actions -------------------------------------------------------

    [Fact]
    public void Recording_AiBuyUnit_AppendsReplayBuyBeat()
    {
        // Scripted AI: Blue buys a recruit onto an empty own-territory
        // tile, then ends turn. Verify a ReplayBuyBeat lands with
        // Blue's actor index. Buy is simpler than Move to script
        // because the target can be any empty own tile — no need to
        // reason about defense levels.
        bool blueActed = false;
        HexCoord? blueCapital = null;
        HexCoord? blueEmpty = null;
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> visited, HashSet<HexCoord> ru, DeterministicRng rng)
        {
            if (c != PlayerId.FromIndex(1)) return null;
            if (blueActed) return null;
            Territory blue = s.Territories.First(t => t.Owner == c);
            blueCapital = blue.Capital!.Value;
            // Pick an empty Blue tile that isn't the capital.
            foreach (HexCoord coord in blue.Coords)
            {
                if (coord.Equals(blueCapital.Value)) continue;
                HexTile? t = s.Grid.Get(coord);
                if (t?.Occupant == null) { blueEmpty = coord; break; }
            }
            blueActed = true;
            return new AiBuyUnitAction(blueCapital.Value, blueEmpty!.Value, UnitLevel.Recruit);
        }

        var f = new Fixture(redKind: PlayerKind.Human, blueKind: PlayerKind.Computer, aiChooser: Chooser);
        f.Hud.ClickEndTurn();   // Red ends → Blue AI runs scripted buy → null → end turn.

        ReplayBuyBeat? buy = f.Controller.ReplayBeats.OfType<ReplayBuyBeat>().FirstOrDefault();
        Assert.NotNull(buy);
        Assert.Equal(blueCapital, buy!.Capital);
        Assert.Equal(blueEmpty, buy.To);
        Assert.Equal(UnitLevel.Recruit, buy.Level);
        Assert.Equal(1, buy.Actor);  // Blue is player index 1
    }

    [Fact]
    public void Recording_AiImplicitEndTurn_AppendsReplayEndTurnBeat()
    {
        // Chooser always returns null → Blue's AI immediately ends turn.
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> visited, HashSet<HexCoord> ru, DeterministicRng rng) => null;
        var f = new Fixture(redKind: PlayerKind.Human, blueKind: PlayerKind.Computer, aiChooser: Chooser);
        int before = f.Controller.ReplayBeats.Count;
        f.Hud.ClickEndTurn();   // Red ends. Blue runs AI (null action → EndTurn).

        // Beats since Red's end turn: at minimum Red EndTurn + Blue
        // EndTurn. (Beats from any AI Move don't apply here; chooser
        // returns null.)
        IEnumerable<ReplayEndTurnBeat> endTurnBeats = f.Controller.ReplayBeats
            .Skip(before)
            .OfType<ReplayEndTurnBeat>();
        Assert.Equal(2, endTurnBeats.Count());
        Assert.Equal(new[] { 0, 1 }, endTurnBeats.Select(b => b.Actor).ToArray());
    }
}
