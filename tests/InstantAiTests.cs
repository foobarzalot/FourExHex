using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Live-AI "Instant" speed: the user-visible 1:1 of instant replay for
/// AI opponents' turns. Same chunked frame-yielded driver
/// (<c>InstantAiTick</c> → <c>RunInstantTick</c>), so these assertions
/// mirror the instant-replay ones in <see cref="ReplayPlaybackTests"/>
/// — per-turn-sampled redraw, silent, responsive — with the one
/// deliberate difference that the "Opponents are taking their turns…"
/// overlay stays for live play.
///
/// Instant is selected via the injected <c>aiSilentMode</c> predicate
/// (Main wires it to <c>UserSettings.AiSpeed == PlaybackSpeed.Instant
/// &amp;&amp; !IsReplayMode</c>); a <see cref="QueuedAiPacer"/> lets the
/// chunked driver be drained deterministically via <c>DrainAll()</c>.
/// </summary>
public class InstantAiTests
{
    // 5x1 line: Red (human) capital {(3,0),(4,0)}; Blue (AI) holds
    // {(0,0),(1,0),(2,0)} with a Spearman at (2,0). A scripted chooser
    // can send that Spearman into Red's capital to eliminate Red.
    private static (GameState State, SessionState Session, MockHexMapView Map,
        MockHudView Hud, Player Red, Player Blue) BuildKillScenario()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(5, 1, PlayerId.None);
        grid.Get(HexCoord.FromOffset(0, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(1, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Owner = blue.Id;
        grid.Get(HexCoord.FromOffset(3, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(4, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(2, 0))!.Occupant = new Unit(blue.Id, UnitLevel.Spearman);

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        return (state, new SessionState(), new MockHexMapView(), new MockHudView(), red, blue);
    }

    // 11x2: Blue AI; four lone Red outposts at odd cols on row 0, each
    // with a Blue peasant immediately left that captures it — all four
    // captures fall in Blue's single turn. Red also keeps a 2-tile
    // capital at (10,*) so it isn't pre-eliminated. Mirrors the
    // ReplayPlaybackTests per-capture fixture so the same per-turn
    // coalescing assertion applies to the live path.
    private static (GameState State, SessionState Session, MockHexMapView Map,
        MockHudView Hud, Player Red, Player Blue, List<(HexCoord From, HexCoord To)> Captures)
        BuildMultiCaptureScenario()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var players = new List<Player> { red, blue };

        HexGrid grid = TestHelpers.BuildRectGrid(11, 2, blue.Id);
        grid.Get(HexCoord.FromOffset(10, 0))!.Owner = red.Id;
        grid.Get(HexCoord.FromOffset(10, 1))!.Owner = red.Id;

        var captures = new List<(HexCoord From, HexCoord To)>();
        for (int i = 0; i < 4; i++)
        {
            HexCoord redTile = HexCoord.FromOffset(2 * i + 1, 0);
            HexCoord blueTile = HexCoord.FromOffset(2 * i, 0);
            grid.Get(redTile)!.Owner = red.Id;
            grid.Get(blueTile)!.Occupant = new Unit(blue.Id, UnitLevel.Peasant);
            captures.Add((blueTile, redTile));
        }

        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players, new TurnState(players), new Treasury());
        return (state, new SessionState(), new MockHexMapView(), new MockHudView(),
            red, blue, captures);
    }

    private static Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?>
        SequencedCaptureChooser(PlayerId aiColor, List<(HexCoord From, HexCoord To)> captures)
    {
        int idx = 0;
        return (s, c, v, r) =>
        {
            if (c != aiColor || idx >= captures.Count) return null;
            (HexCoord from, HexCoord to) = captures[idx++];
            return new AiMoveAction(from, to);
        };
    }

    private static GameController NewController(
        GameState state, SessionState session, MockHexMapView map, MockHudView hud,
        QueuedAiPacer pacer,
        Func<GameState, PlayerId, HashSet<HexCoord>, Random, AiAction?>? chooser,
        bool instant)
    {
        return new GameController(
            state, session, map, hud,
            seed: 1,
            aiChooser: chooser,
            aiPacer: pacer,
            maxTurnNumber: 20,
            aiSilentMode: instant ? () => true : (Func<bool>?)null);
    }

    private static List<string> BeatSignatures(IReadOnlyList<ReplayBeat> beats) =>
        beats.Select(b => b switch
        {
            ReplayMoveBeat m => $"M {m.From} {m.To}",
            ReplayBuyBeat by => $"B {by.Capital} {by.To} {by.Level}",
            ReplayBuildTowerBeat t => $"T {t.Capital} {t.To}",
            ReplayEndTurnBeat => "ET",
            _ => b.GetType().Name,
        }).ToList();

    private static void AssertSameBoard(GameStateSnapshot reference, GameState live)
    {
        var refTiles = new Dictionary<HexCoord, (PlayerId Owner, string? Occ)>();
        foreach ((HexCoord coord, PlayerId owner, HexOccupant? occ) in reference.EnumerateTiles())
            refTiles[coord] = (owner, occ?.GetType().Name);
        foreach (HexTile t in live.Grid.Tiles)
        {
            (PlayerId color, string? occ) = refTiles[t.Coord];
            Assert.Equal(color, t.Owner);
            Assert.Equal(occ, t.Occupant?.GetType().Name);
        }
    }

    // --- Silent + overlay lifecycle -------------------------------------

    [Fact]
    public void InstantAi_SetsSilentDuringBatch_ClearsOnHandBackToHuman()
    {
        var (state, session, map, hud, _, _) = BuildKillScenario();
        // Passive AI: ends its turn immediately, handing back to human.
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r) => null;
        var pacer = new QueuedAiPacer();
        var c = NewController(state, session, map, hud, pacer, Chooser, instant: true);
        c.StartGame();
        pacer.DrainAll();
        Assert.False(map.SilentMode); // Red (human) acting

        hud.ClickEndTurn();
        // Blue (AI) batch is queued on the pacer — silent armed before
        // the first tick so no SFX/VFX leak.
        Assert.True(map.SilentMode);

        pacer.DrainAll();
        // AI turn drained, control back to Red — silent lifted.
        Assert.False(map.SilentMode);
    }

    [Fact]
    public void InstantAi_ShowsOpponentsOverlay_DuringBatch_ClearedOnHandBack()
    {
        var (state, session, map, hud, _, _) = BuildKillScenario();
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r) => null;
        var pacer = new QueuedAiPacer();
        var c = NewController(state, session, map, hud, pacer, Chooser, instant: true);
        c.StartGame();
        pacer.DrainAll();
        Assert.Null(hud.CurrentTutorialMessage);

        hud.ClickEndTurn();
        // The one deliberate difference from instant replay: live AI
        // keeps the working indicator.
        Assert.Equal("Opponents are taking their turns…", hud.CurrentTutorialMessage);

        pacer.DrainAll();
        Assert.Null(hud.CurrentTutorialMessage);
    }

    [Fact]
    public void InstantAi_IgnoresHumanInput_WhileBatchPending()
    {
        var (state, session, map, hud, _, _) = BuildKillScenario();
        // Always-move chooser: the batch never self-terminates, so it
        // stays parked on the pacer for the input assertion.
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r) =>
            new AiMoveAction(HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        var pacer = new QueuedAiPacer();
        var c = NewController(state, session, map, hud, pacer, Chooser, instant: true);
        c.StartGame();
        pacer.DrainAll();
        Assert.Null(session.SelectedTerritory);

        hud.ClickEndTurn();
        Assert.True(pacer.HasPending); // InstantAiTick queued, not drained

        // Race a Tab in while the AI batch is pending — must be inert.
        hud.PressNextTerritory();
        Assert.Null(session.SelectedTerritory);
    }

    // --- Per-turn sampling (1:1 with instant replay) --------------------

    [Fact]
    public void InstantAi_RedrawsOncePerTurn_NotPerCapture()
    {
        var (state, session, map, hud, _, blue, captures) = BuildMultiCaptureScenario();
        var pacer = new QueuedAiPacer();
        var c = NewController(state, session, map, hud, pacer,
            SequencedCaptureChooser(blue.Id, captures), instant: true);
        c.StartGame();
        pacer.DrainAll(); // stable on Red T1 (Red is human → nothing queued)

        int rebuildBefore = map.RebuildCount;
        hud.ClickEndTurn();   // Red ends → Blue AI runs all 4 captures in one turn
        pacer.DrainAll();
        int rebuildDelta = map.RebuildCount - rebuildBefore;

        int captureBeats = c.ReplayBeats.Count(b => b is ReplayMoveBeat);
        Assert.True(captureBeats >= 4, $"expected >=4 capture beats, got {captureBeats}");
        // Per-capture rebuild ⇒ delta ≈ 4; per-turn-sampled redraw ⇒
        // delta well under the capture count (suppressed mid-turn, one
        // structural rebuild at batch end).
        Assert.True(rebuildDelta < captureBeats,
            $"Instant AI rebuilt the map {rebuildDelta}× for {captureBeats} "
            + "captures — expected per-turn, not per-capture.");
        Assert.False(map.SilentMode); // lifted on hand-back to Red
    }

    [Fact]
    public void InstantAi_RefreshesPerTurn_NotPerAction()
    {
        var (state, session, map, hud, _, blue, captures) = BuildMultiCaptureScenario();
        var pacer = new QueuedAiPacer();
        var c = NewController(state, session, map, hud, pacer,
            SequencedCaptureChooser(blue.Id, captures), instant: true);
        c.StartGame();
        pacer.DrainAll();

        int refreshBefore = map.RefreshOccupantCount;
        hud.ClickEndTurn();
        pacer.DrainAll();
        int refreshDelta = map.RefreshOccupantCount - refreshBefore;

        int actionBeats = c.ReplayBeats.Count(b => b is ReplayMoveBeat);
        // O(turns), not O(actions): one AI turn here, so a couple of
        // refreshes at most — never one per capturing move.
        Assert.True(refreshDelta < actionBeats,
            $"Expected ≈one refresh for the AI turn, got {refreshDelta} "
            + $"for {actionBeats} action beats.");
    }

    // --- No drift vs the paced step machine -----------------------------

    [Fact]
    public void InstantAi_SameBeatsAndFinalStateAsPaced()
    {
        // The whole point of the shared cores (ApplyAiActionCore /
        // EndCurrentAiPlayerTurnCore): instant and paced must produce
        // an identical recorded beat stream and identical final board.
        List<string> Run(bool instant, out GameStateSnapshot snap)
        {
            var (state, session, map, hud, _, blue, captures) = BuildMultiCaptureScenario();
            var pacer = new QueuedAiPacer();
            var c = NewController(state, session, map, hud, pacer,
                SequencedCaptureChooser(blue.Id, captures), instant);
            c.StartGame();
            pacer.DrainAll();
            hud.ClickEndTurn();
            pacer.DrainAll();
            snap = GameStateSnapshot.Capture(state.Grid, state.Treasury, state.Territories);
            return BeatSignatures(c.ReplayBeats);
        }

        List<string> paced = Run(instant: false, out GameStateSnapshot pacedSnap);
        List<string> fast = Run(instant: true, out GameStateSnapshot instantSnap);

        Assert.Equal(paced, fast);
        // And the boards converge too (compare instant's final state to
        // the paced reference snapshot).
        var (s2, ss2, m2, h2, _, b2, caps2) = BuildMultiCaptureScenario();
        var p2 = new QueuedAiPacer();
        var c2 = NewController(s2, ss2, m2, h2, p2,
            SequencedCaptureChooser(b2.Id, caps2), instant: true);
        c2.StartGame();
        p2.DrainAll();
        h2.ClickEndTurn();
        p2.DrainAll();
        AssertSameBoard(pacedSnap, s2);
        Assert.Equal(
            pacedSnap.EnumerateTiles().Count(),
            instantSnap.EnumerateTiles().Count());
    }

    // --- Mid-batch defeat overlay pause + resume ------------------------

    [Fact]
    public void InstantAi_AiKillsHuman_ShowsDefeatOverlay_AllowsDismiss()
    {
        var (state, session, map, hud, red, _) = BuildKillScenario();
        AiAction? scripted = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        AiAction? Chooser(GameState s, PlayerId c, HashSet<HexCoord> v, Random r)
        {
            AiAction? next = scripted;
            scripted = null;
            return next;
        }
        var pacer = new QueuedAiPacer();
        var c = NewController(state, session, map, hud, pacer, Chooser, instant: true);
        c.StartGame();
        pacer.DrainAll();

        hud.ClickEndTurn();
        pacer.DrainAll();

        // Blue's capture eliminated Red mid-batch: the driver stopped,
        // silent lifted, defeat overlay visible.
        Assert.Equal(red.Id, session.PendingDefeatScreen);
        Assert.False(map.SilentMode);

        int refreshesBefore = hud.RefreshCount;
        hud.ClickDefeatContinue();
        Assert.Null(session.PendingDefeatScreen);
        Assert.True(hud.RefreshCount > refreshesBefore);
    }
}
