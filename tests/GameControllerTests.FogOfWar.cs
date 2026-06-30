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

        public FogGame(HexGrid grid, GameMode mode, PlayerKind blueKind = PlayerKind.Computer)
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
            Controller = new GameController(State, Session, Map, Hud);
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
    public void FogOfWar_RemembersVisibleTilesAtStart()
    {
        var game = new FogGame(CornerVsRest(), GameMode.FogOfWar);

        Assert.NotNull(game.Map.LastFog);
        foreach (HexCoord c in game.Map.LastFog!.Visible)
            Assert.True(game.State.IsRemembered(c));
        // Memory is the same instance the controller maintains on GameState.
        Assert.Same(game.State.Remembered, game.Map.LastFog.Remembered);
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
