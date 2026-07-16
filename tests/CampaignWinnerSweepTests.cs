// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace FourExHex.Tests;

/// <summary>
/// Campaign winner-distribution sweep (#73 groundwork): plays campaign
/// levels headless with every active slot forced to a Soldier Computer
/// (the human's hash-assigned slot included) and records which slot wins
/// each level. The report answers whether "assign the human to the
/// observed winner's slot" would spread the human across colors and
/// turn-order positions, or collapse onto first-movers.
///
/// Also hosts the seed search that generates the baked winnable-seed
/// table for <see cref="CampaignProgress.SeedForLevel"/>: per level, walk a
/// deterministic candidate-seed sequence (attempt 0 = the level number, so a
/// level whose human slot already wins keeps its map byte-identically) and
/// take the first seed whose all-AI game is won by the level's hash-assigned
/// human slot — the winnability proof at Soldier.
///
/// Env-gated so plain <c>dotnet test</c> stays fast:
///   FOUREXHEX_CAMPAIGN_SWEEP=1        — run the distribution sweep
///   FOUREXHEX_CAMPAIGN_SEED_SEARCH=1  — run the seed search
///   FOUREXHEX_SWEEP_LEVELS=a-b        — optional level range (default 0-255)
///   FOUREXHEX_SWEEP_OUT=path          — optional CSV path (default %TMP%);
///                                       summary at path + ".summary.txt";
///                                       search also emits path + ".table.cs.txt"
///   FOUREXHEX_SEARCH_MAX_ATTEMPTS=n   — search cutoff per level (default 128)
/// Invoke:
///   FOUREXHEX_CAMPAIGN_SWEEP=1 dotnet test --filter FullyQualifiedName~CampaignWinnerSweep
///
/// Faithful to a real campaign launch (Main.cs): 30×20 grid,
/// ProceduralGame.Build with the level's seed/densities/mode, turn cap 500
/// (the FOUREXHEX_6AI full-mode convention). Deterministic: the CSV is
/// byte-identical across reruns of the same range.
/// </summary>
public class CampaignWinnerSweepTests
{
    private const int Cols = 30;
    private const int Rows = 20;
    private const int MaxTurns = 500;

    private readonly ITestOutputHelper _output;

    public CampaignWinnerSweepTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed record LevelResult(
        int Level, GameMode Mode, int PlayerCount, int[] ActiveSlots,
        int HumanHashSlot, int WinnerSlot, int WinnerPosition, int FinalTurn);

    [Fact]
    public void CampaignWinnerSweep_ReportDistribution()
    {
        if (Environment.GetEnvironmentVariable("FOUREXHEX_CAMPAIGN_SWEEP") is null)
        {
            _output.WriteLine("Sweep skipped: FOUREXHEX_CAMPAIGN_SWEEP not set.");
            return;
        }

        (int lo, int hi) = ParseRange(
            Environment.GetEnvironmentVariable("FOUREXHEX_SWEEP_LEVELS"));
        string csvPath = Environment.GetEnvironmentVariable("FOUREXHEX_SWEEP_OUT")
            ?? Path.Combine(Path.GetTempPath(), "campaign-sweep.csv");
        string progressPath = csvPath + ".progress";
        string summaryPath = csvPath + ".summary.txt";

        int total = hi - lo + 1;
        var results = new LevelResult[total];
        int done = 0;
        object progressLock = new object();
        long startTicks = Environment.TickCount64;

        Parallel.For(lo, hi + 1, level =>
        {
            results[level - lo] = RunLevel(level);
            int finished = Interlocked.Increment(ref done);
            lock (progressLock)
            {
                File.WriteAllText(progressPath,
                    $"{finished}/{total} levels, " +
                    $"{(Environment.TickCount64 - startTicks) / 1000}s elapsed\n");
            }
        });

        WriteCsv(csvPath, results);
        string summary = BuildSummary(results,
            (Environment.TickCount64 - startTicks) / 1000);
        File.WriteAllText(summaryPath, summary);
        _output.WriteLine($"CSV: {csvPath}");
        _output.WriteLine(summary);
    }

    /// <summary>
    /// Play one campaign level all-AI to completion. Roster mirrors
    /// Player.BuildCampaignRoster minus the human: every active slot a
    /// Soldier Computer, keeping its original color slot via
    /// PlayerId.FromIndex so turn order (sorted active slots) matches a
    /// real campaign game. WinnerSlot -1 = DidNotResolve (turn cap).
    /// </summary>
    private static LevelResult RunLevel(int level)
    {
        int[] slots = CampaignProgress.ActiveColorSlotsForLevel(level);
        (int winnerSlot, int finalTurn) =
            RunGame(level, CampaignProgress.SeedForLevel(level));
        int winnerPosition = winnerSlot >= 0 ? Array.IndexOf(slots, winnerSlot) : -1;
        return new LevelResult(
            level, CampaignProgress.ModeForLevel(level), slots.Length, slots,
            CampaignProgress.HumanColorSlotForLevel(level),
            winnerSlot, winnerPosition, finalTurn);
    }

    /// <summary>
    /// One all-AI game of a campaign level's configuration (roster, densities,
    /// mode all derived from the level number) on an explicit map/turn-RNG
    /// seed — the level's own seed for the sweep, a candidate seed for the
    /// search. WinnerSlot -1 = no winner by the turn cap.
    /// </summary>
    private static (int WinnerSlot, int FinalTurn) RunGame(int level, int seed)
    {
        int[] slots = CampaignProgress.ActiveColorSlotsForLevel(level);
        var players = new List<Player>(slots.Length);
        foreach (int slot in slots)
        {
            players.Add(new Player(
                GameSettings.PlayerConfig[slot].Name,
                PlayerId.FromIndex(slot),
                PlayerKind.Computer,
                Difficulty.Soldier));
        }

        GameState state = ProceduralGame.Build(
            Cols, Rows, players, seed,
            CampaignProgress.MapGenOptionsForLevel(level),
            CampaignProgress.ModeForLevel(level));

        var session = new SessionState();
        var controller = new GameController(state, session,
            new MockHexMapView(), new MockHudView(),
            seed: seed,
            aiPacer: new SynchronousAiPacer(),
            aiChooser: AiDispatcher.ChooseForCurrentPlayer,
            maxTurnNumber: MaxTurns);
        controller.StartGame();
        // All-AI + SynchronousAiPacer → StartGame returns at GameEnded
        // (natural win, Winner set) or the turn cap (Winner stays null).

        return (session.Winner?.Index ?? -1, state.Turns.TurnNumber);
    }

    private sealed record SearchResult(
        int Level, int HumanSlot, int Seed, int Attempt, int FinalTurn, bool Found);

    [Fact]
    public void CampaignSeedSearch_GenerateWinnableSeedTable()
    {
        if (Environment.GetEnvironmentVariable("FOUREXHEX_CAMPAIGN_SEED_SEARCH") is null)
        {
            _output.WriteLine("Search skipped: FOUREXHEX_CAMPAIGN_SEED_SEARCH not set.");
            return;
        }

        (int lo, int hi) = ParseRange(
            Environment.GetEnvironmentVariable("FOUREXHEX_SWEEP_LEVELS"));
        // Default headroom: a 2-player level with the human as second mover
        // wins only ~6% of rolls (the observed worst case needed attempt 61),
        // so 128 keeps even an unlucky re-search comfortably inside the cap.
        int maxAttempts = int.TryParse(
            Environment.GetEnvironmentVariable("FOUREXHEX_SEARCH_MAX_ATTEMPTS"),
            out int parsed) ? parsed : 128;
        string csvPath = Environment.GetEnvironmentVariable("FOUREXHEX_SWEEP_OUT")
            ?? Path.Combine(Path.GetTempPath(), "campaign-seed-search.csv");
        string progressPath = csvPath + ".progress";

        // Crash/kill-safe: each level's row is appended to the CSV the moment
        // its search concludes, and a rerun resumes by skipping levels already
        // on disk. Per-level results are deterministic, so a resumed run
        // produces the same rows a single run would have.
        Dictionary<int, SearchResult> results = LoadExistingSearchRows(csvPath);
        int resumed = results.Count;
        if (resumed == 0)
        {
            File.WriteAllText(csvPath,
                "level,label,humanSlot,seed,seedHex,attempt,finalTurn,found\n");
        }

        int total = hi - lo + 1;
        int levelsDone = resumed;
        int gamesPlayed = 0;
        object writeLock = new object();
        long startTicks = Environment.TickCount64;

        Parallel.For(lo, hi + 1, level =>
        {
            lock (writeLock) { if (results.ContainsKey(level)) return; }
            int humanSlot = CampaignProgress.HumanColorSlotForLevel(level);
            SearchResult result = new SearchResult(
                level, humanSlot, 0, maxAttempts, 0, Found: false);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int seed = CandidateSeed(level, attempt);
                (int winnerSlot, int finalTurn) = RunGame(level, seed);
                Interlocked.Increment(ref gamesPlayed);
                if (winnerSlot == humanSlot)
                {
                    result = new SearchResult(
                        level, humanSlot, seed, attempt, finalTurn, Found: true);
                    break;
                }
            }
            int finished;
            lock (writeLock)
            {
                results[level] = result;
                File.AppendAllText(csvPath, SearchCsvRow(result));
                finished = ++levelsDone;
                File.WriteAllText(progressPath,
                    $"{finished}/{total} levels ({resumed} resumed), " +
                    $"{gamesPlayed} games, " +
                    $"{(Environment.TickCount64 - startTicks) / 1000}s elapsed\n");
            }
        });

        WriteSearchOutputs(csvPath, results, lo, hi, maxAttempts,
            (Environment.TickCount64 - startTicks) / 1000, gamesPlayed, resumed);
        _output.WriteLine($"CSV: {csvPath}");
        _output.WriteLine(File.ReadAllText(csvPath + ".summary.txt"));
    }

    private static string SearchCsvRow(SearchResult r) =>
        $"{r.Level},{CampaignProgress.LabelFor(r.Level)},{r.HumanSlot}," +
        $"{r.Seed},{SeedFormat.ToHex(r.Seed)},{r.Attempt},{r.FinalTurn}," +
        $"{(r.Found ? 1 : 0)}\n";

    private static Dictionary<int, SearchResult> LoadExistingSearchRows(string csvPath)
    {
        var results = new Dictionary<int, SearchResult>();
        if (!File.Exists(csvPath)) return results;
        foreach (string line in File.ReadAllLines(csvPath))
        {
            string[] cols = line.Split(',');
            if (cols.Length < 8 || !int.TryParse(cols[0], out int level)) continue;
            results[level] = new SearchResult(
                level, int.Parse(cols[2]), int.Parse(cols[3]),
                int.Parse(cols[5]), int.Parse(cols[6]), cols[7] == "1");
        }
        return results;
    }

    /// <summary>
    /// Deterministic candidate-seed sequence for a level. Attempt 0 is the
    /// level number itself — a level that is already winnable from the human's
    /// slot keeps its existing map byte-identically. Later attempts are an
    /// integer avalanche mix over (level, attempt) spanning the full 32-bit
    /// seed range, decorrelated from every CampaignProgress per-level draw.
    /// </summary>
    private static int CandidateSeed(int level, int attempt)
    {
        if (attempt == 0) return level;
        uint h = unchecked((uint)level * 2246822519u + (uint)attempt * 3266489917u
            + 668265263u);
        h ^= h >> 15;
        h = unchecked(h * 2654435761u);
        h ^= h >> 13;
        return unchecked((int)h);
    }

    private static void WriteSearchOutputs(string csvPath,
        Dictionary<int, SearchResult> resultsByLevel, int lo, int hi,
        int maxAttempts, long elapsedSeconds, int gamesPlayed, int resumed)
    {
        // Canonical, sorted final CSV (append order varies across threads
        // and resumes; the finished artifact is deterministic).
        var ordered = new List<SearchResult>();
        for (int level = lo; level <= hi; level++)
        {
            if (resultsByLevel.TryGetValue(level, out SearchResult? r)) ordered.Add(r);
        }
        SearchResult[] results = ordered.ToArray();
        var csv = new StringBuilder();
        csv.AppendLine("level,label,humanSlot,seed,seedHex,attempt,finalTurn,found");
        foreach (SearchResult r in results) csv.Append(SearchCsvRow(r));
        File.WriteAllText(csvPath, csv.ToString());

        // Ready-to-paste C# initializer for the baked table (only meaningful
        // when the run covered the full 0-255 range).
        var table = new StringBuilder();
        table.AppendLine("        // Generated by CampaignSeedSearch_GenerateWinnableSeedTable.");
        for (int i = 0; i < results.Length; i++)
        {
            if (i % 8 == 0) table.Append("        ");
            table.Append(results[i].Seed);
            table.Append(i == results.Length - 1 ? "" : ",");
            table.Append((i % 8 == 7 || i == results.Length - 1) ? "\n" : " ");
        }
        File.WriteAllText(csvPath + ".table.cs.txt", table.ToString());

        var sb = new StringBuilder();
        int found = 0;
        int keptIdentity = 0;
        int worstAttempt = -1;
        int worstLevel = -1;
        var failures = new List<int>();
        var attemptHistogram = new SortedDictionary<int, int>();
        foreach (SearchResult r in results)
        {
            if (!r.Found) { failures.Add(r.Level); continue; }
            found++;
            if (r.Attempt == 0) keptIdentity++;
            attemptHistogram[r.Attempt] =
                attemptHistogram.GetValueOrDefault(r.Attempt) + 1;
            if (r.Attempt > worstAttempt)
            {
                worstAttempt = r.Attempt;
                worstLevel = r.Level;
            }
        }
        sb.AppendLine($"=== Campaign seed search: {results.Length} level(s), " +
            $"{gamesPlayed} games, {elapsedSeconds}s wall" +
            (resumed > 0 ? $", {resumed} level(s) resumed from prior run" : "") +
            " ===");
        sb.AppendLine($"Found winnable seed: {found}/{results.Length}  " +
            $"(kept identity seed: {keptIdentity})");
        sb.AppendLine($"Exhausted {maxAttempts} attempts: {failures.Count}" +
            (failures.Count > 0 ? $"  [{string.Join(", ", failures)}]" : ""));
        if (worstLevel >= 0)
        {
            sb.AppendLine($"Deepest search: level {worstLevel} at attempt {worstAttempt}");
        }
        sb.AppendLine("Attempts histogram (attempt: levels):");
        foreach (KeyValuePair<int, int> kvp in attemptHistogram)
        {
            sb.AppendLine($"  {kvp.Key}: {kvp.Value}");
        }
        File.WriteAllText(csvPath + ".summary.txt", sb.ToString());
    }

    private static (int Lo, int Hi) ParseRange(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return (0, CampaignProgress.LevelCount - 1);
        }
        int dash = spec.IndexOf('-');
        if (dash < 0)
        {
            int only = int.Parse(spec);
            return (only, only);
        }
        return (int.Parse(spec[..dash]), int.Parse(spec[(dash + 1)..]));
    }

    private static void WriteCsv(string path, LevelResult[] results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("level,label,tier,mode,playerCount,activeSlots," +
            "humanHashSlot,winnerSlot,winnerPosition,finalTurn,humanHashWon");
        foreach (LevelResult r in results)
        {
            sb.AppendLine(
                $"{r.Level},{CampaignProgress.LabelFor(r.Level)}," +
                $"{CampaignProgress.DifficultyForLevel(r.Level)},{r.Mode}," +
                $"{r.PlayerCount},{string.Join(' ', r.ActiveSlots)}," +
                $"{r.HumanHashSlot},{r.WinnerSlot},{r.WinnerPosition}," +
                $"{r.FinalTurn},{(r.WinnerSlot == r.HumanHashSlot ? 1 : 0)}");
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string BuildSummary(LevelResult[] results, long elapsedSeconds)
    {
        var sb = new StringBuilder();
        int total = results.Length;
        int resolved = 0;
        int hashWins = 0;
        var unresolvedLevels = new List<int>();
        int slotCount = GameSettings.PlayerConfig.Length;
        var winsBySlot = new int[slotCount];
        var winsByPosition = new int[slotCount];
        // [playerCount][position] — first-mover advantage differs by roster size.
        var winsByCountAndPosition = new int[slotCount + 1, slotCount];
        var levelsByCount = new int[slotCount + 1];
        var winsByTier = new Dictionary<Difficulty, int>();
        var levelsByTier = new Dictionary<Difficulty, int>();
        var winsByMode = new Dictionary<GameMode, int>();
        var levelsByMode = new Dictionary<GameMode, int>();

        foreach (LevelResult r in results)
        {
            Difficulty tier = CampaignProgress.DifficultyForLevel(r.Level);
            levelsByTier[tier] = levelsByTier.GetValueOrDefault(tier) + 1;
            levelsByMode[r.Mode] = levelsByMode.GetValueOrDefault(r.Mode) + 1;
            levelsByCount[r.PlayerCount]++;
            if (r.WinnerSlot < 0)
            {
                unresolvedLevels.Add(r.Level);
                continue;
            }
            resolved++;
            winsBySlot[r.WinnerSlot]++;
            winsByPosition[r.WinnerPosition]++;
            winsByCountAndPosition[r.PlayerCount, r.WinnerPosition]++;
            winsByTier[tier] = winsByTier.GetValueOrDefault(tier) + 1;
            winsByMode[r.Mode] = winsByMode.GetValueOrDefault(r.Mode) + 1;
            if (r.WinnerSlot == r.HumanHashSlot) hashWins++;
        }

        sb.AppendLine($"=== Campaign winner sweep: {total} level(s), " +
            $"{elapsedSeconds}s wall ===");
        sb.AppendLine($"Resolved: {resolved}/{total}  " +
            $"DidNotResolve (turn cap {MaxTurns}): {unresolvedLevels.Count}" +
            (unresolvedLevels.Count > 0
                ? $"  [{string.Join(", ", unresolvedLevels)}]" : ""));
        sb.AppendLine($"Hash-assigned human slot won: {hashWins}/{resolved}" +
            (resolved > 0 ? $" ({hashWins * 100 / resolved}%)" : ""));

        sb.AppendLine("Wins by color slot:");
        for (int s = 0; s < slotCount; s++)
        {
            sb.AppendLine($"  slot {s} ({GameSettings.PlayerConfig[s].Name}): " +
                $"{winsBySlot[s]}" +
                (resolved > 0 ? $" ({winsBySlot[s] * 100 / resolved}%)" : ""));
        }

        sb.AppendLine("Wins by turn-order position (1 = first mover):");
        for (int p = 0; p < slotCount; p++)
        {
            sb.AppendLine($"  position {p + 1}: {winsByPosition[p]}" +
                (resolved > 0 ? $" ({winsByPosition[p] * 100 / resolved}%)" : ""));
        }

        sb.AppendLine("Wins by turn-order position, per player count " +
            "(levels in bucket):");
        for (int c = 2; c <= slotCount; c++)
        {
            if (levelsByCount[c] == 0) continue;
            var row = new StringBuilder($"  {c}p ({levelsByCount[c]}): ");
            for (int p = 0; p < c; p++)
            {
                row.Append($"pos{p + 1}={winsByCountAndPosition[c, p]} ");
            }
            sb.AppendLine(row.ToString().TrimEnd());
        }

        sb.AppendLine("Wins / levels by tier:");
        foreach (KeyValuePair<Difficulty, int> kvp in levelsByTier)
        {
            sb.AppendLine($"  {kvp.Key}: " +
                $"{winsByTier.GetValueOrDefault(kvp.Key)}/{kvp.Value} resolved");
        }

        sb.AppendLine("Wins / levels by mode:");
        foreach (KeyValuePair<GameMode, int> kvp in levelsByMode)
        {
            sb.AppendLine($"  {kvp.Key}: " +
                $"{winsByMode.GetValueOrDefault(kvp.Key)}/{kvp.Value} resolved");
        }

        return sb.ToString();
    }
}
