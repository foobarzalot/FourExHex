// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

/// <summary>One per-turn observation of a playtest game: the land count
/// per player slot at that turn.</summary>
public sealed record PlaytestSnapshot(int Turn, IReadOnlyList<int> Land);

/// <summary>Outcome + fun-proxy metrics of one playtest game.
/// <see cref="WinnerSlot"/> is -1 when the game hit the turn cap
/// unresolved; <see cref="DecidedTurn"/>/<see cref="ClosenessPercent"/>
/// are -1 when not computable.</summary>
public sealed record PlaytestGameResult(
    int Seed,
    int WinnerSlot,
    int FinalTurn,
    int LeadChanges,
    int DecidedTurn,
    int ClosenessPercent,
    IReadOnlyDictionary<int, int> EliminationTurns);

/// <summary>Aggregated result of a playtest batch.</summary>
public sealed class PlaytestReport
{
    public IReadOnlyList<PlaytestGameResult> Games { get; }
    public PlaytestReport(IReadOnlyList<PlaytestGameResult> games) => Games = games;

    public string Format()
    {
        var sb = new StringBuilder();
        sb.Append($"playtest — games: {Games.Count}");
        if (Games.Count > 0)
            sb.Append($", seeds {Games[0].Seed}..{Games[^1].Seed}");
        sb.Append('\n');
        if (Games.Count == 0) return sb.ToString();

        var winnerCounts = new SortedDictionary<int, int>();
        foreach (PlaytestGameResult g in Games)
        {
            winnerCounts.TryGetValue(g.WinnerSlot, out int n);
            winnerCounts[g.WinnerSlot] = n + 1;
        }
        sb.Append("winners: ");
        sb.Append(string.Join(", ", winnerCounts.Select(kv =>
        {
            string who = kv.Key < 0
                ? "unresolved"
                : $"slot {kv.Key} ({GameSettings.PlayerConfig[kv.Key].Name})";
            return $"{who} x{kv.Value} ({kv.Value * 100 / Games.Count}%)";
        })));
        sb.Append('\n');

        AppendStat(sb, "length:", Games.Select(g => g.FinalTurn));
        AppendStat(sb, "lead changes:", Games.Select(g => g.LeadChanges));

        List<PlaytestGameResult> resolved = Games.Where(g => g.WinnerSlot >= 0).ToList();
        if (resolved.Count > 0)
        {
            AppendStat(sb, "decided turn:", resolved.Select(g => g.DecidedTurn));
            AppendStat(sb, "closeness % (runner-up/winner land at end):",
                resolved.Select(g => g.ClosenessPercent));
        }

        var elimBySlot = new SortedDictionary<int, List<int>>();
        foreach (PlaytestGameResult g in Games)
            foreach ((int slot, int turn) in g.EliminationTurns)
            {
                if (!elimBySlot.TryGetValue(slot, out List<int>? turns))
                    elimBySlot[slot] = turns = new List<int>();
                turns.Add(turn);
            }
        foreach ((int slot, List<int> turns) in elimBySlot)
        {
            sb.Append($"eliminated slot {slot}: {turns.Count}/{Games.Count} games, ");
            sb.Append($"median turn {Median(turns)}\n");
        }

        foreach (PlaytestGameResult g in Games)
        {
            string winner = g.WinnerSlot < 0 ? "unresolved" : $"slot {g.WinnerSlot}";
            string elims = string.Join(",", g.EliminationTurns
                .OrderBy(kv => kv.Value)
                .Select(kv => $"{kv.Key}@{kv.Value}"));
            sb.Append($"  seed={g.Seed} winner={winner} turns={g.FinalTurn} ");
            sb.Append($"decided={g.DecidedTurn} leadchg={g.LeadChanges} ");
            sb.Append($"close={g.ClosenessPercent}% elims=[{elims}]\n");
        }
        return sb.ToString();
    }

    private static void AppendStat(StringBuilder sb, string label, IEnumerable<int> values)
    {
        List<int> sorted = values.OrderBy(v => v).ToList();
        sb.Append($"{label} min {sorted[0]}, median {Median(sorted)}, max {sorted[^1]}\n");
    }

    private static int Median(List<int> values)
    {
        List<int> sorted = values.OrderBy(v => v).ToList();
        return sorted[(sorted.Count - 1) / 2];
    }
}

/// <summary>Integer-only fun-proxy metrics over a playtest land series.</summary>
public static class PlaytestMetrics
{
    /// <summary>The slot with strictly the most land, or -1 on a tie
    /// (including all-zero).</summary>
    public static int LeaderOf(IReadOnlyList<int> land)
    {
        int best = -1, bestLand = 0;
        bool tied = true;
        for (int slot = 0; slot < land.Count; slot++)
        {
            if (land[slot] > bestLand)
            {
                bestLand = land[slot];
                best = slot;
                tied = false;
            }
            else if (land[slot] == bestLand && land[slot] > 0)
            {
                tied = true;
            }
        }
        return tied ? -1 : best;
    }

    /// <summary>Number of strict land-leader handoffs across the series;
    /// tie snapshots don't count as a leader and don't break a streak.</summary>
    public static int LeadChanges(IReadOnlyList<PlaytestSnapshot> series)
    {
        int changes = 0, lastLeader = -1;
        foreach (PlaytestSnapshot snap in series)
        {
            int leader = LeaderOf(snap.Land);
            if (leader < 0) continue;
            if (lastLeader >= 0 && leader != lastLeader) changes++;
            lastLeader = leader;
        }
        return changes;
    }

    /// <summary>The first turn of the winner's unbroken final land lead —
    /// how late the game was still contested. -1 when unresolved.</summary>
    public static int DecidedTurn(IReadOnlyList<PlaytestSnapshot> series, int winnerSlot)
    {
        if (winnerSlot < 0 || series.Count == 0) return -1;
        int lastContested = -1;
        for (int i = 0; i < series.Count; i++)
        {
            if (LeaderOf(series[i].Land) != winnerSlot) lastContested = i;
        }
        if (lastContested < 0) return series[0].Turn;
        if (lastContested == series.Count - 1) return series[^1].Turn;
        return series[lastContested + 1].Turn;
    }

    /// <summary>Runner-up land as a percent of winner land at game end
    /// (100 = neck-and-neck, 0 = wipeout). -1 when unresolved.</summary>
    public static int ClosenessPercent(IReadOnlyList<int> finalLand, int winnerSlot)
    {
        if (winnerSlot < 0 || winnerSlot >= finalLand.Count) return -1;
        int winnerLand = finalLand[winnerSlot];
        if (winnerLand <= 0) return -1;
        int runnerUp = 0;
        for (int slot = 0; slot < finalLand.Count; slot++)
        {
            if (slot == winnerSlot) continue;
            if (finalLand[slot] > runnerUp) runnerUp = finalLand[slot];
        }
        return runnerUp * 100 / winnerLand;
    }
}

/// <summary>
/// Headless playtest runner: plays a starting map N times as all-AI
/// games, entirely in-process (the DeterminismProbe pattern — all slots
/// forced to Computer, <see cref="SynchronousAiPacer"/>, one
/// <c>StartGame()</c> call per game), and reports balance + fun-proxy
/// metrics. Each game re-deserializes the map JSON so runs never share
/// mutable state; the fresh-game rebuild mirrors Main's starting-map
/// branch (new TurnState + empty Treasury, authored mode carried over).
/// </summary>
public static class LevelPlaytest
{
    public static PlaytestReport Run(
        string mapJson, int games, int baseSeed, int maxTurnNumber = 500)
    {
        var results = new List<PlaytestGameResult>(games);
        for (int i = 0; i < games; i++)
        {
            int seed = baseSeed + i;
            results.Add(RunOne(mapJson, seed, maxTurnNumber));
        }
        return new PlaytestReport(results);
    }

    private static PlaytestGameResult RunOne(string mapJson, int seed, int maxTurnNumber)
    {
        LoadedSave loaded = SaveSerializer.Deserialize(mapJson);

        // Same substitution the campaign winner sweep uses: every active
        // slot plays as Computer (baked difficulty kept). Kind is never
        // read by map/capital logic, so this can't perturb the board.
        var players = new List<Player>(loaded.Players.Count);
        foreach (Player p in loaded.Players)
            players.Add(new Player(p.Name, p.Id, PlayerKind.Computer, p.Difficulty));

        var state = new GameState(
            loaded.State.Grid,
            loaded.State.Territories,
            players,
            new TurnState(players),
            new Treasury(),
            loaded.State.WaterCoords,
            loaded.State.Mode);

        var session = new SessionState();
        var hud = new PlaytestMetricsHud(players);
        var controller = new GameController(
            state, session,
            new PlaytestNullMapView(), hud,
            seed: seed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: maxTurnNumber);
        controller.StartGame();

        int winner = session.Winner?.Index ?? -1;
        IReadOnlyList<PlaytestSnapshot> series = hud.Series;
        var result = new PlaytestGameResult(
            seed,
            winner,
            state.Turns.TurnNumber,
            PlaytestMetrics.LeadChanges(series),
            PlaytestMetrics.DecidedTurn(series, winner),
            series.Count > 0
                ? PlaytestMetrics.ClosenessPercent(series[^1].Land, winner)
                : -1,
            hud.EliminationTurns);
        Log.Info(Log.LogCategory.LevelDesign,
            $"[level] playtest seed={seed} winner={winner} turns={result.FinalTurn} "
            + $"decided={result.DecidedTurn} leadchg={result.LeadChanges}");
        return result;
    }
}

/// <summary>
/// Recording <see cref="IHudView"/>: the controller calls Refresh at
/// every turn transition, which is exactly the per-turn sampling hook
/// the metrics need. Everything else is a no-op sink.
/// </summary>
internal sealed class PlaytestMetricsHud : IHudView
{
#pragma warning disable 67 // never raised — the playtest has no human input
    public event Action? BuyRecruitClicked;
    public event Action<UnitLevel>? BuyUnitClicked;
    public event Action? BuildTowerClicked;
    public event Action? UndoLastClicked;
    public event Action? UndoTurnClicked;
    public event Action? RedoLastClicked;
    public event Action? RedoAllClicked;
    public event Action? EndTurnClicked;
    public event Action? NewGameClicked;
    public event Action? MainMenuClicked;
    public event Action? NextTerritoryClicked;
    public event Action? PreviousTerritoryClicked;
    public event Action? NextUnitClicked;
    public event Action? NextUnitTierClicked;
    public event Action? PreviousUnitClicked;
    public event Action? CancelActionPressed;
    public event Action? AutomateClicked;
    public event Action? DefeatContinueClicked;
    public event Action? ClaimVictoryWinNowClicked;
    public event Action? ClaimVictoryContinueClicked;
    public event Action? ReplayClicked;
    public event Action? TutorialMessageTapped;
#pragma warning restore 67

    private readonly IReadOnlyList<Player> _players;
    private readonly SortedDictionary<int, int[]> _landByTurn = new();
    private readonly Dictionary<int, int> _eliminationTurns = new();

    public PlaytestMetricsHud(IReadOnlyList<Player> players) => _players = players;

    public IReadOnlyList<PlaytestSnapshot> Series =>
        _landByTurn.Select(kv => new PlaytestSnapshot(kv.Key, kv.Value)).ToList();

    public IReadOnlyDictionary<int, int> EliminationTurns => _eliminationTurns;

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining)
    {
        int[] land = new int[GameSettings.PlayerConfig.Length];
        foreach (HexTile tile in state.Grid.Tiles)
        {
            if (tile.Owner != PlayerId.None) land[tile.Owner.Index]++;
        }
        // Last refresh of each turn wins — that's the settled state.
        _landByTurn[state.Turns.TurnNumber] = land;

        foreach (Player p in _players)
        {
            if (_eliminationTurns.ContainsKey(p.Id.Index)) continue;
            if (WinConditionRules.IsEliminated(p.Id, state.Grid))
                _eliminationTurns[p.Id.Index] = state.Turns.TurnNumber;
        }
    }

    public void SetMapLabel(string text) { }
    public void ShowTransientBanner(string text) { }
    public void ShowTutorialMessage(string text) { }
    public void ShowTappableTutorialMessage(string text) { }
    public void HideTutorialMessage() { }
    public void SetCta(CtaButton button, bool isCta, bool pulse = true) { }
    public void SetUndoRedoLocked(bool locked) { }
    public void SetVictoryOverlaySuppressed(bool suppressed) { }
    public void SetEndgameOverlaysHeld(bool held) { }
    public HexCoord? SummonedCapitalAlertCoord => null;
    public void SummonCapitalAlertNotice(HexCoord capital, EconomyOutlook outlook) { }
    public void DismissCapitalAlertNotice() { }
    public void SetReplayAvailable(bool available) { }
    public void SetAutomateState(bool enabled, bool running, bool visible) { }
}

/// <summary>No-op <see cref="IHexMapView"/> sink for playtest games.</summary>
internal sealed class PlaytestNullMapView : IHexMapView
{
#pragma warning disable 67 // never raised — the playtest has no human input
    public event Action<HexTile?>? TileClicked;
    public event Action<HexTile?>? TileLongClicked;
    public event Action<HexCoord>? OffGridClicked;
#pragma warning restore 67

    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level) { }
    public void ShowTowerTargets(IEnumerable<HexCoord> coords) { }
    public void ShowTowerCoverage(IEnumerable<HexCoord> coords) { }
    public void ShowTideForecast(IEnumerable<TideStep> steps) { }
    public void ShowSeaVikings(
        IReadOnlyList<SeaViking> atSea, IReadOnlyList<HexCoord> seaGraves) { }
    public void ShowFog(FogView? fog) { }
    public void ShowMoveSource(HexCoord? coord) { }
    public void ShowSelectUnitCue(HexCoord? coord) { }
    public void ShowHighlight(Territory? selected) { }
    public void CenterOnTerritory(Territory territory) { }
    public void CenterOnCoord(HexCoord coord) { }
    public void ShowTerrainFocusPulse(HexCoord? coord) { }
    public void RebuildAfterTerritoryChange() { }
    public void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury,
        IReadOnlySet<HexCoord> visitedCapitals) { }
    public void SetSilentMode(bool silent) { }
    public void AnimateUnitMove(HexCoord from, HexCoord to) { }
    public void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed) { }
    public void PlayTerrainCaptureEffect(HexCoord coord, TerrainFeature terrain) { }
    public void PlaySound(SoundEffect kind, HexCoord? at = null) { }
    public void FlashRejection(
        HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders) { }
}
