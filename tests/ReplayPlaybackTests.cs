using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests that <see cref="GameController.BeginReplay"/> rewinds to the
/// initial snapshot, steps through the recorded beats via
/// <see cref="IAiPacer"/>, and produces the same final state as the
/// live game. Also covers the read-only invariants during playback:
/// human input is ignored and the autosave hook is suppressed.
/// </summary>
public class ReplayPlaybackTests
{
    /// <summary>
    /// Same 5x2 fixture as <see cref="ReplayRecordingTests"/>, but with
    /// a <see cref="QueuedAiPacer"/> so replay playback can be stepped
    /// deterministically via <c>Pacer.DrainAll()</c>. Both colors are
    /// Human by default so player input drives turn transitions.
    /// </summary>
    private class Fixture
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHexMapView Map { get; }
        public MockHudView Hud { get; }
        public GameController Controller { get; }
        public QueuedAiPacer Pacer { get; }
        public Player Red { get; }
        public Player Blue { get; }

        public Fixture(AiKind redKind = AiKind.Human, AiKind blueKind = AiKind.Human,
            Func<GameState, Color, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null)
        {
            Red = new Player("Red", new Color(1f, 0f, 0f), redKind);
            Blue = new Player("Blue", new Color(0f, 0f, 1f), blueKind);
            var players = new List<Player> { Red, Blue };

            HexGrid grid = TestHelpers.BuildRectGrid(5, 2, Blue.Color);
            grid.Get(HexCoord.FromOffset(0, 1))!.Color = Red.Color;
            grid.Get(HexCoord.FromOffset(1, 1))!.Color = Red.Color;

            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players, new TurnState(players), new Treasury());
            Session = new SessionState();
            Session.ClaimVictoryPromptedHighestThreshold[Red.Color] = 90;
            Session.ClaimVictoryPromptedHighestThreshold[Blue.Color] = 90;
            Map = new MockHexMapView();
            Hud = new MockHudView();
            foreach (KeyValuePair<HexCoord, Territory> kvp in territories.BuildTileIndex())
            {
                Map.TileIndex[kvp.Key] = kvp.Value;
            }
            Pacer = new QueuedAiPacer();
            Controller = new GameController(
                State, Session, Map, Hud,
                seed: 1,
                aiChooser: aiChooser,
                aiPacer: Pacer,
                maxTurnNumber: 20);
            Controller.StartGame();
            // StartGame may have scheduled an AI run; drain so the
            // fixture is on a stable human turn for further driving.
            Pacer.DrainAll();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;

        public HexCoord RedCapital =>
            State.Territories.First(t => t.Owner == Red.Color).Capital!.Value;
        public HexCoord RedOther =>
            HexCoord.FromOffset(0, 1).Equals(RedCapital)
                ? HexCoord.FromOffset(1, 1)
                : HexCoord.FromOffset(0, 1);

        /// <summary>
        /// Compare every tile's color and occupant type, plus each
        /// capital's gold, between the current live state and
        /// <paramref name="reference"/>. Used by playback tests to
        /// assert replay arrives at the same final state.
        /// </summary>
        public void AssertStateMatches(GameStateSnapshot reference)
        {
            var refTiles = new Dictionary<HexCoord, (Color Color, string? OccupantType)>();
            foreach ((HexCoord coord, Color color, HexOccupant? occupant) in reference.EnumerateTiles())
            {
                refTiles[coord] = (color, occupant?.GetType().Name);
            }
            foreach (HexTile live in State.Grid.Tiles)
            {
                (Color color, string? occType) = refTiles[live.Coord];
                Assert.Equal(color, live.Color);
                Assert.Equal(occType, live.Occupant?.GetType().Name);
            }

            var refGold = new Dictionary<HexCoord, int>();
            foreach ((HexCoord cap, int gold) in reference.EnumerateGold())
            {
                refGold[cap] = gold;
            }
            foreach (Territory t in State.Territories)
            {
                if (!t.HasCapital) continue;
                Assert.Equal(refGold[t.Capital!.Value], State.Treasury.GetGold(t.Capital!.Value));
            }
        }
    }

    // --- Round-trip determinism -------------------------------------------

    [Fact]
    public void Replay_PlaysHumanBuyThenEndTurn_ToSameFinalState()
    {
        var f = new Fixture();

        // Play a scripted game: Red buys a peasant, ends turn, Blue
        // ends turn (Blue is human; no input so manual ClickEndTurn).
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyPeasant();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);
        f.Hud.ClickEndTurn();
        f.Pacer.DrainAll();
        f.Hud.ClickEndTurn();  // Blue ends, hands back to Red for turn 2.
        f.Pacer.DrainAll();

        // Snapshot the live final state.
        GameStateSnapshot liveFinal = GameStateSnapshot.Capture(
            f.State.Grid, f.State.Treasury, f.State.Territories);

        // Replay should produce the same final state.
        Assert.True(f.Controller.ReplayBeats.Count >= 3,
            $"Expected >=3 beats, got {f.Controller.ReplayBeats.Count}");
        f.Controller.BeginReplay();
        f.Pacer.DrainAll();

        f.AssertStateMatches(liveFinal);
    }

    [Fact]
    public void Replay_PlaysAiBuy_ToSameFinalState()
    {
        bool blueActed = false;
        HexCoord? buyCapital = null;
        HexCoord? buyDest = null;
        AiAction? Chooser(GameState s, Color c, HashSet<HexCoord> visited, Random rng)
        {
            if (c != new Color(0f, 0f, 1f)) return null;
            if (blueActed) return null;
            Territory blue = s.Territories.First(t => t.Owner == c);
            buyCapital = blue.Capital!.Value;
            foreach (HexCoord coord in blue.Coords)
            {
                if (coord.Equals(buyCapital.Value)) continue;
                if (s.Grid.Get(coord)?.Occupant == null) { buyDest = coord; break; }
            }
            blueActed = true;
            return new AiBuyUnitAction(buyCapital.Value, buyDest!.Value, UnitLevel.Peasant);
        }

        var f = new Fixture(blueKind: AiKind.Random, aiChooser: Chooser);
        f.Hud.ClickEndTurn();  // Red ends → Blue AI runs scripted buy → null → end turn → Red T2.
        f.Pacer.DrainAll();

        GameStateSnapshot liveFinal = GameStateSnapshot.Capture(
            f.State.Grid, f.State.Treasury, f.State.Territories);

        f.Controller.BeginReplay();
        f.Pacer.DrainAll();

        f.AssertStateMatches(liveFinal);
    }

    // --- Input lockout during replay --------------------------------------

    [Fact]
    public void Replay_TileClicks_AreIgnoredDuringPlayback()
    {
        var f = new Fixture();
        // Make a buy so there's a beat to replay.
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyPeasant();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);
        Assert.Single(f.Controller.ReplayBeats);

        f.Controller.BeginReplay();
        // Click an empty tile during replay — should be ignored.
        f.Map.SimulateClick(f.Tile(2, 0));
        // Beat list count must stay 1 (no new beat recorded).
        Assert.Single(f.Controller.ReplayBeats);

        f.Pacer.DrainAll();
        // Still 1 beat.
        Assert.Single(f.Controller.ReplayBeats);
    }

    [Fact]
    public void Replay_EndTurnButton_IsIgnoredDuringPlayback()
    {
        var f = new Fixture();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyPeasant();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);

        int beatsBeforeReplay = f.Controller.ReplayBeats.Count;
        f.Controller.BeginReplay();
        f.Hud.ClickEndTurn();
        // No new beat from the ignored end-turn press.
        Assert.Equal(beatsBeforeReplay, f.Controller.ReplayBeats.Count);
    }

    // --- HumanTurnStarted suppression -------------------------------------

    [Fact]
    public void Replay_HumanTurnStarted_DoesNotFireDuringPlayback()
    {
        var f = new Fixture();
        int fireCount = 0;
        f.Controller.HumanTurnStarted += () => fireCount++;

        // Build some beats: Red ends turn → Blue ends turn → Red T2.
        f.Hud.ClickEndTurn();
        f.Pacer.DrainAll();
        f.Hud.ClickEndTurn();
        f.Pacer.DrainAll();
        int liveFireCount = fireCount;
        Assert.True(liveFireCount >= 1, "Live play should have fired HumanTurnStarted");

        // Reset counter and replay — should not fire.
        fireCount = 0;
        f.Controller.BeginReplay();
        f.Pacer.DrainAll();
        Assert.Equal(0, fireCount);
    }

    // --- Replay state transitions ----------------------------------------

    [Fact]
    public void BeginReplay_NoInitialSnapshot_IsNoOp()
    {
        // Construct a controller without StartGame so no snapshot exists.
        var red = new Player("Red", new Color(1f, 0f, 0f));
        var blue = new Player("Blue", new Color(0f, 0f, 1f));
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, blue.Color);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var controller = new GameController(state, new SessionState(),
            new MockHexMapView(), new MockHudView(),
            aiPacer: new SynchronousAiPacer(),
            seed: 1);
        // No StartGame, so InitialReplaySnapshot is null.
        Assert.Null(controller.InitialReplaySnapshot);
        controller.BeginReplay();  // Should not throw or set _replayMode.
        Assert.False(controller.IsReplayMode);
    }

    [Fact]
    public void Replay_AfterCompletion_ClearsReplayMode()
    {
        var f = new Fixture();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyPeasant();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);

        f.Controller.BeginReplay();
        Assert.True(f.Controller.IsReplayMode);
        f.Pacer.DrainAll();
        Assert.False(f.Controller.IsReplayMode);
    }
}
