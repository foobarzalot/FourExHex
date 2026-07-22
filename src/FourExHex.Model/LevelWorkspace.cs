// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Headless authoring session for a starting map: the level-design
/// harness's Godot-free counterpart of the map editor's draft state.
/// Holds the grid + water set + territory thread and a 6-slot baked
/// roster; every paint op routes through <see cref="MapEditPaint"/> so
/// each intermediate state stays legal (the returned territory list is
/// threaded back in — the capital-inheritance invariant). Serializes
/// to the exact starting-map format the game's Load Starting Map flow
/// reads (turn-0 <see cref="TurnState"/>, baked kinds, mode).
/// </summary>
public sealed class LevelWorkspace
{
    private readonly HexGrid _grid;
    private readonly HashSet<HexCoord> _water;
    private IReadOnlyList<Territory> _territories;
    private readonly PlayerKind[] _kinds;
    private readonly Difficulty[] _difficulties;

    public int Cols { get; }
    public int Rows { get; }
    public GameMode Mode { get; set; } = GameMode.Freeform;
    public int MapSeed { get; set; }
    public HexGrid Grid => _grid;
    public IReadOnlySet<HexCoord> Water => _water;
    public IReadOnlyList<Territory> Territories => _territories;

    /// <summary>Blank canvas: every in-bounds cell is water, no slots active.</summary>
    public LevelWorkspace(int cols, int rows)
        : this(cols, rows, new HexGrid(), AllWater(cols, rows), new List<Territory>())
    {
    }

    private LevelWorkspace(
        int cols, int rows, HexGrid grid, HashSet<HexCoord> water,
        IReadOnlyList<Territory> territories)
    {
        Cols = cols;
        Rows = rows;
        _grid = grid;
        _water = water;
        _territories = territories;
        _kinds = new PlayerKind[GameSettings.PlayerConfig.Length];
        Array.Fill(_kinds, PlayerKind.None);
        _difficulties = new Difficulty[GameSettings.PlayerConfig.Length];
        Array.Fill(_difficulties, Difficulty.Soldier);
    }

    private static HashSet<HexCoord> AllWater(int cols, int rows)
    {
        var water = new HashSet<HexCoord>();
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
                water.Add(HexCoord.FromOffset(col, row));
        return water;
    }

    /// <summary>
    /// Procedural starting point: the map editor's Generate path.
    /// The first <paramref name="activeSlots"/> slots are seeded as
    /// Computer/Soldier owners of the generated land.
    /// </summary>
    public static LevelWorkspace NewProcedural(
        int cols, int rows, int seed, MapGenOptions options, GameMode mode,
        int activeSlots)
    {
        var players = new List<Player>(activeSlots);
        for (int slot = 0; slot < activeSlots; slot++)
        {
            players.Add(new Player(
                GameSettings.PlayerConfig[slot].Name,
                PlayerId.FromIndex(slot),
                PlayerKind.Computer,
                Difficulty.Soldier));
        }

        MapGenResult gen = MapGenerator.BuildInitialGrid(cols, rows, players, seed, options);
        var ws = new LevelWorkspace(
            cols, rows, gen.Grid, new HashSet<HexCoord>(gen.WaterCoords),
            TerritoryFinder.Recompute(gen.Grid, new List<Territory>()))
        {
            Mode = mode,
            MapSeed = seed,
        };
        for (int slot = 0; slot < activeSlots; slot++)
            ws._kinds[slot] = PlayerKind.Computer;
        if (Log.IsEnabled(Log.LogCategory.LevelDesign, Log.LogLevel.Debug))
            Log.Debug(Log.LogCategory.LevelDesign,
                $"[level] procedural new {cols}x{rows} seed={seed} slots={activeSlots} tiles={CountTiles(gen.Grid)}");
        return ws;
    }

    public PlayerKind KindFor(int slot) => _kinds[slot];
    public Difficulty DifficultyFor(int slot) => _difficulties[slot];

    public void SetSlot(int slot, PlayerKind kind, Difficulty difficulty = Difficulty.Soldier)
    {
        _kinds[slot] = kind;
        _difficulties[slot] = difficulty;
        Log.Debug(Log.LogCategory.LevelDesign,
            $"[level] roster slot {slot} = {kind}:{difficulty}");
    }

    public void PaintLand(int slot, HexCoord coord)
    {
        // Painting land for a dormant slot is an implicit "this color
        // plays" — activate it so Validate() doesn't trip on every new
        // region. An explicit SetSlot choice is never overridden.
        if (_kinds[slot] == PlayerKind.None)
            SetSlot(slot, PlayerKind.Computer);
        _territories = MapEditPaint.PaintLand(
            _grid, _water, _territories, Cols, Rows, coord, PlayerId.FromIndex(slot));
        LogPaint("land", coord, slot);
    }

    public void PaintNeutral(HexCoord coord)
    {
        _territories = MapEditPaint.PaintNeutral(
            _grid, _water, _territories, Cols, Rows, coord);
        LogPaint("neutral", coord);
    }

    public void PaintWater(HexCoord coord)
    {
        _territories = MapEditPaint.PaintWater(
            _grid, _water, _territories, Cols, Rows, coord);
        LogPaint("water", coord);
    }

    public void PaintCapital(HexCoord coord)
    {
        _territories = MapEditPaint.PaintCapital(
            _grid, _water, _territories, Cols, Rows, coord);
        LogPaint("capital", coord);
    }

    public void ToggleTree(HexCoord coord)
    {
        _territories = MapEditPaint.PaintTreeToggle(
            _grid, _water, _territories, Cols, Rows, coord);
        LogPaint("tree", coord);
    }

    public void ToggleTower(HexCoord coord)
    {
        _territories = MapEditPaint.PaintTowerToggle(
            _grid, _water, _territories, Cols, Rows, coord);
        LogPaint("tower", coord);
    }

    public void ToggleGold(HexCoord coord)
    {
        _territories = MapEditPaint.PaintGoldToggle(
            _grid, _water, _territories, Cols, Rows, coord);
        LogPaint("gold", coord);
    }

    public void ToggleMountain(HexCoord coord)
    {
        _territories = MapEditPaint.PaintMountainToggle(
            _grid, _water, _territories, Cols, Rows, coord);
        LogPaint("mountain", coord);
    }

    public IReadOnlyList<string> Validate()
    {
        IReadOnlyList<string> problems =
            MapRosterRules.ValidateForSave(_territories, _kinds);
        Log.Debug(Log.LogCategory.LevelDesign,
            $"[level] validate: {(problems.Count == 0 ? "OK" : $"{problems.Count} problem(s)")}");
        return problems;
    }

    /// <summary>Board + roster + mode/seed as agent-legible text.</summary>
    public string RenderText()
    {
        var sb = new StringBuilder();
        sb.Append(MapTextRenderer.Render(_grid, _water, Cols, Rows));
        for (int slot = 0; slot < _kinds.Length; slot++)
        {
            if (_kinds[slot] == PlayerKind.None) continue;
            sb.Append($"roster slot {slot} ({GameSettings.PlayerConfig[slot].Name}): ");
            sb.Append($"{_kinds[slot]}:{_difficulties[slot]}\n");
        }
        sb.Append($"mode: {Mode}  seed: {MapSeed}  size: {Cols}x{Rows}\n");
        return sb.ToString();
    }

    /// <summary>
    /// The exact bytes `SaveStore.WriteMapSlot` would write: turn-0
    /// state carrying the active preview roster, serialized with the
    /// full 6-slot bake roster (kinds + difficulties incl. None).
    /// </summary>
    public string ToJson(string name)
    {
        List<Player> preview = MapRosterRules.PreviewRosterFromKinds(_kinds);
        var state = new GameState(
            _grid, _territories, preview,
            new TurnState(preview, currentPlayerIndex: 0, turnNumber: 0),
            new Treasury(), _water, Mode);

        var bake = new List<Player>(_kinds.Length);
        for (int slot = 0; slot < _kinds.Length; slot++)
        {
            bake.Add(new Player(
                GameSettings.PlayerConfig[slot].Name,
                PlayerId.FromIndex(slot),
                _kinds[slot],
                _difficulties[slot]));
        }
        return SaveSerializer.SerializeMap(state, MapSeed, bake, name);
    }

    public static LevelWorkspace FromJson(string json)
    {
        LoadedSave loaded = SaveSerializer.Deserialize(json);

        int cols = 0, rows = 0;
        foreach (HexTile tile in loaded.State.Grid.Tiles)
            GrowBounds(tile.Coord, ref cols, ref rows);
        foreach (HexCoord coord in loaded.State.WaterCoords)
            GrowBounds(coord, ref cols, ref rows);

        var ws = new LevelWorkspace(
            cols, rows, loaded.State.Grid,
            new HashSet<HexCoord>(loaded.State.WaterCoords),
            loaded.State.Territories)
        {
            Mode = loaded.State.Mode,
            MapSeed = loaded.MasterSeed,
        };

        if (loaded.MapHasBakedKinds)
        {
            foreach (Player p in loaded.Players)
            {
                ws._kinds[p.Id.Index] = p.Kind;
                ws._difficulties[p.Id.Index] = p.Difficulty;
            }
        }
        else
        {
            // Legacy map with no baked roster: the game's fallback is
            // slot 0 human, the rest computer (Main.LegacyDefaultRoster).
            for (int slot = 0; slot < ws._kinds.Length; slot++)
                ws._kinds[slot] = slot == 0 ? PlayerKind.Human : PlayerKind.Computer;
        }
        return ws;
    }

    private static void GrowBounds(HexCoord coord, ref int cols, ref int rows)
    {
        (int col, int row) = coord.ToOffset();
        if (col + 1 > cols) cols = col + 1;
        if (row + 1 > rows) rows = row + 1;
    }

    public static IEnumerable<HexCoord> RectCoords(int col1, int row1, int col2, int row2)
    {
        int colLo = Math.Min(col1, col2), colHi = Math.Max(col1, col2);
        int rowLo = Math.Min(row1, row2), rowHi = Math.Max(row1, row2);
        for (int row = rowLo; row <= rowHi; row++)
            for (int col = colLo; col <= colHi; col++)
                yield return HexCoord.FromOffset(col, row);
    }

    private void LogPaint(string op, HexCoord coord, int? slot = null)
    {
        if (!Log.IsEnabled(Log.LogCategory.LevelDesign, Log.LogLevel.Debug)) return;
        (int col, int row) = coord.ToOffset();
        string target = slot.HasValue ? $" slot={slot.Value}" : "";
        Log.Debug(Log.LogCategory.LevelDesign,
            $"[level] paint {op} {col},{row}{target} -> territories={_territories.Count}");
    }

    private static int CountTiles(HexGrid grid)
    {
        int n = 0;
        foreach (HexTile _ in grid.Tiles) n++;
        return n;
    }
}
