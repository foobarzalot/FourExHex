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
/// Env-gated so plain <c>dotnet test</c> stays fast:
///   FOUREXHEX_CAMPAIGN_SWEEP=1  — required to run at all
///   FOUREXHEX_SWEEP_LEVELS=a-b  — optional level range (default 0-255)
///   FOUREXHEX_SWEEP_OUT=path    — optional CSV path (default %TMP%);
///                                 the summary lands at path + ".summary.txt"
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
        var players = new List<Player>(slots.Length);
        foreach (int slot in slots)
        {
            players.Add(new Player(
                GameSettings.PlayerConfig[slot].Name,
                PlayerId.FromIndex(slot),
                PlayerKind.Computer,
                Difficulty.Soldier));
        }

        GameMode mode = CampaignProgress.ModeForLevel(level);
        int seed = CampaignProgress.SeedForLevel(level);
        GameState state = ProceduralGame.Build(
            Cols, Rows, players, seed,
            CampaignProgress.MapGenOptionsForLevel(level), mode);

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

        int winnerSlot = session.Winner?.Index ?? -1;
        int winnerPosition = winnerSlot >= 0 ? Array.IndexOf(slots, winnerSlot) : -1;
        return new LevelResult(
            level, mode, slots.Length, slots,
            CampaignProgress.HumanColorSlotForLevel(level),
            winnerSlot, winnerPosition, state.Turns.TurnNumber);
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
