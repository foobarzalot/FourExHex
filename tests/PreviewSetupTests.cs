using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Regression for "Preview enters with stale territory borders from
/// Record mode" — applying the tutorial's InitialSnapshot mutates
/// tile colors via per-tile setters, but the territory border lines
/// + capital nodes live in separate map layers that only refresh on
/// RebuildAfterTerritoryChange. Without that call the preview shows
/// the post-recording partition.
///
/// Tested via <see cref="PreviewSetup"/> (pure-C# helper) using
/// <see cref="MockHexMapView"/>; PreviewPane.Start delegates here
/// so the visual reset is reachable from xUnit even though
/// PreviewPane itself is Godot-coupled.
/// </summary>
public class PreviewSetupTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static (GameState state, Tutorial tutorial) BuildSetup()
    {
        var players = new List<Player>
        {
            new("Red", Red, AiKind.Human),
            new("Blue", Blue, AiKind.Heuristic),
        };
        HexGrid grid = TestHelpers.BuildRectGrid(3, 2, Red);
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = Blue;
        grid.Get(HexCoord.FromOffset(2, 1))!.Owner = Blue;
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(grid, state.Treasury, territories);
        var tutorial = new Tutorial
        {
            Title = "",
            Replay = new Replay(snapshot, 1, 0,
                new List<ReplayBeat> { new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 } }),
        };
        return (state, tutorial);
    }

    [Fact]
    public void Apply_CallsRebuildAfterTerritoryChange()
    {
        (GameState state, Tutorial tutorial) = BuildSetup();
        var map = new MockHexMapView();
        int before = map.RebuildCount;
        PreviewSetup.Apply(map, state, tutorial);
        Assert.True(map.RebuildCount > before,
            "PreviewSetup.Apply must call RebuildAfterTerritoryChange so " +
            "territory borders don't carry over from the prior session.");
    }

    [Fact]
    public void Apply_ClearsHighlight()
    {
        (GameState state, Tutorial tutorial) = BuildSetup();
        var map = new MockHexMapView();
        // Simulate a leftover highlight from the prior session.
        map.ShowHighlight(state.Territories[0]);

        PreviewSetup.Apply(map, state, tutorial);

        Assert.True(map.HighlightWasCleared,
            "PreviewSetup.Apply must clear the highlight so the prior " +
            "session's selection doesn't bleed in.");
    }

    [Fact]
    public void Apply_ClearsOverlays()
    {
        (GameState state, Tutorial tutorial) = BuildSetup();
        var map = new MockHexMapView();
        // Stale overlay state from prior session.
        map.ShowMoveTargets(new[] { new HexCoord(1, 1) }, UnitLevel.Recruit);
        map.ShowTowerTargets(new[] { new HexCoord(2, 2) });
        map.ShowMoveSource(new HexCoord(3, 3));

        PreviewSetup.Apply(map, state, tutorial);

        Assert.Empty(map.LastMoveTargets);
        Assert.Empty(map.LastTowerTargets);
        Assert.Null(map.LastMoveSource);
    }

    [Fact]
    public void Apply_ResetsTurnStateToTutorialInitial()
    {
        (GameState state, Tutorial tutorial) = BuildSetup();
        // Simulate the state being mid-game (turn 5, blue's turn).
        state.Turns.Reset(currentPlayerIndex: 1, turnNumber: 5);
        var map = new MockHexMapView();

        PreviewSetup.Apply(map, state, tutorial);

        Assert.Equal(1, state.Turns.TurnNumber);
        Assert.Equal(0, state.Turns.CurrentPlayerIndex);
    }
}
