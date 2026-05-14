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

    /// <summary>
    /// Name of the starting map this game was launched from, or null if
    /// it was a procedural (Random Map) game. Carried across save/load so
    /// the in-game label can keep showing "Map: foo" after a reload.
    /// </summary>
    public string? OriginMapName { get; }

    /// <summary>
    /// Highest claim-victory threshold (50/75/90) each human color has
    /// already dismissed this game. Empty for fresh games and for
    /// saves written before any tier was added. Saves written before
    /// the multi-tier change carried only a flat color list; those
    /// load with each color mapped to 50.
    /// </summary>
    public IReadOnlyDictionary<Color, int> ClaimVictoryPromptedHighestThreshold { get; }

    /// <summary>
    /// Authored tutorial accompanying this save, if any. Null for plain
    /// in-progress saves and starting maps; non-null when the file came
    /// from <c>user://tutorials/</c> or any v3 file that included the
    /// optional <c>"Tutorial"</c> block. v2 files always load with
    /// Tutorial = null.
    /// </summary>
    public Tutorial? Tutorial { get; }

    /// <summary>
    /// Recorded replay payload from when this game was saved. Null for
    /// v2/v3 saves (pre-feature), starting maps, and v4 saves whose
    /// controller had no replay data. Non-null v4 saves carry the
    /// game-start snapshot plus every recorded beat up to save time.
    /// </summary>
    public Replay? Replay { get; }

    public LoadedSave(
        GameState state,
        IReadOnlyList<Player> players,
        int masterSeed,
        int maxTurnNumber,
        string slotName,
        string? originMapName = null,
        IReadOnlyDictionary<Color, int>? claimVictoryPromptedHighestThreshold = null,
        Tutorial? tutorial = null,
        Replay? replay = null)
    {
        State = state;
        Players = players;
        MasterSeed = masterSeed;
        MaxTurnNumber = maxTurnNumber;
        SlotName = slotName;
        OriginMapName = originMapName;
        ClaimVictoryPromptedHighestThreshold = claimVictoryPromptedHighestThreshold
            ?? new Dictionary<Color, int>();
        Tutorial = tutorial;
        Replay = replay;
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
    public const int CurrentFormatVersion = 4;

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
        int maxTurnNumber,
        string? originMapName = null,
        IReadOnlyDictionary<Color, int>? claimVictoryPromptedHighestThreshold = null,
        Tutorial? tutorial = null,
        Replay? replay = null)
        => SerializeInternal(
            state, masterSeed, players, slotName, maxTurnNumber,
            includeKind: true, originMapName: originMapName,
            claimVictoryPromptedHighestThreshold: claimVictoryPromptedHighestThreshold,
            tutorial: tutorial,
            replay: replay);

    /// <summary>
    /// Serialize a starting map — same JSON format as <see cref="Serialize"/>,
    /// but the per-player <c>Kind</c> field is omitted. Editor maps don't
    /// commit to a roster; the Play Game config menu assigns roles at play
    /// time. Optional <paramref name="tutorial"/> attaches an authored
    /// tutorial to the file (used by <see cref="SaveStore.WriteTutorial"/>);
    /// regular starting maps pass null.
    /// </summary>
    public static string SerializeMap(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName,
        Tutorial? tutorial = null)
        => SerializeInternal(state, masterSeed, players, slotName,
            maxTurnNumber: int.MaxValue, includeKind: false,
            originMapName: null, claimVictoryPromptedHighestThreshold: null,
            tutorial: tutorial,
            replay: null);

    private static string SerializeInternal(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName,
        int maxTurnNumber,
        bool includeKind,
        string? originMapName,
        IReadOnlyDictionary<Color, int>? claimVictoryPromptedHighestThreshold,
        Tutorial? tutorial,
        Replay? replay)
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
            OriginMapName = originMapName,
            MasterSeed = masterSeed,
            TurnNumber = state.Turns.TurnNumber,
            CurrentPlayerIndex = state.Turns.CurrentPlayerIndex,
            MaxTurnNumber = maxTurnNumber,
            Players = SerializePlayers(players, includeKind),
            Tiles = SerializeTiles(state.Grid, indexByColor),
            Territories = SerializeTerritories(state.Territories, indexByColor),
            Gold = SerializeGold(state.Territories, state.Treasury),
            Water = SerializeWater(state.WaterCoords),
            ClaimVictoryPromptedHighestByColorHex =
                SerializeClaimVictoryPrompted(claimVictoryPromptedHighestThreshold),
            Tutorial = tutorial == null ? null : new TutorialDto
            {
                Title = tutorial.Title,
            },
            // A tutorial carries its own Replay; if the caller passed
            // a tutorial, write that Replay block. Otherwise honor an
            // explicit `replay` arg (regular in-progress saves).
            Replay = SerializeReplay(tutorial?.Replay ?? replay, indexByColor),
        };
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    /// <summary>
    /// Encode the prompted-tier dictionary as a hex→percent map. Returns
    /// null when empty so the field is omitted from JSON entirely (kept
    /// clean for fresh games and starting maps). New saves never write
    /// the legacy <c>ClaimVictoryPromptedColorHexes</c> field — it is
    /// read-only on the deserialize path for backward compatibility.
    /// </summary>
    private static Dictionary<string, int>? SerializeClaimVictoryPrompted(
        IReadOnlyDictionary<Color, int>? prompted)
    {
        if (prompted == null || prompted.Count == 0) return null;
        var dict = new Dictionary<string, int>(prompted.Count);
        foreach (KeyValuePair<Color, int> kvp in prompted)
        {
            dict[kvp.Key.ToHtml(includeAlpha: false)] = kvp.Value;
        }
        return dict;
    }

    public static LoadedSave Deserialize(string json)
    {
        SaveData? data = JsonSerializer.Deserialize<SaveData>(json, JsonOptions);
        if (data == null)
        {
            throw new InvalidOperationException("Save file is empty or malformed.");
        }
        // Accept v2, v3, v4. v2 predates the Tutorial block; v3 predates
        // the Replay block. Both load with the missing field as null.
        // The next breaking bump (v5, reserved for a non-additive
        // change) will need an explicit migration here.
        if (data.FormatVersion != 2 && data.FormatVersion != 3 && data.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported save format version {data.FormatVersion} " +
                $"(expected 2, 3, or {CurrentFormatVersion}).");
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
        IReadOnlyDictionary<Color, int> prompted = DeserializeClaimVictoryPrompted(
            data.ClaimVictoryPromptedHighestByColorHex,
            data.ClaimVictoryPromptedColorHexes);
        Replay? replay = DeserializeReplay(data.Replay, players);
        Tutorial? tutorial;
        if (data.Tutorial == null)
        {
            tutorial = null;
        }
        else
        {
            if (replay == null)
            {
                throw new InvalidOperationException(
                    "Tutorial block present without a Replay block — malformed save.");
            }
            tutorial = new Tutorial { Title = data.Tutorial.Title, Replay = replay };
        }
        return new LoadedSave(
            state, players, data.MasterSeed, data.MaxTurnNumber, data.SlotName,
            originMapName: data.OriginMapName,
            claimVictoryPromptedHighestThreshold: prompted,
            tutorial: tutorial,
            replay: replay);
    }

    /// <summary>
    /// Read precedence: prefer the per-color threshold map; fall back to
    /// the legacy flat color list (treating each entry as
    /// "prompted at 50% only"); empty if neither is present. The two
    /// fields are NOT additive — a save written by the multi-tier code
    /// only populates the new field, and we ignore the legacy field if
    /// the new one is present.
    /// </summary>
    private static IReadOnlyDictionary<Color, int> DeserializeClaimVictoryPrompted(
        Dictionary<string, int>? perColor, List<string>? legacyHexes)
    {
        var dict = new Dictionary<Color, int>();
        if (perColor != null && perColor.Count > 0)
        {
            foreach (KeyValuePair<string, int> kvp in perColor)
            {
                dict[new Color(kvp.Key)] = kvp.Value;
            }
            return dict;
        }
        if (legacyHexes != null && legacyHexes.Count > 0)
        {
            foreach (string hex in legacyHexes)
            {
                dict[new Color(hex)] = 50;
            }
        }
        return dict;
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

    // --- Replay ---------------------------------------------------------

    private static ReplayDto? SerializeReplay(Replay? replay, Dictionary<Color, int> indexByColor)
    {
        if (replay == null) return null;
        var tileDtos = new List<TileDto>();
        foreach ((HexCoord coord, Color color, HexOccupant? occupant) in replay.InitialSnapshot.EnumerateTiles())
        {
            tileDtos.Add(new TileDto
            {
                Q = coord.Q,
                R = coord.R,
                OwnerIndex = ColorToOwnerIndex(color, indexByColor),
                Occupant = SerializeOccupant(occupant, indexByColor),
            });
        }
        var goldDtos = new List<CapitalGoldDto>();
        foreach ((HexCoord cap, int gold) in replay.InitialSnapshot.EnumerateGold())
        {
            goldDtos.Add(new CapitalGoldDto { Q = cap.Q, R = cap.R, Gold = gold });
        }
        var territoryDtos = new List<TerritoryDto>();
        foreach (Territory t in replay.InitialSnapshot.Territories)
        {
            var coordDtos = new List<CoordDto>(t.Coords.Count);
            foreach (HexCoord c in t.Coords)
            {
                coordDtos.Add(new CoordDto { Q = c.Q, R = c.R });
            }
            territoryDtos.Add(new TerritoryDto
            {
                OwnerIndex = ColorToOwnerIndex(t.Owner, indexByColor),
                Coords = coordDtos,
                CapitalQ = t.Capital?.Q,
                CapitalR = t.Capital?.R,
            });
        }
        return new ReplayDto
        {
            InitialState = new InitialStateDto
            {
                TurnNumber = replay.InitialTurnNumber,
                CurrentPlayerIndex = replay.InitialCurrentPlayerIndex,
                Tiles = tileDtos,
                Territories = territoryDtos,
                Gold = goldDtos,
            },
            Beats = SerializeReplayBeats(replay.Beats),
        };
    }

    private static Replay? DeserializeReplay(ReplayDto? dto, IReadOnlyList<Player> players)
    {
        if (dto == null) return null;
        var grid = new HexGrid();
        foreach (TileDto t in dto.InitialState.Tiles)
        {
            Color color = OwnerIndexToColor(t.OwnerIndex, players);
            grid.Add(new HexTile(new HexCoord(t.Q, t.R), color)
            {
                Occupant = DeserializeOccupant(t.Occupant, players),
            });
        }
        var territories = new List<Territory>(dto.InitialState.Territories.Count);
        foreach (TerritoryDto td in dto.InitialState.Territories)
        {
            Color owner = OwnerIndexToColor(td.OwnerIndex, players);
            var coords = new List<HexCoord>(td.Coords.Count);
            foreach (CoordDto c in td.Coords) coords.Add(new HexCoord(c.Q, c.R));
            HexCoord? capital = td.CapitalQ.HasValue && td.CapitalR.HasValue
                ? new HexCoord(td.CapitalQ.Value, td.CapitalR.Value)
                : (HexCoord?)null;
            territories.Add(new Territory(owner, coords, capital));
        }
        var treasury = new Treasury();
        foreach (CapitalGoldDto g in dto.InitialState.Gold)
        {
            treasury.SetGold(new HexCoord(g.Q, g.R), g.Gold);
        }
        GameStateSnapshot snapshot = GameStateSnapshot.Capture(grid, treasury, territories);
        IReadOnlyList<ReplayBeat> beats = DeserializeReplayBeats(dto.Beats);
        return new Replay(snapshot,
            initialTurnNumber: dto.InitialState.TurnNumber,
            initialCurrentPlayerIndex: dto.InitialState.CurrentPlayerIndex,
            beats: beats);
    }

    private static List<ReplayBeatDto> SerializeReplayBeats(IReadOnlyList<ReplayBeat> beats)
    {
        var dtos = new List<ReplayBeatDto>(beats.Count);
        foreach (ReplayBeat beat in beats)
        {
            ReplayBeatDto dto = beat switch
            {
                ReplayMoveBeat mv => new ReplayBeatDto
                {
                    Kind = "Move",
                    FromQ = mv.From.Q,
                    FromR = mv.From.R,
                    ToQ = mv.To.Q,
                    ToR = mv.To.R,
                },
                ReplayBuyBeat bu => new ReplayBeatDto
                {
                    Kind = "BuyUnit",
                    CapitalQ = bu.Capital.Q,
                    CapitalR = bu.Capital.R,
                    ToQ = bu.To.Q,
                    ToR = bu.To.R,
                    Level = bu.Level.ToString(),
                },
                ReplayBuildTowerBeat bt => new ReplayBeatDto
                {
                    Kind = "BuildTower",
                    CapitalQ = bt.Capital.Q,
                    CapitalR = bt.Capital.R,
                    ToQ = bt.To.Q,
                    ToR = bt.To.R,
                },
                ReplayEndTurnBeat _ => new ReplayBeatDto { Kind = "EndTurn" },
                ReplayLongPressRallyBeat rally => new ReplayBeatDto
                {
                    Kind = "LongPressRally",
                    ToQ = rally.Target.Q,
                    ToR = rally.Target.R,
                },
                ReplayClaimVictoryBeat cv => new ReplayBeatDto
                {
                    Kind = "ClaimVictory",
                    ThresholdPercent = cv.ThresholdPercent,
                },
                ReplayDismissClaimBeat dcv => new ReplayBeatDto
                {
                    Kind = "DismissClaim",
                    ThresholdPercent = dcv.ThresholdPercent,
                },
                ReplayDismissDefeatBeat _ => new ReplayBeatDto { Kind = "DismissDefeat" },
                ReplayDisplayTextBeat dt => new ReplayBeatDto
                {
                    Kind = "DisplayText",
                    Text = dt.Text,
                },
                _ => throw new InvalidOperationException(
                    $"Unknown replay beat kind for serialization: {beat.GetType()}"),
            };
            dto.Index = beat.Index;
            dto.Turn = beat.Turn;
            dto.Actor = beat.Actor;
            dtos.Add(dto);
        }
        return dtos;
    }

    private static IReadOnlyList<ReplayBeat> DeserializeReplayBeats(List<ReplayBeatDto>? dtos)
    {
        if (dtos == null) return Array.Empty<ReplayBeat>();
        var beats = new List<ReplayBeat>(dtos.Count);
        foreach (ReplayBeatDto dto in dtos)
        {
            ReplayBeat beat = dto.Kind switch
            {
                "Move" => new ReplayMoveBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    From = new HexCoord(
                        dto.FromQ ?? throw new InvalidOperationException("Move beat missing FromQ"),
                        dto.FromR ?? throw new InvalidOperationException("Move beat missing FromR")),
                    To = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("Move beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("Move beat missing ToR")),
                },
                "BuyUnit" => new ReplayBuyBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Capital = new HexCoord(
                        dto.CapitalQ ?? throw new InvalidOperationException("BuyUnit beat missing CapitalQ"),
                        dto.CapitalR ?? throw new InvalidOperationException("BuyUnit beat missing CapitalR")),
                    To = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("BuyUnit beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("BuyUnit beat missing ToR")),
                    Level = Enum.Parse<UnitLevel>(dto.Level ?? throw new InvalidOperationException("BuyUnit beat missing Level")),
                },
                "BuildTower" => new ReplayBuildTowerBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Capital = new HexCoord(
                        dto.CapitalQ ?? throw new InvalidOperationException("BuildTower beat missing CapitalQ"),
                        dto.CapitalR ?? throw new InvalidOperationException("BuildTower beat missing CapitalR")),
                    To = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("BuildTower beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("BuildTower beat missing ToR")),
                },
                "EndTurn" => new ReplayEndTurnBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                },
                "LongPressRally" => new ReplayLongPressRallyBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Target = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("LongPressRally beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("LongPressRally beat missing ToR")),
                },
                "ClaimVictory" => new ReplayClaimVictoryBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    ThresholdPercent = dto.ThresholdPercent
                        ?? throw new InvalidOperationException("ClaimVictory beat missing ThresholdPercent"),
                },
                "DismissClaim" => new ReplayDismissClaimBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    ThresholdPercent = dto.ThresholdPercent
                        ?? throw new InvalidOperationException("DismissClaim beat missing ThresholdPercent"),
                },
                "DismissDefeat" => new ReplayDismissDefeatBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                },
                "DisplayText" => new ReplayDisplayTextBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Text = dto.Text ?? "",
                },
                _ => throw new InvalidOperationException(
                    $"Unknown replay beat kind in save: {dto.Kind}"),
            };
            beats.Add(beat);
        }
        return beats;
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

    /// <summary>
    /// Name of the starting map this game descended from. Null for
    /// procedural (Random Map) games and absent in saves written
    /// before this field was added.
    /// </summary>
    public string? OriginMapName { get; set; }

    public int MasterSeed { get; set; }
    public int TurnNumber { get; set; }
    public int CurrentPlayerIndex { get; set; }
    public int MaxTurnNumber { get; set; }
    public List<PlayerDto> Players { get; set; } = new();
    public List<TileDto> Tiles { get; set; } = new();
    public List<TerritoryDto> Territories { get; set; } = new();
    public List<CapitalGoldDto> Gold { get; set; } = new();
    public List<CoordDto> Water { get; set; } = new();

    /// <summary>
    /// Legacy field: flat list of human color hex strings that dismissed
    /// the End-Turn claim-victory prompt back when there was a single
    /// 50% threshold. Read-only — new saves never populate it. Loaders
    /// fall back to this when the per-tier field below is absent and
    /// treat each entry as "prompted at 50%".
    /// </summary>
    public List<string>? ClaimVictoryPromptedColorHexes { get; set; }

    /// <summary>
    /// Highest claim-victory tier (50/75/90) each human color hex has
    /// already dismissed this game. Null/missing in older saves and
    /// when the dictionary is empty (kept clean for fresh games).
    /// Supersedes <see cref="ClaimVictoryPromptedColorHexes"/>.
    /// </summary>
    public Dictionary<string, int>? ClaimVictoryPromptedHighestByColorHex { get; set; }

    /// <summary>
    /// Authored tutorial accompanying this save. Null/missing in v2
    /// files and in v3 saves that aren't tutorials. Present when the
    /// file lives under <c>user://tutorials/</c>.
    /// </summary>
    public TutorialDto? Tutorial { get; set; }

    /// <summary>
    /// Recorded replay payload (initial snapshot + beat log) saved
    /// alongside the in-progress game state in v4+. Null/missing in
    /// v2/v3 saves and in v4 saves whose controller had no replay
    /// data. Starting maps and tutorials never include it.
    /// </summary>
    public ReplayDto? Replay { get; set; }
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

public sealed class TutorialDto
{
    public string Title { get; set; } = "";
}

/// <summary>
/// Replay payload DTO: the captured game-start snapshot plus every
/// recorded beat. Embedded inside <see cref="SaveData.Replay"/> on
/// v4+ saves. Mirrors the tutorial-<c>Beats</c> shape — kind-
/// discriminated DTOs round-trip through hand-written switches in
/// <see cref="SaveSerializer"/>.
/// </summary>
public sealed class ReplayDto
{
    public InitialStateDto InitialState { get; set; } = new();
    public List<ReplayBeatDto> Beats { get; set; } = new();
}

public sealed class InitialStateDto
{
    public int TurnNumber { get; set; }
    public int CurrentPlayerIndex { get; set; }
    public List<TileDto> Tiles { get; set; } = new();
    public List<TerritoryDto> Territories { get; set; } = new();
    public List<CapitalGoldDto> Gold { get; set; } = new();
}

/// <summary>
/// Kind-discriminated DTO for a single recorded <see cref="ReplayBeat"/>.
/// Every kind-specific field is nullable on the DTO; the
/// SerializeReplayBeats / DeserializeReplayBeats switches map between
/// these and the typed records.
/// </summary>
public sealed class ReplayBeatDto
{
    public int Index { get; set; }
    public int Turn { get; set; }
    public int Actor { get; set; }
    public string Kind { get; set; } = "";

    public int? FromQ { get; set; }
    public int? FromR { get; set; }
    public int? ToQ { get; set; }
    public int? ToR { get; set; }
    public int? CapitalQ { get; set; }
    public int? CapitalR { get; set; }
    public string? Level { get; set; }
    public int? ThresholdPercent { get; set; }
    /// <summary>Authored narration for <see cref="ReplayDisplayTextBeat"/>.</summary>
    public string? Text { get; set; }
}
