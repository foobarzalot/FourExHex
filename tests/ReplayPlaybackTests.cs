using System;
using System.Collections.Generic;
using System.Linq;
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

        public Fixture(PlayerKind redKind = PlayerKind.Human, PlayerKind blueKind = PlayerKind.Human,
            Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?>? aiChooser = null,
            bool instantReplay = false,
            Func<bool>? replayInstantMode = null)
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
            Pacer = new QueuedAiPacer();
            Controller = new GameController(
                State, Session, Map, Hud,
                seed: 1,
                aiChooser: aiChooser,
                aiPacer: Pacer,
                maxTurnNumber: 20,
                replayIsInstantMode: replayInstantMode
                    ?? (instantReplay ? () => true : (Func<bool>?)null));
            Controller.StartGame();
            // StartGame may have scheduled an AI run; drain so the
            // fixture is on a stable human turn for further driving.
            Pacer.DrainAll();
        }

        public HexTile Tile(int col, int row) => State.Grid.Get(HexCoord.FromOffset(col, row))!;

        public HexCoord RedCapital =>
            State.Territories.First(t => t.Owner == Red.Id).Capital!.Value;
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
            var refTiles = new Dictionary<HexCoord, (PlayerId Owner, string? OccupantType)>();
            foreach ((HexCoord coord, PlayerId color, HexOccupant? occupant, bool _) in reference.EnumerateTiles())
            {
                refTiles[coord] = (color, occupant?.GetType().Name);
            }
            foreach (HexTile live in State.Grid.Tiles)
            {
                (PlayerId color, string? occType) = refTiles[live.Coord];
                Assert.Equal(color, live.Owner);
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

        // Play a scripted game: Red buys a recruit, ends turn, Blue
        // ends turn (Blue is human; no input so manual ClickEndTurn).
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyRecruit();
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
    public void Replay_HumanBuyOntoOwnEmptyThenMoveSameUnit_ProducesSameFinalState()
    {
        // Regression for a replay-fidelity bug uncovered during the
        // ReplayRecorder extraction's manual smoke test: a human buys a
        // recruit onto an own-empty tile (HasMovedThisTurn=false on the
        // live side), then moves that recruit in the same turn (capture).
        // During replay, ExecuteAiBuyUnit's "fresh buy consumes the
        // unit's move" rule was applied unconditionally (no
        // !_isReplayMode() gate analogous to ExecuteAiMove's reposition
        // gate), so the subsequent move beat threw "unit has already
        // moved this turn." Pre-existing in main; visible once the
        // recorded log replays through this specific sequence.
        var f = new Fixture();

        // Step 1: select Red's territory, enter Buy Recruit mode, place
        // recruit onto own-empty RedOther.
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);
        Assert.True(f.State.Grid.Get(f.RedOther)!.Occupant is Unit,
            "Buy should have placed a recruit on RedOther.");
        Assert.False(((Unit)f.State.Grid.Get(f.RedOther)!.Occupant!).HasMovedThisTurn,
            "A human buy onto own-empty must leave HasMovedThisTurn=false " +
            "so the unit can still move this turn.");

        // Step 2: pick up that recruit and move it onto an adjacent
        // Blue tile (capture). Find any Blue tile adjacent to RedOther.
        HexCoord? captureTarget = null;
        foreach (HexCoord neighbor in f.RedOther.Neighbors())
        {
            HexTile? n = f.State.Grid.Get(neighbor);
            if (n != null && n.Owner == f.Blue.Id)
            {
                captureTarget = neighbor;
                break;
            }
        }
        Assert.True(captureTarget.HasValue,
            "Test setup error: RedOther should have at least one Blue neighbor.");

        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);
        f.Map.SimulateClick(f.State.Grid.Get(captureTarget!.Value)!);

        // Step 3: end turn so the beat log has more than just the buy + move.
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();  // Blue (human) ends → Red T2.

        GameStateSnapshot liveFinal = GameStateSnapshot.Capture(
            f.State.Grid, f.State.Treasury, f.State.Territories);

        // Replay must reproduce the live final state without throwing.
        f.Controller.BeginReplay();
        f.Pacer.DrainAll();

        f.AssertStateMatches(liveFinal);
    }

    [Fact]
    public void Replay_AfterUndoRedoChurn_ProducesSameFinalState()
    {
        // The beat log must stay faithful through undo/redo churn: each
        // undo trims the beat tail, each redo restores it, and a fresh
        // action after an undo drops the stashed forward branch. If the
        // session undo stack and the recorder's beat bookkeeping ever
        // diverge (the three-stack sync UndoReplayBeatSyncTests pins),
        // the trimmed tail is wrong and this round-trip desyncs.
        // No Treasury seeding: gold set after StartGame is NOT in the
        // initial replay snapshot, so the live and replay sides would
        // start from different treasuries and desync. The default
        // capital gold affords the single recruit this script buys.
        var f = new Fixture();

        // Buy a recruit, undo it, redo it — the kept action. Buy mode is
        // radio-style and persists across the place + the redo, so toggle
        // it off explicitly before the move clicks below (otherwise the
        // "pick up" click would buy-combine a second recruit instead).
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);
        f.Hud.ClickUndoLast();
        f.Hud.ClickRedoLast();
        f.Hud.ClickBuyRecruit();  // toggle buy mode off

        // Move it onto a Blue neighbor, then undo — and replace the
        // discarded forward branch with a different action (a second
        // buy onto the capital's own tile), invalidating the redo stash.
        HexCoord? captureTarget = null;
        foreach (HexCoord neighbor in f.RedOther.Neighbors())
        {
            HexTile? n = f.State.Grid.Get(neighbor);
            if (n != null && n.Owner == f.Blue.Id)
            {
                captureTarget = neighbor;
                break;
            }
        }
        Assert.True(captureTarget.HasValue,
            "Test setup error: RedOther should have at least one Blue neighbor.");
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);
        f.Map.SimulateClick(f.State.Grid.Get(captureTarget!.Value)!);
        f.Hud.ClickUndoLast();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);
        f.Map.SimulateClick(f.State.Grid.Get(captureTarget!.Value)!);

        // Undo EVERYTHING this turn, then redo it all back.
        f.Hud.ClickUndoTurn();
        f.Hud.ClickRedoAll();

        // The churn must leave exactly the kept actions in the log:
        // one buy, one move (plus the end-turn beats appended below).
        Assert.Equal(
            new[] { "ReplayBuyBeat", "ReplayMoveBeat" },
            f.Controller.ReplayBeats.Select(b => b.GetType().Name).ToArray());

        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();  // Blue ends → Red T2.

        GameStateSnapshot liveFinal = GameStateSnapshot.Capture(
            f.State.Grid, f.State.Treasury, f.State.Territories);

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
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> visited, Random rng)
        {
            if (c != PlayerId.FromIndex(1)) return null;
            if (blueActed) return null;
            Territory blue = s.Territories.First(t => t.Owner == c);
            buyCapital = blue.Capital!.Value;
            foreach (HexCoord coord in blue.Coords)
            {
                if (coord.Equals(buyCapital.Value)) continue;
                if (s.Grid.Get(coord)?.Occupant == null) { buyDest = coord; break; }
            }
            blueActed = true;
            return new AiBuyUnitAction(buyCapital.Value, buyDest!.Value, UnitLevel.Recruit);
        }

        var f = new Fixture(blueKind: PlayerKind.Computer, aiChooser: Chooser);
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
        f.Hud.ClickBuyRecruit();
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
        f.Hud.ClickBuyRecruit();
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
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, blue.Id);
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
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);

        f.Controller.BeginReplay();
        Assert.True(f.Controller.IsReplayMode);
        f.Pacer.DrainAll();
        Assert.False(f.Controller.IsReplayMode);
    }

    // --- Instant replay --------------------------------------------------

    /// <summary>
    /// Builds a small recorded game then returns the fixture (already
    /// in instant-replay mode) plus the live final snapshot, so the
    /// instant tests share one recording. Five+ beats so the
    /// per-beat-refresh assertion is meaningful.
    /// </summary>
    private static (Fixture F, GameStateSnapshot Live, int Beats) RecordedInstantGame()
    {
        var f = new Fixture(instantReplay: true);
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!);   // buy beat
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();              // Red end
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();              // Blue end → Red T2
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();              // Red end T2
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();              // Blue end T2 → Red T3
        GameStateSnapshot live = GameStateSnapshot.Capture(
            f.State.Grid, f.State.Treasury, f.State.Territories);
        Assert.True(f.Controller.ReplayBeats.Count >= 4,
            $"Expected >=4 beats, got {f.Controller.ReplayBeats.Count}");
        return (f, live, f.Controller.ReplayBeats.Count);
    }

    [Fact]
    public void Replay_Instant_SetsSilentModeDuringPlaybackAndClearsAfter()
    {
        (Fixture f, _, _) = RecordedInstantGame();

        f.Controller.BeginReplay();
        // Silent mode must be on the moment instant replay begins, so
        // the very first queued tick already runs with sound/VFX off.
        Assert.True(f.Map.SilentMode);

        f.Pacer.DrainAll();
        // And cleared once playback finishes so the final game-over
        // board renders normally.
        Assert.False(f.Map.SilentMode);
    }

    [Fact]
    public void Replay_Instant_RefreshesPerTurn_NotPerBeat_AndReachesSameFinalState()
    {
        (Fixture f, GameStateSnapshot live, int beats) = RecordedInstantGame();
        int endTurns = f.Controller.ReplayBeats.Count(b => b is ReplayEndTurnBeat);

        f.Controller.BeginReplay();
        int refreshesBefore = f.Map.RefreshOccupantCount;
        f.Pacer.DrainAll();
        int refreshDelta = f.Map.RefreshOccupantCount - refreshesBefore;

        // Instant replay repaints once per turn boundary plus one final
        // refresh at EndReplay — O(turns), never once per action beat.
        Assert.True(refreshDelta <= endTurns + 1,
            $"Expected ≈one refresh per turn (≤{endTurns + 1}) for {beats} "
            + $"beats, got {refreshDelta}");
        // Fidelity must still match the live game.
        f.AssertStateMatches(live);
    }

    [Fact]
    public void Replay_Instant_DoesNotPlayPerActionSound()
    {
        (Fixture f, _, _) = RecordedInstantGame();

        f.Controller.BeginReplay();
        int unitPlacedBefore = f.Map.UnitPlacedSounds.Count;
        f.Pacer.DrainAll();

        // The recorded buy replays through ExecuteAiBuyUnit, which
        // normally fires UnitPlaced; instant replay must stay silent.
        Assert.Equal(unitPlacedBefore, f.Map.UnitPlacedSounds.Count);
    }

    [Fact]
    public void Replay_Instant_DoesNotPlayBankruptcySound()
    {
        // A Commander pre-placed on Red's 2-tile territory (upkeep 54 ≫
        // Red's seeded gold + income) guarantees Red's StartPlayerTurn
        // bankrupts on turn 2 — both during the live recording and again
        // when the recorded EndTurn beats replay it. Instant replay is a
        // silent fast-forward, so the bankruptcy bell must NOT play
        // during playback (it leaked through the old unconditional
        // Bankruptcy/GameWon silent-gate exemption).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(0, 1))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(1, 1))!.Owner = red.Id;
        // Pre-placed BEFORE StartGame so it rides in the initial replay
        // snapshot and the bankruptcy reproduces on playback.
        grid.Get(HexCoord.FromOffset(0, 1))!.Occupant = new Unit(red.Id, UnitLevel.Commander);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        session.ClaimVictoryPromptedHighestThreshold[red.Id] = 90;
        session.ClaimVictoryPromptedHighestThreshold[blue.Id] = 90;
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var pacer = new QueuedAiPacer();
        var controller = new GameController(
            state, session, map, hud,
            seed: 1,
            aiPacer: pacer,
            maxTurnNumber: 20,
            replayIsInstantMode: () => true);
        controller.StartGame();
        pacer.DrainAll();

        hud.ClickEndTurn(); pacer.DrainAll(); // Red ends T1
        hud.ClickEndTurn(); pacer.DrainAll(); // Blue ends T1 → Red T2 bankrupts
        hud.ClickEndTurn(); pacer.DrainAll(); // Red ends T2
        hud.ClickEndTurn(); pacer.DrainAll(); // Blue ends T2 → Red T3

        // The live bankruptcy fired (silent off during live recording).
        // By Red's T3 start the bankruptcy Grave has aged into a Tree
        // (grave → tree at the owner's next turn-start), so the
        // post-recording occupant proves the bankruptcy happened.
        Assert.Equal(1, map.BankruptcySoundCount);
        Assert.IsType<Tree>(state.Grid.Get(HexCoord.FromOffset(0, 1))!.Occupant);
        Assert.True(controller.ReplayBeats.Count(b => b is ReplayEndTurnBeat) >= 4);

        int bankruptcyBefore = map.BankruptcySoundCount;
        controller.BeginReplay();
        pacer.DrainAll();

        // Replay reproduced the bankruptcy (Commander → Grave → Tree, same
        // deterministic aging) but stayed silent.
        Assert.IsType<Tree>(state.Grid.Get(HexCoord.FromOffset(0, 1))!.Occupant);
        Assert.Equal(bankruptcyBefore, map.BankruptcySoundCount);
    }

    [Fact]
    public void Replay_Instant_RedrawsOncePerTurn_NotPerCapture()
    {
        // Custom grid: Blue AI has five recruits, each adjacent to a
        // lone Red outpost it captures in a single turn. Red also holds
        // a 2-tile capital territory so it isn't pre-eliminated. On
        // replay, each of the five capturing moves runs HandleCapture →
        // RebuildAfterTerritoryChange. Instant replay must coalesce the
        // structural redraw to once per TURN (the user-visible "draw the
        // screen once per turn"), NOT once per capture — otherwise a big
        // endgame turn re-tessellates the whole map dozens of times and
        // ends up slower than Fast.
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(11, 2, blue.Id);
        // Red 2-tile capital territory (keeps Red alive at start).
        grid.Get(HexCoord.FromOffset(10, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(10, 1))!.Owner = red.Id;
        // Four lone Red outposts at odd cols on row 0; a Blue recruit on
        // the even col immediately to the left of each captures it. Stop
        // at col 7 so no outpost touches Red's (10,*) capital, which
        // would defend it and make the recruit capture illegal.
        var captures = new List<(HexCoord From, HexCoord To)>();
        for (int i = 0; i < 4; i++)
        {
            int redCol = 2 * i + 1;
            int blueCol = 2 * i;
            HexCoord redTile = HexCoord.FromOffset(redCol, 0);
            HexCoord blueTile = HexCoord.FromOffset(blueCol, 0);
            grid.Get(redTile)!.Owner = red.Id;
            grid.Get(blueTile)!.Occupant = new Unit(blue.Id, UnitLevel.Recruit);
            captures.Add((blueTile, redTile));
        }

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();

        int moveIdx = 0;
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r)
        {
            if (c != blue.Id) return null;
            if (moveIdx >= captures.Count) return null;
            (HexCoord from, HexCoord to) = captures[moveIdx++];
            return new AiMoveAction(from, to);
        }

        var pacer = new QueuedAiPacer();
        var controller = new GameController(
            state, session, map, hud,
            seed: 1,
            aiChooser: Chooser,
            aiPacer: pacer,
            maxTurnNumber: 20,
            replayIsInstantMode: () => true);
        controller.StartGame();
        pacer.DrainAll();

        // Red (human) ends turn → Blue AI runs its four capturing moves
        // in one turn, then ends → back to Red T2.
        hud.ClickEndTurn();
        pacer.DrainAll();

        // Snapshot the live final state AFTER the recorded play so the
        // fidelity check compares replay's end state to the real one.
        GameStateSnapshot live = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);

        int captureBeats = controller.ReplayBeats.Count(b => b is ReplayMoveBeat);
        Assert.True(captureBeats >= 4,
            $"Fixture should record >=4 capturing move beats, got {captureBeats}");

        controller.BeginReplay();
        int rebuildBefore = map.RebuildCount;   // excludes BeginReplay's setup rebuild
        pacer.DrainAll();
        int rebuildDelta = map.RebuildCount - rebuildBefore;

        // The five captures all fall in Blue's single turn. Per-capture
        // rebuild ⇒ delta ≈ 5; per-turn sampled redraw ⇒ delta well
        // under the capture count (≈ one redraw per turn boundary).
        Assert.True(rebuildDelta < captureBeats,
            $"Instant replay rebuilt the map {rebuildDelta}× for {captureBeats} "
            + "captures — expected once per turn, not once per capture.");

        // Fidelity must still hold.
        var refTiles = new Dictionary<HexCoord, (PlayerId, string?)>();
        foreach ((HexCoord coord, PlayerId color, HexOccupant? occ, bool _) in live.EnumerateTiles())
            refTiles[coord] = (color, occ?.GetType().Name);
        foreach (HexTile t in state.Grid.Tiles)
        {
            (PlayerId color, string? occType) = refTiles[t.Coord];
            Assert.Equal(color, t.Owner);
            Assert.Equal(occType, t.Occupant?.GetType().Name);
        }
    }

    // --- Mid-flight replay speed switching ------------------------------

    /// <summary>
    /// Records the same multi-turn human game as
    /// <see cref="RecordedInstantGame"/> on the given fixture and returns
    /// the live final snapshot. Replay-speed is governed by the fixture's
    /// injected predicate, so the recording itself is speed-agnostic.
    /// </summary>
    private static GameStateSnapshot RecordMultiTurnGame(Fixture f)
    {
        f.Map.SimulateClick(f.State.Grid.Get(f.RedCapital)!);
        f.Hud.ClickBuyRecruit();
        f.Map.SimulateClick(f.State.Grid.Get(f.RedOther)!); // buy beat
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();            // Red end T1
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();            // Blue end T1 → Red T2
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();            // Red end T2
        f.Hud.ClickEndTurn(); f.Pacer.DrainAll();            // Blue end T2 → Red T3
        Assert.True(f.Controller.ReplayBeats.Count >= 4,
            $"Expected >=4 beats, got {f.Controller.ReplayBeats.Count}");
        return GameStateSnapshot.Capture(f.State.Grid, f.State.Treasury, f.State.Territories);
    }

    [Fact]
    public void Speed_PacedToInstant_MidReplay_GoesSilentAndReachesSameState()
    {
        bool instantFlag = false;
        var f = new Fixture(replayInstantMode: () => instantFlag);
        GameStateSnapshot live = RecordMultiTurnGame(f);

        f.Controller.BeginReplay(); // paced
        Assert.False(f.Map.SilentMode);

        // Step the first beat's PREVIEW so a (non-end-turn) acting
        // territory is highlighted, then switch to Instant before its
        // execute — the switch must clear that lingering highlight.
        f.Pacer.StepOne(); // StepReplayPreview: highlights the acting territory
        Assert.NotNull(f.Map.LastHighlight);
        Assert.False(f.Map.SilentMode);

        instantFlag = true;
        int guard = 0;
        while (!f.Map.SilentMode && f.Pacer.HasPending && guard++ < 40)
            f.Pacer.StepOne();
        Assert.True(f.Map.SilentMode,
            "switching Replay Speed to Instant mid-replay should silence the view");
        Assert.Null(f.Map.LastHighlight); // paced acting-territory highlight cleared on switch to Instant

        f.Pacer.DrainAll();
        Assert.False(f.Map.SilentMode); // lifted at end of replay
        f.AssertStateMatches(live);
    }

    [Fact]
    public void Speed_InstantToPaced_MidReplay_SwitchesTrackAndLiftsSilent()
    {
        bool instantFlag = true;
        var f = new Fixture(replayInstantMode: () => instantFlag);
        GameStateSnapshot live = RecordMultiTurnGame(f);

        f.Controller.BeginReplay(); // instant
        Assert.True(f.Map.SilentMode);

        instantFlag = false;  // user switches to a paced speed
        f.Pacer.StepOne();    // instant tick runs T1 beats → boundary reschedule picks paced
        Assert.False(f.Map.SilentMode,
            "switching Replay Speed off Instant mid-replay should lift silent mode");

        f.Pacer.DrainAll();
        Assert.False(f.Map.SilentMode);
        f.AssertStateMatches(live);
    }

    [Fact]
    public void Replay_HumanRepositionThenMoveSameUnit_DoesNotThrow()
    {
        // A human may reposition a unit onto an own-empty tile and then
        // move it again the same turn (ExecuteMove never consumes the
        // move). ExecuteAiMove DOES consume it (an AI-loop selection
        // shim). Replaying the recorded human moves through
        // ExecuteAiMove must NOT apply that shim, or the second move
        // throws "already moved this turn" — the about_to_win desync
        // (beat #992).
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var players = new List<Player> { red, blue };
        // 5x2: row 0 all Red (capital lands lex-min (0,0)), row 1 Blue.
        var grid = TestHelpers.BuildRectGrid(5, 2, blue.Id);
        for (int x = 0; x < 5; x++)
            grid.Get(HexCoord.FromOffset(x, 0))!.Owner = red.Id;
        HexCoord a = HexCoord.FromOffset(2, 0);
        HexCoord b = HexCoord.FromOffset(3, 0);
        HexCoord cc = HexCoord.FromOffset(4, 0);
        grid.Get(a)!.Occupant = new Unit(red.Id);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        var session = new SessionState();
        var map = new MockHexMapView();
        var hud = new MockHudView();
        var pacer = new QueuedAiPacer();
        var controller = new GameController(
            state, session, map, hud, seed: 1, aiPacer: pacer, maxTurnNumber: 20);
        controller.StartGame();
        pacer.DrainAll();

        HexCoord cap = state.Territories.First(t => t.Owner == red.Id).Capital!.Value;
        Assert.DoesNotContain(cap, new[] { a, b, cc });

        // Human: select unit@a → reposition a→b; reselect@b → move b→cc.
        map.SimulateClick(grid.Get(a)!);
        map.SimulateClick(grid.Get(b)!);
        map.SimulateClick(grid.Get(b)!);
        map.SimulateClick(grid.Get(cc)!);

        Assert.Equal(2, controller.ReplayBeats.Count(x => x is ReplayMoveBeat));
        Assert.IsType<Unit>(grid.Get(cc)!.Occupant);

        GameStateSnapshot live = GameStateSnapshot.Capture(
            state.Grid, state.Treasury, state.Territories);

        controller.BeginReplay();
        pacer.DrainAll(); // must not throw "already moved this turn"

        var refTiles = new Dictionary<HexCoord, (PlayerId, string?)>();
        foreach ((HexCoord coord, PlayerId color, HexOccupant? occ, bool _) in live.EnumerateTiles())
            refTiles[coord] = (color, occ?.GetType().Name);
        foreach (HexTile t in state.Grid.Tiles)
        {
            (PlayerId color, string? occType) = refTiles[t.Coord];
            Assert.Equal(color, t.Owner);
            Assert.Equal(occType, t.Occupant?.GetType().Name);
        }
    }
}
