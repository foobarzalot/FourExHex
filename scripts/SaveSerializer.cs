using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <summary>
/// Bundle returned by <see cref="SaveSerializer.Deserialize"/>. Holds the
/// reconstructed <see cref="GameState"/> plus the bookkeeping metadata
/// (master seed, max-turn cap, slot name, player roster) the load path
/// needs to rebuild a <see cref="GameController"/>.
/// </summary>
public sealed class LoadedSave
{
    public GameState State { get; }
    public IReadOnlyList<Player> Players { get; }
    public int MasterSeed { get; }
    public int MaxTurnNumber { get; }
    public string SlotName { get; }

    public LoadedSave(
        GameState state,
        IReadOnlyList<Player> players,
        int masterSeed,
        int maxTurnNumber,
        string slotName)
    {
        State = state;
        Players = players;
        MasterSeed = masterSeed;
        MaxTurnNumber = maxTurnNumber;
        SlotName = slotName;
    }
}

/// <summary>
/// Save-game serializer. Pure C# (Godot-free except for the
/// <see cref="Color"/> type which is a struct) so it is exercised by
/// unit tests; the file I/O layer (SaveStore) wraps this.
///
/// Format: System.Text.Json over hand-written DTOs. Mirrors the explicit-
/// switch style of <see cref="GameStateSnapshot.CloneOccupant"/> rather
/// than relying on polymorphic deserialization, so the format is easy
/// to reason about and round-trip.
///
/// Tile and unit colors are stored as a player index (not a hex string).
/// This dodges float-equality drift on Godot.Color round-trip and matches
/// the "tiles always belong to a player" invariant of real saved games.
/// </summary>
public static class SaveSerializer
{
    /// <summary>
    /// Bump on any breaking schema change. <see cref="Deserialize"/>
    /// rejects mismatched values rather than attempting migration.
    /// </summary>
    public const int CurrentFormatVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName,
        int maxTurnNumber)
        => SerializeInternal(state, masterSeed, players, slotName, maxTurnNumber, includeKind: true);

    /// <summary>
    /// Serialize a starting map — same JSON format as <see cref="Serialize"/>,
    /// but the per-player <c>Kind</c> field is omitted. Editor maps don't
    /// commit to a roster; the Play Game config menu assigns roles at play
    /// time.
    /// </summary>
    public static string SerializeMap(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName)
        => SerializeInternal(state, masterSeed, players, slotName,
            maxTurnNumber: int.MaxValue, includeKind: false);

    private static string SerializeInternal(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName,
        int maxTurnNumber,
        bool includeKind)
    {
        // Player index by color for fast tile/unit owner lookup.
        var indexByColor = new Dictionary<Color, int>();
        for (int i = 0; i < players.Count; i++)
        {
            indexByColor[players[i].Color] = i;
        }

        var data = new SaveData
        {
            FormatVersion = CurrentFormatVersion,
            SavedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            SlotName = slotName,
            MasterSeed = masterSeed,
            TurnNumber = state.Turns.TurnNumber,
            CurrentPlayerIndex = state.Turns.CurrentPlayerIndex,
            MaxTurnNumber = maxTurnNumber,
            Players = SerializePlayers(players, includeKind),
            Tiles = SerializeTiles(state.Grid, indexByColor),
            Territories = SerializeTerritories(state.Territories, indexByColor),
            Gold = SerializeGold(state.Territories, state.Treasury),
            Water = SerializeWater(state.WaterCoords),
        };
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    public static LoadedSave Deserialize(string json)
    {
        SaveData? data = JsonSerializer.Deserialize<SaveData>(json, JsonOptions);
        if (data == null)
        {
            throw new InvalidOperationException("Save file is empty or malformed.");
        }
        if (data.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported save format version {data.FormatVersion} " +
                $"(expected {CurrentFormatVersion}).");
        }

        IReadOnlyList<Player> players = DeserializePlayers(data.Players);
        var turnState = new TurnState(
            players,
            currentPlayerIndex: data.CurrentPlayerIndex,
            turnNumber: data.TurnNumber);

        var grid = new HexGrid();
        foreach (TileDto tile in data.Tiles)
        {
            Color color = OwnerIndexToColor(tile.OwnerIndex, players);
            var hexTile = new HexTile(new HexCoord(tile.Q, tile.R), color)
            {
                Occupant = DeserializeOccupant(tile.Occupant, players),
            };
            grid.Add(hexTile);
        }

        IReadOnlyList<Territory> territories = DeserializeTerritories(data.Territories, players);

        var treasury = new Treasury();
        foreach (CapitalGoldDto g in data.Gold)
        {
            treasury.SetGold(new HexCoord(g.Q, g.R), g.Gold);
        }

        IReadOnlySet<HexCoord> waterCoords = DeserializeWater(data.Water);
        var state = new GameState(grid, territories, players, turnState, treasury, waterCoords);
        return new LoadedSave(
            state, players, data.MasterSeed, data.MaxTurnNumber, data.SlotName);
    }

    // --- Water -----------------------------------------------------------

    private static List<CoordDto> SerializeWater(IReadOnlySet<HexCoord> water)
    {
        var dtos = new List<CoordDto>(water.Count);
        foreach (HexCoord c in water)
        {
            dtos.Add(new CoordDto { Q = c.Q, R = c.R });
        }
        return dtos;
    }

    private static IReadOnlySet<HexCoord> DeserializeWater(List<CoordDto>? dtos)
    {
        var set = new HashSet<HexCoord>();
        if (dtos == null) return set;
        foreach (CoordDto c in dtos)
        {
            set.Add(new HexCoord(c.Q, c.R));
        }
        return set;
    }

    // --- Players ---------------------------------------------------------

    private static List<PlayerDto> SerializePlayers(IReadOnlyList<Player> players, bool includeKind)
    {
        var dtos = new List<PlayerDto>(players.Count);
        for (int i = 0; i < players.Count; i++)
        {
            Player p = players[i];
            dtos.Add(new PlayerDto
            {
                Index = i,
                Name = p.Name,
                ColorHex = p.Color.ToHtml(includeAlpha: false),
                // Null is omitted from JSON via JsonOptions, so starting
                // maps have no per-color role baked into the file.
                Kind = includeKind ? p.Kind.ToString() : null,
            });
        }
        return dtos;
    }

    private static IReadOnlyList<Player> DeserializePlayers(List<PlayerDto> dtos)
    {
        var list = new List<Player>(dtos.Count);
        foreach (PlayerDto dto in dtos)
        {
            // Missing Kind = starting map. Use Human as a placeholder;
            // the play-scene load path overrides from GameSettings.
            AiKind kind = string.IsNullOrEmpty(dto.Kind)
                ? AiKind.Human
                : Enum.Parse<AiKind>(dto.Kind);
            list.Add(new Player(dto.Name, new Color(dto.ColorHex), kind));
        }
        return list;
    }

    // --- Tiles -----------------------------------------------------------

    private static List<TileDto> SerializeTiles(
        HexGrid grid, Dictionary<Color, int> indexByColor)
    {
        var dtos = new List<TileDto>();
        foreach (HexTile tile in grid.Tiles)
        {
            dtos.Add(new TileDto
            {
                Q = tile.Coord.Q,
                R = tile.Coord.R,
                OwnerIndex = ColorToOwnerIndex(tile.Color, indexByColor),
                Occupant = SerializeOccupant(tile.Occupant, indexByColor),
            });
        }
        return dtos;
    }

    // --- Occupants -------------------------------------------------------

    private static OccupantDto? SerializeOccupant(
        HexOccupant? occupant, Dictionary<Color, int> indexByColor)
    {
        return occupant switch
        {
            null => null,
            Unit u => new OccupantDto
            {
                Type = "Unit",
                OwnerIndex = ColorToOwnerIndex(u.Owner, indexByColor),
                Level = u.Level.ToString(),
                HasMovedThisTurn = u.HasMovedThisTurn,
            },
            Capital => new OccupantDto { Type = "Capital" },
            Tower => new OccupantDto { Type = "Tower" },
            Tree => new OccupantDto { Type = "Tree" },
            Grave => new OccupantDto { Type = "Grave" },
            _ => throw new InvalidOperationException(
                $"Unknown occupant type for serialization: {occupant.GetType()}"),
        };
    }

    private static HexOccupant? DeserializeOccupant(
        OccupantDto? dto, IReadOnlyList<Player> players)
    {
        if (dto == null) return null;
        return dto.Type switch
        {
            "Unit" => new Unit(
                OwnerIndexToColor(dto.OwnerIndex ?? 0, players),
                Enum.Parse<UnitLevel>(dto.Level ?? nameof(UnitLevel.Peasant)))
            {
                HasMovedThisTurn = dto.HasMovedThisTurn ?? false,
            },
            "Capital" => new Capital(),
            "Tower" => new Tower(),
            "Tree" => new Tree(),
            "Grave" => new Grave(),
            _ => throw new InvalidOperationException(
                $"Unknown occupant type in save: {dto.Type}"),
        };
    }

    // --- Territories -----------------------------------------------------

    private static List<TerritoryDto> SerializeTerritories(
        IReadOnlyList<Territory> territories, Dictionary<Color, int> indexByColor)
    {
        var dtos = new List<TerritoryDto>(territories.Count);
        foreach (Territory t in territories)
        {
            var coords = new List<CoordDto>(t.Coords.Count);
            foreach (HexCoord c in t.Coords)
            {
                coords.Add(new CoordDto { Q = c.Q, R = c.R });
            }
            dtos.Add(new TerritoryDto
            {
                OwnerIndex = ColorToOwnerIndex(t.Owner, indexByColor),
                Coords = coords,
                CapitalQ = t.Capital?.Q,
                CapitalR = t.Capital?.R,
            });
        }
        return dtos;
    }

    private static IReadOnlyList<Territory> DeserializeTerritories(
        List<TerritoryDto> dtos, IReadOnlyList<Player> players)
    {
        var territories = new List<Territory>(dtos.Count);
        foreach (TerritoryDto dto in dtos)
        {
            Color owner = OwnerIndexToColor(dto.OwnerIndex, players);
            var coords = new List<HexCoord>(dto.Coords.Count);
            foreach (CoordDto c in dto.Coords)
            {
                coords.Add(new HexCoord(c.Q, c.R));
            }
            HexCoord? capital = dto.CapitalQ.HasValue && dto.CapitalR.HasValue
                ? new HexCoord(dto.CapitalQ.Value, dto.CapitalR.Value)
                : (HexCoord?)null;
            territories.Add(new Territory(owner, coords, capital));
        }
        return territories;
    }

    // --- Gold ------------------------------------------------------------

    private static List<CapitalGoldDto> SerializeGold(
        IReadOnlyList<Territory> territories, Treasury treasury)
    {
        var dtos = new List<CapitalGoldDto>();
        foreach (Territory t in territories)
        {
            if (!t.HasCapital) continue;
            HexCoord cap = t.Capital!.Value;
            dtos.Add(new CapitalGoldDto
            {
                Q = cap.Q,
                R = cap.R,
                Gold = treasury.GetGold(cap),
            });
        }
        return dtos;
    }

    // --- Color/index helpers --------------------------------------------

    private static int ColorToOwnerIndex(Color color, Dictionary<Color, int> indexByColor)
    {
        if (indexByColor.TryGetValue(color, out int idx)) return idx;
        throw new InvalidOperationException(
            $"Cannot serialize color {color} — not in player roster. " +
            $"Save format requires every tile/unit owner to belong to a known player.");
    }

    private static Color OwnerIndexToColor(int index, IReadOnlyList<Player> players)
    {
        if (index < 0 || index >= players.Count)
        {
            throw new InvalidOperationException(
                $"Owner index {index} out of range for {players.Count}-player roster.");
        }
        return players[index].Color;
    }
}

// --- DTOs ---------------------------------------------------------------
// Public for System.Text.Json reflection. Kept in this file so the schema
// is co-located with the (de)serialization code.

public sealed class SaveData
{
    public int FormatVersion { get; set; }
    public long SavedAtUnix { get; set; }
    public string SlotName { get; set; } = "";
    public int MasterSeed { get; set; }
    public int TurnNumber { get; set; }
    public int CurrentPlayerIndex { get; set; }
    public int MaxTurnNumber { get; set; }
    public List<PlayerDto> Players { get; set; } = new();
    public List<TileDto> Tiles { get; set; } = new();
    public List<TerritoryDto> Territories { get; set; } = new();
    public List<CapitalGoldDto> Gold { get; set; } = new();
    public List<CoordDto> Water { get; set; } = new();
}

public sealed class PlayerDto
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "";
    /// <summary>
    /// AI kind name (one of <see cref="AiKind"/>). Null/missing in
    /// "starting map" exports from the editor — the role for each color
    /// is assigned at play time via the Play Game config menu, not
    /// baked into the map. Present in regular in-progress saves.
    /// </summary>
    public string? Kind { get; set; }
}

public sealed class TileDto
{
    public int Q { get; set; }
    public int R { get; set; }
    public int OwnerIndex { get; set; }
    public OccupantDto? Occupant { get; set; }
}

public sealed class OccupantDto
{
    public string Type { get; set; } = "";
    public int? OwnerIndex { get; set; }
    public string? Level { get; set; }
    public bool? HasMovedThisTurn { get; set; }
}

public sealed class TerritoryDto
{
    public int OwnerIndex { get; set; }
    public List<CoordDto> Coords { get; set; } = new();
    public int? CapitalQ { get; set; }
    public int? CapitalR { get; set; }
}

public sealed class CoordDto
{
    public int Q { get; set; }
    public int R { get; set; }
}

public sealed class CapitalGoldDto
{
    public int Q { get; set; }
    public int R { get; set; }
    public int Gold { get; set; }
}
