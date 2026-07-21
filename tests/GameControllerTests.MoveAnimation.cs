// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Unit-move travel animation (issue #163) --------------------------
    //
    // Both move executors — the shared AI/replay one (GameOperations.
    // ExecuteMoveCore) and the human click-to-move handler (GameController.
    // ExecuteMove) — hint the view with the (from, to) pair via
    // AnimateUnitMove so the next occupant refresh can tween the rebuilt
    // glyph from source to destination instead of snapping. The
    // paced-vs-instant gate lives view-side (silent mode / Instant speed);
    // the controller hints unconditionally, mirroring
    // PlayDestructionEffect/PlaySound. Captures and combines hint too —
    // the arriving (post-combine) glyph is the one that travels. Buys,
    // tower builds, long-press rally, and undo/redo never hint.

    [Fact]
    public void AiTurn_CaptureMove_HintsUnitMoveAnimation()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        var move = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(2, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, move);

        c.StartGame();

        // Sanity: the capture happened.
        Assert.Equal(state.Players[0].Id, state.Grid.Get(HexCoord.FromOffset(2, 1))!.Owner);

        (HexCoord From, HexCoord To) anim = Assert.Single(map.AnimatedMoves);
        Assert.Equal(HexCoord.FromOffset(1, 1), anim.From);
        Assert.Equal(HexCoord.FromOffset(2, 1), anim.To);
    }

    [Fact]
    public void AiTurn_CombineMove_HintsUnitMoveAnimation()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        // Second Red recruit at (0,1); moving the (1,1) recruit onto it
        // combines into a Soldier — the combined glyph still travels.
        state.Grid.Get(HexCoord.FromOffset(0, 1))!.Occupant =
            new Unit(state.Players[0].Id);
        var move = new AiMoveAction(HexCoord.FromOffset(1, 1), HexCoord.FromOffset(0, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, move);

        c.StartGame();

        // Sanity: the combine happened.
        Assert.Equal(UnitLevel.Soldier,
            state.Grid.Get(HexCoord.FromOffset(0, 1))!.Unit!.Level);

        (HexCoord From, HexCoord To) anim = Assert.Single(map.AnimatedMoves);
        Assert.Equal(HexCoord.FromOffset(1, 1), anim.From);
        Assert.Equal(HexCoord.FromOffset(0, 1), anim.To);
    }

    // The buy/build negatives run one action each: StartPlayerTurn seeds a
    // first-turn AI territory's gold to its starting value (15g here), so a
    // single turn can't afford both a tower (15g) and a recruit (10g).

    [Fact]
    public void AiTurn_BuyUnit_HintsNoMoveAnimation()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        var buy = new AiBuyUnitAction(cap, HexCoord.FromOffset(2, 1), UnitLevel.Recruit);
        GameController c = BuildHarnessWithStubAi(state, map, hud, buy);

        c.StartGame();

        Assert.NotNull(state.Grid.Get(HexCoord.FromOffset(2, 1))!.Unit);
        Assert.Empty(map.AnimatedMoves);
    }

    [Fact]
    public void AiTurn_BuildTower_HintsNoMoveAnimation()
    {
        (GameState state, MockHexMapView map, MockHudView hud) = BuildAiFixture();
        HexCoord cap = RedCapital(state);
        var build = new AiBuildTowerAction(cap, HexCoord.FromOffset(0, 1));
        GameController c = BuildHarnessWithStubAi(state, map, hud, build);

        c.StartGame();

        Assert.IsType<Tower>(state.Grid.Get(HexCoord.FromOffset(0, 1))!.Occupant);
        Assert.Empty(map.AnimatedMoves);
    }

    [Fact]
    public void HumanMove_HintsMoveAnimation()
    {
        // The human click-to-move path (GameController.ExecuteMove) hints
        // like the AI executor — the view tween's duration follows the
        // Human Player Speed setting (Instant snaps, view-side).
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture move

        Assert.Equal(g.Red.Id, g.Tile(2, 1).Owner);
        (HexCoord From, HexCoord To) anim = Assert.Single(g.Map.AnimatedMoves);
        Assert.Equal(HexCoord.FromOffset(1, 1), anim.From);
        Assert.Equal(HexCoord.FromOffset(2, 1), anim.To);
    }

    [Fact]
    public void HumanMove_UndoDoesNotHintMoveAnimation()
    {
        // Undo restores state via snapshot + refresh with no hint — the
        // glyph reappears at the source instantly, never travels back.
        var g = new TestGame();
        g.Tile(1, 1).Occupant = new Unit(g.Red.Id);

        g.Map.SimulateClick(g.Tile(1, 1));
        g.Map.SimulateClick(g.Tile(2, 1)); // capture move
        Assert.Single(g.Map.AnimatedMoves);

        g.Hud.ClickUndoLast();

        // Sanity: the move was walked back.
        Assert.Null(g.Tile(2, 1).Unit);
        Assert.Single(g.Map.AnimatedMoves); // no new hint from the undo
    }

    [Fact]
    public void Replay_MoveBeat_HintsUnitMoveAnimationOnExecute()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(aiPacer: pacer);
        pacer.DrainAll();

        // Live script: Red buys a recruit, then moves it to capture the
        // adjacent enemy tile — [Buy, Move]. (Same fixture shape as
        // ReplayMoveSourcePreviewTests.)
        HexCoord capital = h.State.Territories
            .First(t => t.Owner == h.Players[0].Id).Capital!.Value;
        HexCoord from = HexCoord.FromOffset(0, 1).Equals(capital)
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);
        HexCoord to = HexCoord.FromOffset(2, 1);

        h.Map.SimulateClick(h.State.Grid.Get(capital)!);
        h.Hud.ClickBuyRecruit();
        h.Map.SimulateClick(h.State.Grid.Get(from)!);
        h.Map.SimulateClick(h.State.Grid.Get(from)!);   // pick the unit up
        h.Map.SimulateClick(h.State.Grid.Get(to)!);     // capture move
        pacer.DrainAll();

        Assert.Equal(2, h.Controller.ReplayBeats.Count);
        Assert.IsType<ReplayMoveBeat>(h.Controller.ReplayBeats[1]);
        // The live human move hinted once as it was played.
        Assert.Single(h.Map.AnimatedMoves);

        h.Controller.BeginReplay();

        pacer.StepOne();                                 // preview: Buy
        pacer.StepOne();                                 // execute: Buy
        pacer.StepOne();                                 // preview: Move
        Assert.Single(h.Map.AnimatedMoves);              // preview doesn't hint
        pacer.StepOne();                                 // execute: Move
        Assert.Equal(2, h.Map.AnimatedMoves.Count);      // replay execute hints
        (HexCoord From, HexCoord To) anim = h.Map.AnimatedMoves[^1];
        Assert.Equal(from, anim.From);
        Assert.Equal(to, anim.To);
    }
}
