using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pins the AiSimulator ↔ GameOperations mirror: for EVERY candidate
/// action the AI enumerators emit from a rich mid-game state, applying
/// it via <see cref="AiSimulator.Apply"/> on a clone must produce a
/// state checksum-identical to running the same action through the
/// real <see cref="GameOperations"/> ExecuteAi* path. The simulator's
/// doc comment promises this lockstep ("simulated futures match what
/// the real harness would produce"); if a new mutation rule lands on
/// one side only, AI scoring silently predicts wrong futures — this
/// test turns that drift into a red test naming the action and the
/// first divergent checksum line.
/// </summary>
public class AiSimulatorDriftTests
{
    private const int RedGold = 100;
    private const int BlueGold = 50;

    /// <summary>
    /// 8x4 mid-game-shaped state. Red (Computer, current player) owns
    /// the left three columns with unmoved units (Recruit + Soldier), a
    /// tree and a grave inside its territory, and a funded capital; Blue
    /// owns the rest with a border Recruit (capturable), a deeper
    /// Soldier, a Tower, and its own funded capital. Ingredients chosen
    /// so the enumerators emit every action kind: capture moves,
    /// repositions, tree/grave clears, buy-place, buy-capture,
    /// buy-combine, and tower builds.
    /// </summary>
    private static GameState BuildRichState() => BuildRichState(randomized: false);

    private static GameState BuildRichState(bool randomized, bool originMerge = false)
    {
        var red = new Player("Red", PlayerId.FromIndex(0), PlayerKind.Computer);
        var blue = new Player("Blue", PlayerId.FromIndex(1), PlayerKind.Computer);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(8, 4, blue.Id);
        for (int col = 0; col < 3; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                grid.Get(HexCoord.FromOffset(col, row))!.Owner = red.Id;
            }
        }

        // Red pieces: two unmoved units, a tree, a grave.
        grid.Get(HexCoord.FromOffset(1, 1))!.Occupant = new Unit(red.Id, UnitLevel.Recruit);
        grid.Get(HexCoord.FromOffset(2, 1))!.Occupant = new Unit(red.Id, UnitLevel.Soldier);
        grid.Get(HexCoord.FromOffset(0, 2))!.Occupant = new Tree();
        grid.Get(HexCoord.FromOffset(1, 3))!.Occupant = new Grave();

        // Blue pieces: a capturable border Recruit, a deeper Soldier, a Tower.
        grid.Get(HexCoord.FromOffset(3, 1))!.Occupant = new Unit(blue.Id, UnitLevel.Recruit);
        grid.Get(HexCoord.FromOffset(5, 2))!.Occupant = new Unit(blue.Id, UnitLevel.Soldier);
        grid.Get(HexCoord.FromOffset(4, 3))!.Occupant = new Tower();

        if (originMerge)
        {
            // Detached Red patch (4,0)-(6,0) with its own Soldier, one Blue
            // bridge tile (3,0) away from the 12-tile main territory. The
            // patch Soldier capturing the bridge merges patch + main with
            // the ORIGIN (patch) being the smaller side, so the origin rule
            // and the largest rule pick different surviving capitals — a
            // one-sided threading of originCapital diverges the checksums.
            grid.Get(HexCoord.FromOffset(4, 0))!.Owner = red.Id;
            grid.Get(HexCoord.FromOffset(5, 0))!.Owner = red.Id;
            grid.Get(HexCoord.FromOffset(6, 0))!.Owner = red.Id;
            grid.Get(HexCoord.FromOffset(6, 0))!.Occupant = new Unit(red.Id, UnitLevel.Soldier);
        }

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury(),
            useRandomizedSelection: randomized,
            useOriginMergeCapital: originMerge);

        foreach (Territory t in state.Territories.Where(t => t.HasCapital))
        {
            state.Treasury.SetGold(t.Capital!.Value, t.Owner == red.Id ? RedGold : BlueGold);
        }
        return state;
    }

    /// <summary>
    /// Every distinct action any enumerator emits for the current
    /// player: the all-in-one <see cref="AiCommon.Enumerate"/> plus the
    /// phase-specific enumerators ComputerAi actually drives, so kinds
    /// only a phase helper emits (e.g. buy-combine) are covered too.
    /// </summary>
    private static List<AiAction> AllCandidateActions(GameState state)
    {
        PlayerId current = state.Turns.CurrentPlayer.Id;
        var candidates = new List<AiCandidate>();
        foreach (Territory t in state.Territories.Where(t => t.Owner == current))
        {
            candidates.AddRange(AiCommon.Enumerate(t, state));
            candidates.AddRange(AiCommon.EnumeratePhase2b(t, state));
            candidates.AddRange(AiCommon.EnumeratePhase3(t, state));
            candidates.AddRange(AiCommon.EnumeratePhase4Towers(t, state));
            foreach (HexCoord c in t.Coords)
            {
                if (state.Grid.Get(c)?.Unit is Unit u && !u.HasMovedThisTurn)
                {
                    candidates.AddRange(AiCommon.EnumeratePhase1ForUnit(c, u, t, state));
                    candidates.AddRange(AiCommon.EnumeratePhase2aForUnit(c, u, t, state));
                    candidates.AddRange(AiCommon.EnumeratePhase4bForUnit(c, u, t, state));
                }
            }
        }
        return candidates.Select(c => c.Action).Distinct().ToList();
    }

    /// <summary>
    /// Run one action through the real mutation path: a fresh
    /// <see cref="GameOperations"/> (mock views, inert callbacks) over a
    /// clone of <paramref name="initial"/>, dispatched to the matching
    /// ExecuteAi* method. Returns the mutated clone.
    /// </summary>
    private static GameState ExecuteViaGameOperations(GameState initial, AiAction action)
    {
        GameState real = AiSimulator.Clone(initial);
        var ops = new GameOperations(
            real,
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
        switch (action)
        {
            case AiMoveAction mv:
                ops.ExecuteAiMove(mv.Source, mv.Destination);
                break;
            case AiBuyUnitAction bu:
                ops.ExecuteAiBuyUnit(bu.Capital, bu.Destination, bu.Level);
                break;
            case AiBuyCombineAction bc:
                ops.ExecuteAiBuyCombine(bc.Capital, bc.CombineTarget, bc.BuyLevel);
                break;
            case AiBuildTowerAction bt:
                // Mirror AiActionLowering: a tower intent on an own
                // unmoved unit's tile executes as TWO discrete beats —
                // the make-way reposition to the deterministic escape,
                // then the build on the vacated tile. The simulator
                // applies the same net outcome atomically; this sweep
                // pins that the two-beat real path matches it.
                if (real.Grid.Get(bt.Destination)?.Occupant is Unit)
                {
                    Territory terr = real.Territories.First(t => t.Contains(bt.Destination));
                    HexCoord escape = PurchaseRules.TowerPushDestination(
                        bt.Destination, terr, real.Grid)!.Value;
                    ops.ExecuteAiMove(bt.Destination, escape);
                }
                ops.ExecuteAiBuildTower(bt.Capital, bt.Destination);
                break;
            default:
                throw new NotSupportedException(
                    $"Unexpected enumerated action kind {action.GetType().Name}.");
        }
        return real;
    }

    /// <summary>First line where the two canonical strings differ — far
    /// more readable on failure than two full board dumps.</summary>
    private static string FirstDifference(string sim, string real)
    {
        string[] simLines = sim.Split('\n');
        string[] realLines = real.Split('\n');
        int n = Math.Max(simLines.Length, realLines.Length);
        for (int i = 0; i < n; i++)
        {
            string s = i < simLines.Length ? simLines[i] : "<missing>";
            string r = i < realLines.Length ? realLines[i] : "<missing>";
            if (s != r) return $"line {i}: simulator [{s}] vs GameOperations [{r}]";
        }
        return "<identical>";
    }

    [Fact]
    public void Clone_IsChecksumIdenticalToOriginal()
    {
        // Guards the test's own setup: both sides below start from
        // Clone(initial), so Clone infidelity would otherwise cancel out.
        GameState initial = BuildRichState();
        Assert.Equal(
            GameStateChecksum.Stringify(initial),
            GameStateChecksum.Stringify(AiSimulator.Clone(initial)));
    }

    [Fact]
    public void Fixture_EmitsEveryActionKind()
    {
        // Fixture-rot guard: if a rules change quietly stops the rich
        // state from producing some action kind, the drift sweep below
        // would still pass while covering less than it claims.
        GameState initial = BuildRichState();
        List<AiAction> actions = AllCandidateActions(initial);
        Assert.Contains(actions, a => a is AiMoveAction);
        Assert.Contains(actions, a => a is AiBuyUnitAction);
        Assert.Contains(actions, a => a is AiBuyCombineAction);
        Assert.Contains(actions, a => a is AiBuildTowerAction);
        // Push-out builds (tower onto an own unmoved unit's tile) must be
        // part of the sweep so the drift check covers the push mutation.
        Assert.Contains(actions, a =>
            a is AiBuildTowerAction bt && initial.Grid.Get(bt.Destination)!.Occupant is Unit);
        Assert.True(actions.Count >= 20,
            $"Expected a rich candidate set, got only {actions.Count}.");
    }

    [Fact]
    public void EveryEnumeratedCandidate_SimulatesIdenticallyToGameOperations()
    {
        AssertNoDrift(BuildRichState(randomized: false));
    }

    [Fact]
    public void EveryEnumeratedCandidate_SimulatesIdenticallyToGameOperations_WithRandomizedSelection()
    {
        // The fidelity gate for #91: with randomized capital placement on, a
        // capture that relocates a capital must place it on the SAME tile in
        // the cloned 1-ply simulation as in real play — otherwise AI scoring
        // predicts a board that won't happen. Board-state-derived seeding makes
        // the clone reproduce the real pick; this proves it for every candidate.
        AssertNoDrift(BuildRichState(randomized: true));
    }

    [Fact]
    public void EveryEnumeratedCandidate_SimulatesIdenticallyToGameOperations_WithOriginMergeCapital()
    {
        // The fidelity gate for #117: with the origin-capital merge rule on,
        // a capture that merges two same-owner territories must keep the SAME
        // surviving capital in the cloned 1-ply simulation as in real play.
        // The fixture's patch-side merge picks a different winner under the
        // origin rule than under largest-wins, so threading originCapital
        // through only one of the two paths fails this sweep.
        GameState initial = BuildRichState(randomized: true, originMerge: true);

        // Fixture guard: the merging capture must actually be enumerated.
        Assert.Contains(AllCandidateActions(initial), a =>
            a is AiMoveAction mv
            && mv.Source == HexCoord.FromOffset(6, 0)
            && mv.Destination == HexCoord.FromOffset(3, 0));

        AssertNoDrift(initial);
    }

    [Fact]
    public void BuildTower_OnFreeUnitTile_PushesUnitAside_SimAndRealAgree()
    {
        // Push-out build: a tower dropped on a tile holding an own
        // unmoved unit relocates the unit to its deterministic push
        // destination without consuming its move — identically in the
        // simulator and the real ExecuteAiBuildTower path.
        GameState initial = BuildRichState();
        PlayerId red = initial.Players[0].Id;
        Territory redTerr = initial.Territories.First(t => t.Owner == red);
        HexCoord cap = redTerr.Capital!.Value;
        HexCoord unitTile = HexCoord.FromOffset(1, 1);
        Assert.IsType<Unit>(initial.Grid.Get(unitTile)!.Occupant); // fixture guard
        HexCoord expectedEscape = PurchaseRules.TowerPushDestination(
            unitTile, redTerr, initial.Grid)!.Value;

        var action = new AiBuildTowerAction(cap, unitTile);
        GameState sim = AiSimulator.Clone(initial);
        AiSimulator.Apply(action, sim);
        GameState real = ExecuteViaGameOperations(initial, action);

        foreach (GameState after in new[] { sim, real })
        {
            Assert.IsType<Tower>(after.Grid.Get(unitTile)!.Occupant);
            Unit pushed = Assert.IsType<Unit>(after.Grid.Get(expectedEscape)!.Occupant);
            Assert.Equal(UnitLevel.Recruit, pushed.Level);
            Assert.False(pushed.HasMovedThisTurn);
            Assert.Equal(RedGold - PurchaseRules.TowerCostFor(Difficulty.Soldier),
                after.Treasury.GetGold(cap));
        }
        Assert.Equal(GameStateChecksum.Stringify(sim), GameStateChecksum.Stringify(real));
    }

    private static void AssertNoDrift(GameState initial)
    {
        List<AiAction> actions = AllCandidateActions(initial);

        foreach (AiAction action in actions)
        {
            GameState sim = AiSimulator.Clone(initial);
            AiSimulator.Apply(action, sim);
            GameState real = ExecuteViaGameOperations(initial, action);

            string simCanonical = GameStateChecksum.Stringify(sim);
            string realCanonical = GameStateChecksum.Stringify(real);
            Assert.True(simCanonical == realCanonical,
                $"Simulator drift for {action}: " +
                FirstDifference(simCanonical, realCanonical));
        }
    }
}
