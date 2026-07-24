// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public partial class GameControllerTests
{
    // --- Fog Of War mode -------------------------------------------------

    private sealed class FogGame
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public FogGame(
            HexGrid grid, GameMode mode,
            PlayerKind blueKind = PlayerKind.Computer,
            bool recordingMode = false)
        {
            Red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Human);
            Blue = new Player("Blue", PlayerId.FromIndex(1), blueKind);
            var players = new List<Player> { Red, Blue };
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(
                grid, territories, players, new TurnState(players), new Treasury(),
                waterCoords: null, mode: mode);
            Session = new SessionState();
            Map = new MockHexMapView();
            Hud = new MockHudView();
            Controller = new GameController(State, Session, Map, Hud,
                recordingMode: recordingMode);
            Controller.StartGame();
        }
    }

    // 7x2: Red owns the top-left corner block (cols 0-1), Blue owns the rest.
    private static HexGrid CornerVsRest()
    {
        var grid = TestHelpers.BuildRectGrid(7, 2, PlayerId.FromIndex(1));
        for (int row = 0; row < 2; row++)
            for (int col = 0; col < 2; col++)
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = PlayerId.FromIndex(0);
        return grid;
    }

    [Fact]
    public void FogOfWar_StartGame_PushesProjectionForHumanSight()
    {
        var game = new FogGame(CornerVsRest(), GameMode.FogOfWar);

        Assert.NotNull(game.Map.LastFog);
        HashSet<HexCoord> expected = VisibilityRules.ComputeVisible(game.State, game.Red.Id);
        Assert.Equal(expected, game.Map.LastFog!.Visible.ToHashSet());
        // Red's own corner tiles are in sight; a far Blue tile is not.
        Assert.Contains(HexCoord.FromOffset(0, 0), game.Map.LastFog.Visible);
        Assert.DoesNotContain(HexCoord.FromOffset(6, 1), game.Map.LastFog.Visible);
    }

    [Fact]
    public void Freeform_StartGame_PushesNullFog()
    {
        var game = new FogGame(CornerVsRest(), GameMode.Freeform);
        Assert.Null(game.Map.LastFog);
        Assert.True(game.Map.ShowFogCount > 0); // ShowFog(null) was actually called
    }

    [Fact]
    public void FogOfWar_MarksVisibleTilesSeenAtStart()
    {
        var game = new FogGame(CornerVsRest(), GameMode.FogOfWar);

        Assert.NotNull(game.Map.LastFog);
        foreach (HexCoord c in game.Map.LastFog!.Visible)
            Assert.True(game.State.IsSeen(c));
        // The seen set is the same instance the controller maintains on GameState.
        Assert.Same(game.State.Seen, game.Map.LastFog.Seen);
    }

    // Build a tower on one of Red's owned empty tiles and return its coord, so
    // the undo tests have a concrete, genuinely-undoable mutation to check.
    private static HexCoord BuildTowerForRed(FogGame game)
    {
        HexTile anyRed = game.State.Grid.Get(HexCoord.FromOffset(0, 0))!;
        game.Map.SimulateClick(anyRed);
        HexCoord cap = game.Session.SelectedTerritory!.Capital!.Value;
        game.State.Treasury.SetGold(cap, 30);

        HexCoord towerCoord = default;
        foreach ((int c, int r) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            HexCoord coord = HexCoord.FromOffset(c, r);
            if (coord != cap && game.State.Grid.Get(coord)!.Occupant == null)
            {
                towerCoord = coord;
                break;
            }
        }
        game.Hud.ClickBuildTower();
        game.Map.SimulateClick(game.State.Grid.Get(towerCoord)!);
        return towerCoord;
    }

    [Fact]
    public void Freeform_UndoLast_RevertsAction()
    {
        // Control for the fog test below: the same build-tower action IS
        // undoable in a normal game, so the fog assertion isn't a false pass.
        var game = new FogGame(CornerVsRest(), GameMode.Freeform);
        HexCoord tower = BuildTowerForRed(game);
        Assert.IsType<Tower>(game.State.Grid.Get(tower)!.Occupant);

        game.Hud.ClickUndoLast();
        Assert.Null(game.State.Grid.Get(tower)!.Occupant);
    }

    [Fact]
    public void FogOfWar_UndoRedo_AreBlocked_NoFreeScouting()
    {
        // Undo is disabled under fog: undoing a capture/build after it revealed
        // tiles would be free scouting, because fog memory is sticky across
        // undo. So the action must persist through undo (and redo).
        var game = new FogGame(CornerVsRest(), GameMode.FogOfWar);
        HexCoord tower = BuildTowerForRed(game);
        Assert.IsType<Tower>(game.State.Grid.Get(tower)!.Occupant);

        game.Hud.ClickUndoLast();
        Assert.IsType<Tower>(game.State.Grid.Get(tower)!.Occupant); // not reverted
        game.Hud.ClickUndoTurn();
        Assert.IsType<Tower>(game.State.Grid.Get(tower)!.Occupant);
        game.Hud.ClickRedoLast();
        Assert.IsType<Tower>(game.State.Grid.Get(tower)!.Occupant);
    }

    [Fact]
    public void FogOfWar_RecordingMode_UndoRedoAllowed_ForAuthoring()
    {
        // Tutorial authoring is exempt from the fog undo lock: the author
        // needs to revert recording mistakes, and free scouting is moot —
        // playback re-fogs from scratch (BeginReplay clears Seen).
        var game = new FogGame(CornerVsRest(), GameMode.FogOfWar, recordingMode: true);
        HexCoord tower = BuildTowerForRed(game);
        Assert.IsType<Tower>(game.State.Grid.Get(tower)!.Occupant);

        game.Hud.ClickUndoLast();
        Assert.Null(game.State.Grid.Get(tower)!.Occupant); // reverted
        game.Hud.ClickRedoLast();
        Assert.IsType<Tower>(game.State.Grid.Get(tower)!.Occupant); // redone
    }

    // --- Win gating: no victory over ground the human has never seen ------

    [Fact]
    public void FogOfWar_ClaimVictoryPrompt_SuppressedWhileLandHidden()
    {
        // Red owns 4/6 of the row — well past the 50% tier — but col 5 lies
        // outside Red's sight and has never been seen. End Turn just ends the
        // turn: you can't claim a victory over ground you've never laid eyes on.
        var game = new FogGame(LopsidedRow(), GameMode.FogOfWar);
        Assert.False(game.State.IsSeen(HexCoord.FromOffset(5, 0)));

        game.Hud.ClickEndTurn();

        Assert.Null(game.Session.PendingClaimVictory);
        Assert.False(game.Session.IsGameOver);
    }

    [Fact]
    public void FogOfWar_ClaimVictoryPrompt_FiresOnceMapFullyRevealed()
    {
        // Same board, same ownership share: the tier math is untouched, the
        // reveal is only a precondition. Once the last tile is known, the 50%
        // offer appears exactly as it does in freeform.
        var game = new FogGame(LopsidedRow(), GameMode.FogOfWar);
        TestHelpers.RevealWholeGrid(game.State);

        game.Hud.ClickEndTurn();

        Assert.NotNull(game.Session.PendingClaimVictory);
        Assert.Equal(50, game.Session.PendingClaimVictory!.Value.ThresholdPercent);
    }

    // Red's mainland (cols 0-1, capital-bearing) plus one Blue tile at col 2,
    // and a lone Red island at col 4 — col 3 is water, so the island is never
    // adjacent to Red's sight and stays unseen for the whole game. A Red
    // recruit on col 1 can capture col 2, which hands Red every tile.
    private static HexGrid IslandDomination()
    {
        PlayerId red = PlayerId.FromIndex(0);
        HexGrid grid = TestHelpers.BuildSpotGrid(
            red,
            HexCoord.FromOffset(0, 0), HexCoord.FromOffset(1, 0),
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(4, 0));
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = PlayerId.FromIndex(1);
        grid.Get(HexCoord.FromOffset(1, 0))!.Occupant = new Unit(red);
        return grid;
    }

    [Fact]
    public void FogOfWar_DominationWin_NotGatedByFog()
    {
        // Only the claim-victory prompt waits on a revealed map; the domination
        // win never does. Sight comes only from capital-bearing territories, so
        // Red's lone island is owned yet never seen — and the sweep still ends
        // the game, or a player who had taken the whole map would be stranded
        // with nothing left to capture.
        var game = new FogGame(IslandDomination(), GameMode.FogOfWar);
        HexCoord island = HexCoord.FromOffset(4, 0);
        Assert.False(game.State.IsSeen(island));

        game.Map.SimulateClick(game.State.Grid.Get(HexCoord.FromOffset(1, 0)));
        game.Map.SimulateClick(game.State.Grid.Get(HexCoord.FromOffset(2, 0)));

        Assert.False(game.State.IsSeen(island)); // still never laid eyes on it
        Assert.True(game.Session.IsGameOver);
        Assert.Equal(game.Red.Id, game.Session.Winner);
    }

    [Fact]
    public void FogOfWar_Victory_RevealsWholeMap()
    {
        // Red (human) owns 4/6 of a connected row → can claim victory. Winning
        // lifts the fog: the controller pushes ShowFog(null) once the game ends.
        var game = new FogGame(LopsidedRow(), GameMode.FogOfWar);
        Assert.NotNull(game.Map.LastFog); // fog active mid-game
        // Claiming requires a fully-revealed map (see the win-gating facts
        // above), so the far end of the row has to be known first.
        TestHelpers.RevealWholeGrid(game.State);

        game.Hud.ClickEndTurn();              // trips the 50% claim-victory offer
        game.Hud.ClickClaimVictoryWinNow();   // declares Red the winner

        Assert.True(game.Session.IsGameOver);
        Assert.Null(game.Map.LastFog);        // fog lifted on victory
    }

    [Fact]
    public void FogOfWar_BeginReplay_ResetsFogMemory()
    {
        // Replay must re-animate fog from scratch, not inherit the live game's
        // accumulated exploration. BeginReplay clears the seen set; its setup
        // refresh re-marks only the replay's initial sight.
        var game = new FogGame(CornerVsRest(), GameMode.FogOfWar);
        HexCoord far = HexCoord.FromOffset(6, 1); // far from Red's starting corner
        game.State.MarkSeen(far);                 // simulate prior exploration
        Assert.True(game.State.IsSeen(far));

        game.Controller.BeginReplay();

        Assert.False(game.State.IsSeen(far)); // fog memory reset for the replay
    }

    [Fact]
    public void FogOfWar_TwoHumans_FailsSafeToNullFog()
    {
        // The menu lock guarantees one human; if somehow two humans reach a
        // Fog game (e.g. a bad load), the controller renders without fog rather
        // than guessing a perspective.
        var game = new FogGame(CornerVsRest(), GameMode.FogOfWar, blueKind: PlayerKind.Human);
        Assert.Null(game.Map.LastFog);
    }
}
