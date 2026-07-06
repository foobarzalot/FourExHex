using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Buy-combine legality: two units combine only when their level sum is at
/// most Commander (<see cref="UnitLevelExtensions.CanCombineWith"/>). The AI
/// enumerator and the execution layer must both enforce it — an unchecked
/// pair fabricates an out-of-range <see cref="UnitLevel"/> in live play and
/// records a buy beat that replay's <c>ExecuteAiBuyUnit</c> validation
/// rejects, making the game's replay unplayable.
/// </summary>
public class BuyCombineLegalityTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    /// <summary>
    /// 20-tile Red territory, rich treasury, an unmoved Red Soldier, and an
    /// adjacent Blue Soldier on a mountain (defense 2 + 1 = 3) that neither
    /// a lone Captain (needs &gt; 3) nor any single affordable unit short of a
    /// Commander can capture — the exact bait that makes an unchecked
    /// enumerator offer a Captain-onto-Soldier "combine" (sum 5).
    /// </summary>
    private static (GameState State, Territory RedTerritory, HexCoord SoldierAt) BuildTemptingState()
    {
        HexGrid grid = TestHelpers.BuildRectGrid(5, 4, Red);
        HexCoord bait = HexCoord.FromOffset(5, 0);
        grid.Add(new HexTile(bait, Blue));
        grid.Get(bait)!.IsMountain = true;
        grid.Get(bait)!.Occupant = new Unit(Blue, UnitLevel.Soldier);
        HexCoord soldierAt = HexCoord.FromOffset(4, 0);
        grid.Get(soldierAt)!.Occupant = new Unit(Red, UnitLevel.Soldier);

        var players = new List<Player>
        {
            new Player("Red", Red, PlayerKind.Computer),
            new Player("Blue", Blue, PlayerKind.Computer),
        };
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        Territory red = state.Territories.First(t => t.Owner == Red);
        state.Treasury.SetGold(red.Capital!.Value, 300);
        return (state, red, soldierAt);
    }

    [Fact]
    public void EnumeratePhase2b_NeverEmitsOverCommanderCombines()
    {
        (GameState state, Territory red, _) = BuildTemptingState();

        List<AiBuyCombineAction> combines = AiCommon.EnumeratePhase2b(red, state)
            .Select(c => c.Action)
            .OfType<AiBuyCombineAction>()
            .ToList();

        // The legal Soldier-onto-Soldier (sum 4 = Commander) combine unlocks
        // the bait, so the phase must still offer something — this guards the
        // fixture against silently tempting nothing.
        Assert.True(combines.Count > 0,
            "fixture emitted no buy-combines at all: " + string.Join("; ",
                AiCommon.EnumeratePhase2b(red, state).Select(c => c.Action.ToString())));

        foreach (AiBuyCombineAction combine in combines)
        {
            Unit target = state.Grid.Get(combine.CombineTarget)!.Unit!;
            Assert.True(combine.BuyLevel.CanCombineWith(target.Level),
                $"Phase 2b emitted an illegal buy-combine: {combine.BuyLevel} onto " +
                $"{target.Level} at {combine.CombineTarget} (sum exceeds Commander).");
        }
    }

    [Fact]
    public void ExecuteAiBuyCombine_RejectsOverCommanderPair()
    {
        (GameState state, Territory red, HexCoord soldierAt) = BuildTemptingState();
        var ops = new GameOperations(
            state,
            new SessionState(),
            new MockHexMapView(),
            new MockHudView(),
            recordingMode: false,
            previewMode: false,
            isReplayMode: () => false,
            aiSilentMode: () => false,
            isReplayInstantActive: () => false,
            clearUndoAndReplayBookkeeping: () => { },
            onGameEnded: () => { },
            onHumanTurnStarted: () => { },
            maxTurnNumber: 100,
            masterSeed: 1,
            onAfterRefresh: null);

        Assert.Throws<System.InvalidOperationException>(() =>
            ops.ExecuteAiBuyCombine(red.Capital!.Value, soldierAt, UnitLevel.Captain));
        // The board is untouched: the Soldier is still a Soldier.
        Assert.Equal(UnitLevel.Soldier, state.Grid.Get(soldierAt)!.Unit!.Level);
    }
}
