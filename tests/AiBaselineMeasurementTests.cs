using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FourExHex.Tests;

/// <summary>
/// AI behavior measurement harness (#138 baseline): plays full-size
/// (30×20, cap 500) all-AI freeform games — the plain FOUREXHEX_6AI
/// configuration — and reports, per game and aggregated:
///   - towers built per player (executed AiBuildTowerAction count),
///   - towers still standing at game end (undercounts lifetime builds
///     when a tower is destroyed),
///   - the executed-action mix by AiAction subtype,
///   - the phase / kind distribution from ComputerAi's [chose] line,
///   - win/loss correlation: each action is stamped with the actor's
///     tile-count rank among living players at the moment of the
///     choice, so tower builds can be compared against the all-action
///     base rate (are towers a losing player's move?), alongside the
///     eventual winner's share of tower builds vs of all actions.
///
/// Runtime is deliberately NOT measured here — games run under
/// Parallel.For, which distorts wall-clock; the canonical [ai-prof]
/// baseline comes from a single seeded FOUREXHEX_6AI headless run.
///
/// Env-gated so plain <c>dotnet test</c> stays fast:
///   FOUREXHEX_AI_MEASURE=1        — run the measurement
///   FOUREXHEX_MEASURE_SEEDS=a-b   — optional seed range (default 101-110)
///   FOUREXHEX_MEASURE_OUT=path    — optional summary path
///                                   (default %TMP%/ai-baseline-138.txt)
/// Invoke (filtered — the harness redirects the global Log.Sink and Ai
/// level for the duration, so don't run it alongside the full suite):
///   FOUREXHEX_AI_MEASURE=1 dotnet test --filter FullyQualifiedName~AiBaselineMeasurement
/// </summary>
public class AiBaselineMeasurementTests
{
    private const int Cols = 30;
    private const int Rows = 20;
    private const int MaxTurns = 500;
    private const int PlayerCount = 6;

    private readonly ITestOutputHelper _output;

    public AiBaselineMeasurementTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>One executed tower build: who, their tile-count rank
    /// among living players when they chose it (1 = leader), how many
    /// players were alive, and the turn number.</summary>
    private sealed record TowerBuild(int Slot, int Rank, int Alive, int Turn);

    private sealed record GameResult(
        int Seed, int WinnerSlot, int FinalTurn,
        int[] TowersBuilt, int[] TowersStanding,
        int[] ActionsBySlot, int[] ActionsByRank,
        Dictionary<string, int> ActionsByType,
        List<TowerBuild> TowerBuilds);

    [Fact]
    public void AiBaselineMeasurement_TowerAndPhaseDistribution()
    {
        if (Environment.GetEnvironmentVariable("FOUREXHEX_AI_MEASURE") is null)
        {
            _output.WriteLine("Measurement skipped: FOUREXHEX_AI_MEASURE not set.");
            return;
        }

        (int lo, int hi) = ParseRange(
            Environment.GetEnvironmentVariable("FOUREXHEX_MEASURE_SEEDS"), 101, 110);
        string outPath = Environment.GetEnvironmentVariable("FOUREXHEX_MEASURE_OUT")
            ?? Path.Combine(Path.GetTempPath(), "ai-baseline-138.txt");

        // Phase/kind tallies come from ComputerAi's "[chose] {player}
        // phase={p} kind={k} {action} delta={d}" Debug line, captured via
        // the global Log sink. Aggregate-only: games run in parallel, so
        // per-game attribution of sink lines isn't possible (or needed).
        var phaseCounts = new ConcurrentDictionary<string, int>();
        var kindCounts = new ConcurrentDictionary<string, int>();
        Action<string>? previousSink = Log.Sink;
        Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Debug);
        Log.Sink = line =>
        {
            if (!line.StartsWith("[chose] ", StringComparison.Ordinal)) return;
            string? phase = Token(line, "phase=");
            string? kind = Token(line, "kind=");
            if (phase != null) phaseCounts.AddOrUpdate(phase, 1, (_, n) => n + 1);
            if (kind != null) kindCounts.AddOrUpdate(kind, 1, (_, n) => n + 1);
        };

        try
        {
            int total = hi - lo + 1;
            var results = new GameResult[total];
            long startTicks = Environment.TickCount64;
            Parallel.For(lo, hi + 1, seed => results[seed - lo] = RunGame(seed));
            long elapsedSeconds = (Environment.TickCount64 - startTicks) / 1000;

            string summary = BuildSummary(
                results, phaseCounts, kindCounts, lo, hi, elapsedSeconds);
            File.WriteAllText(outPath, summary);
            _output.WriteLine($"Summary: {outPath}");
            _output.WriteLine(summary);
        }
        finally
        {
            Log.Sink = previousSink;
            Log.SetLevel(Log.LogCategory.Ai, Log.LogLevel.Off);
        }
    }

    /// <summary>
    /// One all-AI freeform game matching a plain FOUREXHEX_6AI launch:
    /// six Soldier Computers, GameSettings-default densities (trees 5,
    /// mountains/gold/clumping 0). The aiChooser is wrapped to tally
    /// every returned action's subtype per player before execution —
    /// zero effect on game state or determinism.
    /// </summary>
    private static GameResult RunGame(int seed)
    {
        var players = new List<Player>(PlayerCount);
        for (int i = 0; i < PlayerCount; i++)
        {
            players.Add(new Player(
                GameSettings.PlayerConfig[i].Name,
                PlayerId.FromIndex(i),
                PlayerKind.Computer,
                Difficulty.Soldier));
        }

        GameState state = ProceduralGame.Build(
            Cols, Rows, players, seed,
            new MapGenOptions(
                TreeDensity: 5, MountainDensity: 0,
                GoldDensity: 0, ClumpingFactor: 0),
            GameMode.Freeform);

        var towersBuilt = new int[PlayerCount];
        var actionsBySlot = new int[PlayerCount];
        var actionsByRank = new int[PlayerCount];
        var actionsByType = new Dictionary<string, int>();
        var towerBuilds = new List<TowerBuild>();
        AiAction? TallyingChooser(
            GameState s, PlayerId forPlayer, HashSet<HexCoord> visited, HashSet<HexCoord> repositionedUnits, Random rng)
        {
            AiAction? action = AiDispatcher.ChooseForCurrentPlayer(s, forPlayer, visited, repositionedUnits, rng);
            if (action != null)
            {
                string type = action.GetType().Name;
                actionsByType[type] = actionsByType.GetValueOrDefault(type) + 1;
                if (!forPlayer.IsNone)
                {
                    (int rank, int alive) = RankByTiles(s, forPlayer.Index);
                    actionsBySlot[forPlayer.Index]++;
                    actionsByRank[rank - 1]++;
                    if (action is AiBuildTowerAction)
                    {
                        towersBuilt[forPlayer.Index]++;
                        towerBuilds.Add(new TowerBuild(
                            forPlayer.Index, rank, alive, s.Turns.TurnNumber));
                    }
                }
            }
            return action;
        }

        var session = new SessionState();
        var controller = new GameController(state, session,
            new MockHexMapView(), new MockHudView(),
            seed: seed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: TallyingChooser,
            maxTurnNumber: MaxTurns);
        controller.StartGame();

        var towersStanding = new int[PlayerCount];
        foreach (HexTile tile in state.Grid.Tiles)
        {
            if (tile.Occupant is Tower && !tile.Owner.IsNone)
                towersStanding[tile.Owner.Index]++;
        }

        return new GameResult(
            seed, session.Winner?.Index ?? -1, state.Turns.TurnNumber,
            towersBuilt, towersStanding, actionsBySlot, actionsByRank,
            actionsByType, towerBuilds);
    }

    /// <summary>
    /// The actor's standing at the moment of an action choice: rank by
    /// owned-tile count (1 = leader; ties share the better rank) and
    /// the number of players still holding at least one tile. Dead
    /// players (zero tiles) can't outrank anyone, so rank is
    /// effectively 1..Alive.
    /// </summary>
    private static (int Rank, int Alive) RankByTiles(GameState state, int slot)
    {
        var tiles = new int[PlayerCount];
        foreach (HexTile tile in state.Grid.Tiles)
        {
            if (!tile.Owner.IsNone) tiles[tile.Owner.Index]++;
        }
        int rank = 1;
        int alive = 0;
        for (int p = 0; p < PlayerCount; p++)
        {
            if (tiles[p] > 0) alive++;
            if (p != slot && tiles[p] > tiles[slot]) rank++;
        }
        return (rank, alive);
    }

    private static string BuildSummary(
        GameResult[] results,
        ConcurrentDictionary<string, int> phaseCounts,
        ConcurrentDictionary<string, int> kindCounts,
        int lo, int hi, long elapsedSeconds)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== AI tower baseline (#138): {results.Length} game(s), " +
            $"{Cols}x{Rows}, cap {MaxTurns}, seeds {lo}-{hi}, " +
            $"6x Soldier Computer, {elapsedSeconds}s wall ===");

        sb.AppendLine("Per game (towers built / standing are per-slot 0..5):");
        sb.AppendLine("  seed  turn  winner      built              standing");
        foreach (GameResult r in results)
        {
            string winner = r.WinnerSlot >= 0
                ? GameSettings.PlayerConfig[r.WinnerSlot].Name
                : "(cap)";
            sb.AppendLine($"  {r.Seed,-5} {r.FinalTurn,-5} {winner,-11} " +
                $"[{string.Join(' ', r.TowersBuilt)}]  sum={r.TowersBuilt.Sum(),-3} " +
                $"[{string.Join(' ', r.TowersStanding)}]  sum={r.TowersStanding.Sum()}");
        }

        int gamesWithTower = results.Count(r => r.TowersBuilt.Sum() > 0);
        int totalTowers = results.Sum(r => r.TowersBuilt.Sum());
        int totalActions = results.Sum(r => r.ActionsByType.Values.Sum());
        sb.AppendLine($"Games with >=1 tower built: {gamesWithTower}/{results.Length}");
        sb.AppendLine($"Towers built total: {totalTowers} " +
            $"(mean per game: {(results.Length > 0 ? totalTowers * 100 / results.Length : 0) / 100.0:0.00})");

        sb.AppendLine($"Executed-action mix ({totalActions} actions):");
        var mergedTypes = new SortedDictionary<string, int>();
        foreach (GameResult r in results)
        {
            foreach (KeyValuePair<string, int> kvp in r.ActionsByType)
                mergedTypes[kvp.Key] = mergedTypes.GetValueOrDefault(kvp.Key) + kvp.Value;
        }
        foreach (KeyValuePair<string, int> kvp in mergedTypes)
        {
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}" +
                (totalActions > 0 ? $" ({kvp.Value * 1000 / totalActions / 10.0:0.0}%)" : ""));
        }

        AppendDistribution(sb, "Phase distribution ([chose] phase=):", phaseCounts);
        AppendDistribution(sb, "Kind distribution ([chose] kind=):", kindCounts);
        AppendWinCorrelation(sb, results);
        return sb.ToString();
    }

    /// <summary>
    /// Tower-vs-winning correlation. Two comparisons, each against its
    /// own base rate so "losing players simply act more/less" can't
    /// masquerade as a tower effect:
    ///   1. the eventual winner's share of tower builds vs their share
    ///      of all actions (games with a winner only);
    ///   2. the rank-at-choice histogram of tower builds vs all actions.
    /// </summary>
    private static void AppendWinCorrelation(StringBuilder sb, GameResult[] results)
    {
        int winnerActions = 0, allActions = 0, winnerTowers = 0, allTowers = 0;
        foreach (GameResult r in results)
        {
            if (r.WinnerSlot < 0) continue;
            allActions += r.ActionsBySlot.Sum();
            winnerActions += r.ActionsBySlot[r.WinnerSlot];
            allTowers += r.TowerBuilds.Count;
            winnerTowers += r.TowerBuilds.Count(b => b.Slot == r.WinnerSlot);
        }
        sb.AppendLine("Winner correlation (games with a winner):");
        sb.AppendLine($"  eventual winner's share of all actions:  " +
            $"{winnerActions}/{allActions}" + Pct(winnerActions, allActions));
        sb.AppendLine($"  eventual winner's share of tower builds: " +
            $"{winnerTowers}/{allTowers}" + Pct(winnerTowers, allTowers));

        var towerRank = new int[PlayerCount];
        var actionRank = new int[PlayerCount];
        foreach (GameResult r in results)
        {
            foreach (TowerBuild b in r.TowerBuilds) towerRank[b.Rank - 1]++;
            for (int p = 0; p < PlayerCount; p++) actionRank[p] += r.ActionsByRank[p];
        }
        sb.AppendLine("Rank at choice (1 = tile leader among living players):");
        sb.AppendLine("  rank      " + string.Join(" ", Enumerable.Range(1, PlayerCount)
            .Select(rk => $"{rk,8}")));
        sb.AppendLine("  actions   " + string.Join(" ", actionRank
            .Select(n => $"{Pct(n, actionRank.Sum()).Trim(),8}")));
        sb.AppendLine("  towers    " + string.Join(" ", towerRank
            .Select(n => $"{Pct(n, towerRank.Sum()).Trim(),8}")));

        // When in the game do towers go up? Thirds of each game's own length.
        var towerThird = new int[3];
        foreach (GameResult r in results)
        {
            foreach (TowerBuild b in r.TowerBuilds)
            {
                int third = Math.Min(2, (b.Turn - 1) * 3 / Math.Max(1, r.FinalTurn));
                towerThird[third]++;
            }
        }
        int totalBuilds = towerThird.Sum();
        sb.AppendLine("Tower builds by game third (of each game's final turn): " +
            $"early={towerThird[0]}{Pct(towerThird[0], totalBuilds)} " +
            $"mid={towerThird[1]}{Pct(towerThird[1], totalBuilds)} " +
            $"late={towerThird[2]}{Pct(towerThird[2], totalBuilds)}");
    }

    private static string Pct(int part, int whole) =>
        whole > 0 ? $" ({part * 1000 / whole / 10.0:0.0}%)" : "";

    private static void AppendDistribution(
        StringBuilder sb, string title, ConcurrentDictionary<string, int> counts)
    {
        int total = counts.Values.Sum();
        sb.AppendLine($"{title} ({total} chosen actions)");
        foreach (KeyValuePair<string, int> kvp in
            counts.OrderByDescending(kvp => kvp.Value).ThenBy(kvp => kvp.Key))
        {
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}" +
                (total > 0 ? $" ({kvp.Value * 1000 / total / 10.0:0.0}%)" : ""));
        }
    }

    /// <summary>Extract the whitespace-delimited value following
    /// <paramref name="key"/> (e.g. "phase=") in a [chose] log line.</summary>
    private static string? Token(string line, string key)
    {
        int at = line.IndexOf(key, StringComparison.Ordinal);
        if (at < 0) return null;
        int start = at + key.Length;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private static (int Lo, int Hi) ParseRange(string? spec, int defLo, int defHi)
    {
        if (string.IsNullOrWhiteSpace(spec)) return (defLo, defHi);
        int dash = spec.IndexOf('-');
        if (dash < 0)
        {
            int only = int.Parse(spec);
            return (only, only);
        }
        return (int.Parse(spec[..dash]), int.Parse(spec[(dash + 1)..]));
    }
}
