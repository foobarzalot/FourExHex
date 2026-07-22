// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Headless level-design CLI. Thin argument-parsing shell over the
/// Model/Controller harness types (LevelWorkspace, LevelPlaytest) —
/// no game logic lives here. See LEVEL_DESIGN.md for the runbook.
/// </summary>
internal static class Program
{
    private const string Usage = """
        FourExHex level designer — headless authoring + playtesting of starting maps.

        usage: <command> <map-name> [options]
          new <name>       create a map file (blank all-water unless --gen)
              --cols N --rows N        board size (default 30x20)
              --gen SEED               procedural start from SEED
              --players N              procedural: active slots (default 6)
              --trees N --mountains N --gold N --neutral N --clump N
                                       procedural densities (percent)
              --mode MODE              Freeform|RisingTides|FogOfWar|VikingRaiders
          show <name>      print the board, roster, and validation status
          edit <name> <ops...>   apply paint ops, save, and print the board
              ops: land SLOT C,R...   paint land for slot (also: rect C,R C,R)
                   neutral C,R...     paint neutral land   (also: rect C,R C,R)
                   water C,R...       paint water          (also: rect C,R C,R)
                   capital C,R        move a territory's capital
                   tree|tower|gold|mountain C,R...   toggle feature/occupant
              --script FILE           read ops from FILE (one op per line, # comments)
          roster <name> --slot I=KIND[:DIFFICULTY] ...
                                      set slots, e.g. --slot 0=Human --slot 1=Computer:Commander
                                      kinds: Human|Computer|None; difficulties:
                                      Recruit|Soldier|Captain|Commander
          validate <name>  print problems (exit 2 if invalid)
          playtest <name>  run headless AI-vs-AI games and print metrics
              --games N               number of games (default 10)
              --seed S                base seed (default 42; game i uses S+i)
              --max-turns M           turn cap per game (default 500)

        common: --dir PATH   map directory (default: the game's user://maps/ dir,
                             so results appear in Load Starting Map immediately)
        env:    FOUREXHEX_LOG="LevelDesign:Debug" for op-by-op tracing
        """;

    private static int Main(string[] args)
    {
        Log.Sink = Console.WriteLine;
        Log.Configure(Environment.GetEnvironmentVariable("FOUREXHEX_LOG"));

        try
        {
            if (args.Length < 2)
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : Fail("expected: <command> <map-name>");
            }

            string command = args[0];
            string name = SaveNames.Sanitize(args[1]);
            var rest = new Args(args.Skip(2).ToArray());
            string dir = rest.Option("--dir") ?? DefaultMapsDir();
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".json");

            return command switch
            {
                "new" => CmdNew(name, path, rest),
                "show" => CmdShow(path),
                "edit" => CmdEdit(name, path, rest),
                "roster" => CmdRoster(name, path, rest),
                "validate" => CmdValidate(path),
                "playtest" => CmdPlaytest(path, rest),
                _ => Fail($"unknown command '{command}'"),
            };
        }
        catch (CliError e)
        {
            return Fail(e.Message);
        }
    }

    private static int CmdNew(string name, string path, Args args)
    {
        if (File.Exists(path))
            return Fail($"{path} already exists — delete it first or pick another name");

        int cols = args.IntOption("--cols") ?? 30;
        int rows = args.IntOption("--rows") ?? 20;
        GameMode mode = ParseEnum<GameMode>(args.Option("--mode") ?? "Freeform", "--mode");

        LevelWorkspace ws;
        int? genSeed = args.IntOption("--gen");
        if (genSeed.HasValue)
        {
            var options = new MapGenOptions
            {
                TreeDensity = args.IntOption("--trees") ?? 0,
                MountainDensity = args.IntOption("--mountains") ?? 0,
                GoldDensity = args.IntOption("--gold") ?? 0,
                NeutralDensity = args.IntOption("--neutral") ?? 0,
                ClumpingFactor = args.IntOption("--clump") ?? 0,
            };
            ws = LevelWorkspace.NewProcedural(
                cols, rows, genSeed.Value, options, mode,
                activeSlots: args.IntOption("--players") ?? 6);
        }
        else
        {
            ws = new LevelWorkspace(cols, rows) { Mode = mode };
        }

        File.WriteAllText(path, ws.ToJson(name));
        Console.WriteLine($"created {path}");
        Console.Write(ws.RenderText());
        return 0;
    }

    private static int CmdShow(string path)
    {
        LevelWorkspace ws = LoadWorkspace(path);
        Console.Write(ws.RenderText());
        PrintValidation(ws);
        return 0;
    }

    private static int CmdEdit(string name, string path, Args args)
    {
        LevelWorkspace ws = LoadWorkspace(path);

        List<string> tokens = args.Positionals.ToList();
        string? script = args.Option("--script");
        if (script != null)
        {
            foreach (string line in File.ReadAllLines(script))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
                tokens.AddRange(trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        if (tokens.Count == 0)
            return Fail("edit needs paint ops (inline or via --script)");

        int applied = ApplyOps(ws, tokens);
        File.WriteAllText(path, ws.ToJson(name));
        Console.WriteLine($"applied {applied} op(s), saved {path}");
        Console.Write(ws.RenderText());
        PrintValidation(ws);
        return 0;
    }

    private static int CmdRoster(string name, string path, Args args)
    {
        LevelWorkspace ws = LoadWorkspace(path);
        IReadOnlyList<string> slots = args.MultiOption("--slot");
        if (slots.Count == 0)
            return Fail("roster needs at least one --slot I=KIND[:DIFFICULTY]");

        foreach (string spec in slots)
        {
            string[] parts = spec.Split('=', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0], out int slot)
                || slot < 0 || slot >= GameSettings.PlayerConfig.Length)
                throw new CliError($"bad --slot spec '{spec}' (want I=KIND[:DIFFICULTY])");
            string[] kindParts = parts[1].Split(':', 2);
            PlayerKind kind = ParseEnum<PlayerKind>(kindParts[0], "--slot kind");
            Difficulty difficulty = kindParts.Length > 1
                ? ParseEnum<Difficulty>(kindParts[1], "--slot difficulty")
                : Difficulty.Soldier;
            ws.SetSlot(slot, kind, difficulty);
        }

        File.WriteAllText(path, ws.ToJson(name));
        Console.WriteLine($"saved {path}");
        Console.Write(ws.RenderText());
        PrintValidation(ws);
        return 0;
    }

    private static int CmdValidate(string path)
    {
        LevelWorkspace ws = LoadWorkspace(path);
        IReadOnlyList<string> problems = ws.Validate();
        if (problems.Count == 0)
        {
            Console.WriteLine("OK");
            return 0;
        }
        foreach (string p in problems) Console.WriteLine(p);
        return 2;
    }

    private static int CmdPlaytest(string path, Args args)
    {
        string json = File.Exists(path)
            ? File.ReadAllText(path)
            : throw new CliError($"no map at {path}");

        // Refuse to playtest an invalid map — the game would refuse to load it.
        LevelWorkspace ws = LevelWorkspace.FromJson(json);
        IReadOnlyList<string> problems = ws.Validate();
        if (problems.Count > 0)
        {
            foreach (string p in problems) Console.WriteLine(p);
            return 2;
        }

        PlaytestReport report = LevelPlaytest.Run(
            json,
            games: args.IntOption("--games") ?? 10,
            baseSeed: args.IntOption("--seed") ?? 42,
            maxTurnNumber: args.IntOption("--max-turns") ?? 500);
        Console.Write(report.Format());
        return 0;
    }

    // --- op parsing ---

    private static int ApplyOps(LevelWorkspace ws, List<string> tokens)
    {
        var opNames = new HashSet<string>
        {
            "land", "neutral", "water", "capital", "tree", "tower", "gold", "mountain",
        };
        int applied = 0;
        int i = 0;
        while (i < tokens.Count)
        {
            string op = tokens[i++];
            if (!opNames.Contains(op))
                throw new CliError($"unknown op '{op}' (expected one of: {string.Join(", ", opNames)})");

            int slot = -1;
            if (op == "land")
            {
                if (i >= tokens.Count || !int.TryParse(tokens[i], out slot)
                    || slot < 0 || slot >= GameSettings.PlayerConfig.Length)
                    throw new CliError("'land' needs a slot index 0-5 before its coords");
                i++;
            }

            List<HexCoord> coords = ParseCoords(tokens, ref i, op, opNames);
            foreach (HexCoord coord in coords)
            {
                switch (op)
                {
                    case "land": ws.PaintLand(slot, coord); break;
                    case "neutral": ws.PaintNeutral(coord); break;
                    case "water": ws.PaintWater(coord); break;
                    case "capital": ws.PaintCapital(coord); break;
                    case "tree": ws.ToggleTree(coord); break;
                    case "tower": ws.ToggleTower(coord); break;
                    case "gold": ws.ToggleGold(coord); break;
                    case "mountain": ws.ToggleMountain(coord); break;
                }
                applied++;
            }
        }
        return applied;
    }

    private static List<HexCoord> ParseCoords(
        List<string> tokens, ref int i, string op, HashSet<string> opNames)
    {
        var coords = new List<HexCoord>();
        while (i < tokens.Count && !opNames.Contains(tokens[i]))
        {
            if (tokens[i] == "rect")
            {
                if (i + 2 >= tokens.Count)
                    throw new CliError($"'{op} rect' needs two corner coords C,R C,R");
                (int c1, int r1) = ParseCoordPair(tokens[i + 1]);
                (int c2, int r2) = ParseCoordPair(tokens[i + 2]);
                coords.AddRange(LevelWorkspace.RectCoords(c1, r1, c2, r2));
                i += 3;
            }
            else
            {
                (int col, int row) = ParseCoordPair(tokens[i]);
                coords.Add(HexCoord.FromOffset(col, row));
                i++;
            }
        }
        if (coords.Count == 0)
            throw new CliError($"op '{op}' has no coords");
        return coords;
    }

    private static (int Col, int Row) ParseCoordPair(string token)
    {
        string[] parts = token.Split(',');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out int col)
            || !int.TryParse(parts[1], out int row))
            throw new CliError($"bad coord '{token}' (want COL,ROW e.g. 4,7)");
        return (col, row);
    }

    // --- shared plumbing ---

    private static LevelWorkspace LoadWorkspace(string path)
    {
        if (!File.Exists(path)) throw new CliError($"no map at {path}");
        return LevelWorkspace.FromJson(File.ReadAllText(path));
    }

    private static void PrintValidation(LevelWorkspace ws)
    {
        IReadOnlyList<string> problems = ws.Validate();
        Console.WriteLine(problems.Count == 0
            ? "validation: OK"
            : "validation: " + string.Join(" | ", problems));
    }

    /// <summary>The game's user://maps/ directory (project.godot has no
    /// custom user-dir override, so it's the stock Godot layout).</summary>
    private static string DefaultMapsDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string godotData = OperatingSystem.IsMacOS()
            ? Path.Combine(home, "Library", "Application Support", "Godot")
            : OperatingSystem.IsWindows()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Godot")
                : Path.Combine(home, ".local", "share", "godot");
        return Path.Combine(godotData, "app_userdata", "FourExHex", "maps");
    }

    private static T ParseEnum<T>(string raw, string what) where T : struct, Enum
    {
        if (Enum.TryParse(raw, ignoreCase: true, out T value)) return value;
        throw new CliError(
            $"bad {what} '{raw}' (want one of: {string.Join("|", Enum.GetNames<T>())})");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }

    private sealed class CliError : Exception
    {
        public CliError(string message) : base(message) { }
    }

    /// <summary>Tiny option scanner: --key value options (repeatable) and
    /// everything else positional, order preserved.</summary>
    private sealed class Args
    {
        private readonly List<(string Key, string Value)> _options = new();
        public List<string> Positionals { get; } = new();

        public Args(string[] raw)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i].StartsWith("--", StringComparison.Ordinal))
                {
                    if (i + 1 >= raw.Length)
                        throw new CliError($"option {raw[i]} needs a value");
                    _options.Add((raw[i], raw[i + 1]));
                    i++;
                }
                else
                {
                    Positionals.Add(raw[i]);
                }
            }
        }

        public string? Option(string key) =>
            _options.LastOrDefault(o => o.Key == key).Value;

        public IReadOnlyList<string> MultiOption(string key) =>
            _options.Where(o => o.Key == key).Select(o => o.Value).ToList();

        public int? IntOption(string key)
        {
            string? raw = Option(key);
            if (raw == null) return null;
            if (!int.TryParse(raw, out int value))
                throw new CliError($"option {key} wants an integer, got '{raw}'");
            return value;
        }
    }
}
