using System;
using System.Collections.Generic;
using System.Text.Json;
using FourExHex.Model;

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
    /// True iff the file carried explicit per-player kinds (issue #70). In-progress
    /// saves and post-#70 starting maps bake kinds; pre-#70 starting maps did not.
    /// The starting-map load path uses this to decide between the baked roster and
    /// the legacy default (6 players, Red human, the rest Computer, all Soldier).
    /// </summary>
    public bool MapHasBakedKinds { get; }

    /// <summary>
    /// Name of the starting map this game was launched from, or null if
    /// it was a procedural (Random Map) game. Carried across save/load so
    /// the in-game label can keep showing "Map: foo" after a reload.
    /// </summary>
    public string? OriginMapName { get; }

    /// <summary>
    /// Highest claim-victory threshold (50/75/90) each human player has
    /// already dismissed this game. Empty for fresh games and for
    /// saves written before any tier was added. Saves written before
    /// the multi-tier change carried only a flat color list; those
    /// load with each player mapped to 50.
    /// </summary>
    public IReadOnlyDictionary<PlayerId, int> ClaimVictoryPromptedHighestThreshold { get; }

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

    /// <summary>
    /// Campaign level index (0..255) this game was launched from, or
    /// null for freeform games and pre-v8 saves. Restored into
    /// <see cref="GameSettings.CampaignLevel"/> on load so resuming an
    /// autosaved campaign game can still record a win (issue #2).
    /// </summary>
    public int? CampaignLevel { get; }

    public LoadedSave(
        GameState state,
        IReadOnlyList<Player> players,
        int masterSeed,
        int maxTurnNumber,
        string slotName,
        string? originMapName = null,
        IReadOnlyDictionary<PlayerId, int>? claimVictoryPromptedHighestThreshold = null,
        Tutorial? tutorial = null,
        Replay? replay = null,
        int? campaignLevel = null,
        bool mapHasBakedKinds = false)
    {
        State = state;
        Players = players;
        MasterSeed = masterSeed;
        MaxTurnNumber = maxTurnNumber;
        SlotName = slotName;
        OriginMapName = originMapName;
        MapHasBakedKinds = mapHasBakedKinds;
        ClaimVictoryPromptedHighestThreshold = claimVictoryPromptedHighestThreshold
            ?? new Dictionary<PlayerId, int>();
        Tutorial = tutorial;
        Replay = replay;
        CampaignLevel = campaignLevel;
    }
}

/// <summary>
/// Save-game serializer. Pure, Godot-free C# so it is exercised by
/// unit tests; the file I/O layer (SaveStore) wraps this.
///
/// Format: System.Text.Json over hand-written DTOs. Mirrors the explicit-
/// switch style of <see cref="GameStateSnapshot.CloneOccupant"/> rather
/// than relying on polymorphic deserialization, so the format is easy
/// to reason about and round-trip.
///
/// Tile and unit owners are stored as a player index (<see cref="PlayerId.Index"/>),
/// not a color, matching the "tiles always belong to a player" invariant
/// of real saved games. The player roster's display hex is carried
/// separately (<see cref="PlayerDto.ColorHex"/>) for the legacy
/// claim-victory-by-hex fields only.
/// </summary>
public static class SaveSerializer
{
    /// <summary>
    /// Bump on any breaking schema change. <see cref="Deserialize"/>
    /// rejects mismatched values rather than attempting migration.
    /// </summary>
    public const int CurrentFormatVersion = 14;

    public static string Serialize(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName,
        int maxTurnNumber,
        string? originMapName = null,
        IReadOnlyDictionary<PlayerId, int>? claimVictoryPromptedHighestThreshold = null,
        Tutorial? tutorial = null,
        Replay? replay = null,
        int? campaignLevel = null)
        => SerializeInternal(
            state, masterSeed, players, slotName, maxTurnNumber,
            includeKind: true, originMapName: originMapName,
            claimVictoryPromptedHighestThreshold: claimVictoryPromptedHighestThreshold,
            tutorial: tutorial,
            replay: replay,
            campaignLevel: campaignLevel);

    /// <summary>
    /// Serialize a starting map — same JSON format as <see cref="Serialize"/>.
    /// Since #70 the per-color kind (Human/Computer/None) and difficulty are
    /// baked in (<paramref name="players"/> carries the chosen roster, including
    /// <see cref="PlayerKind.None"/> slots), so a loaded map restores its exact
    /// player setup. Pre-#70 maps omitted these fields; they still load (every
    /// color defaults via the legacy default roster — see
    /// <see cref="LoadedSave.MapHasBakedKinds"/>). Optional
    /// <paramref name="tutorial"/> attaches an authored tutorial to the file
    /// (used by <see cref="SaveStore.WriteTutorial"/>); regular maps pass null.
    /// </summary>
    public static string SerializeMap(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName,
        Tutorial? tutorial = null)
        => SerializeInternal(state, masterSeed, players, slotName,
            maxTurnNumber: int.MaxValue, includeKind: true,
            originMapName: null, claimVictoryPromptedHighestThreshold: null,
            tutorial: tutorial,
            replay: null,
            campaignLevel: null);

    private static string SerializeInternal(
        GameState state,
        int masterSeed,
        IReadOnlyList<Player> players,
        string slotName,
        int maxTurnNumber,
        bool includeKind,
        string? originMapName,
        IReadOnlyDictionary<PlayerId, int>? claimVictoryPromptedHighestThreshold,
        Tutorial? tutorial,
        Replay? replay,
        int? campaignLevel)
    {
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
            Tiles = SerializeTiles(state.Grid),
            Territories = SerializeTerritories(state.Territories),
            Gold = SerializeGold(state.Territories, state.Treasury),
            Water = SerializeWater(state.WaterCoords),
            ClaimVictoryPromptedHighestByPlayerIndex =
                SerializeClaimVictoryPrompted(claimVictoryPromptedHighestThreshold),
            Tutorial = tutorial == null ? null : new TutorialDto
            {
                Title = tutorial.Title,
            },
            // A tutorial carries its own Replay; if the caller passed
            // a tutorial, write that Replay block. Otherwise honor an
            // explicit `replay` arg (regular in-progress saves).
            Replay = SerializeReplay(tutorial?.Replay ?? replay),
            CampaignLevel = campaignLevel,
            // Null (omitted) for Freeform so every existing freeform save's
            // wire format is unchanged; only Rising Tides games carry it.
            Mode = state.Mode == GameMode.Freeform ? null : state.Mode,
            // The locked tide forecast for the current turn (issue #85). Null
            // (omitted) when empty — every freeform save and any Rising Tides save
            // taken between turns — so the wire format stays clean.
            PendingTide = SerializePendingTide(state.PendingTide),
        };
        // Source-gen path (FourExHexJsonContext) so this works under iOS AOT,
        // where reflection-based serialization is disabled. The context's
        // [JsonSourceGenerationOptions] attribute carries the same
        // WriteIndented + IgnoreNulls options the reflection path historically
        // used, so the JSON wire format is unchanged.
        return JsonSerializer.Serialize(data, FourExHexJsonContext.Default.SaveData);
    }

    /// <summary>
    /// Encode the prompted-tier dictionary as a hex→percent map. Returns
    /// null when empty so the field is omitted from JSON entirely (kept
    /// clean for fresh games and starting maps). v5 keys by player index
    /// (palette-independent); the legacy color-hex fields are read-only
    /// on the deserialize path for v2..v4 backward compatibility.
    /// </summary>
    private static Dictionary<int, int>? SerializeClaimVictoryPrompted(
        IReadOnlyDictionary<PlayerId, int>? prompted)
    {
        if (prompted == null || prompted.Count == 0) return null;
        var dict = new Dictionary<int, int>(prompted.Count);
        foreach (KeyValuePair<PlayerId, int> kvp in prompted)
        {
            if (kvp.Key.IsNone) continue;
            dict[kvp.Key.Index] = kvp.Value;
        }
        return dict;
    }

    public static LoadedSave Deserialize(string json)
    {
        SaveData? data = JsonSerializer.Deserialize(json, FourExHexJsonContext.Default.SaveData);
        if (data == null)
        {
            throw new InvalidOperationException("Save file is empty or malformed.");
        }
        // Accept v2..v8. v2 predates the Tutorial block; v3 predates the
        // Replay block; v4 keyed claim-victory by color hex and threw on
        // an owner not in the roster. v5 keys claim-victory by player
        // index and encodes "no owner" as OwnerIndex -1. v6 renamed the
        // unit levels (Peasant/Spearman/Knight/Baron →
        // Recruit/Soldier/Captain/Commander); pre-v6 level names still
        // load via ParseUnitLevel. Older files also load their
        // claim-victory data via the legacy color-hex path below.
        // v7 added per-player Difficulty; v8 added the optional
        // CampaignLevel pointer (issue #2); v9 added per-tile IsGold
        // (issue #45); v10 added per-tile IsMountain (issue #37) — all
        // default-absent, so pre-bump files load unchanged (missing IsGold /
        // IsMountain → false, an ordinary tile). v12 added the optional
        // Rising Tides Mode flag (issue #56) — absent/null loads as Freeform.
        // v13 made gold and mountain mutually exclusive (issue #81): a tile is
        // gold OR mountain, never both, so a legacy tile carrying both flags is
        // normalized to mountain-only on load (mountain wins) below. v14 added the
        // optional Rising Tides PendingTide forecast (issue #85) — absent/null
        // loads as an empty forecast.
        if (data.FormatVersion is < 2 or > CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported save format version {data.FormatVersion} " +
                $"(expected 2..{CurrentFormatVersion}).");
        }

        IReadOnlyList<Player> players = DeserializePlayers(data.Players);
        bool mapHasBakedKinds = AnyBakedKinds(data.Players);
        var turnState = new TurnState(
            players,
            currentPlayerIndex: data.CurrentPlayerIndex,
            turnNumber: data.TurnNumber);

        var grid = new HexGrid();
        int normalizedGoldMountain = 0;
        foreach (TileDto tile in data.Tiles)
        {
            PlayerId owner = OwnerIndexToId(tile.OwnerIndex, players);
            // Gold and mountain are mutually exclusive (issue #81). A legacy
            // (pre-v13) save could carry both flags on one tile; mountain wins,
            // so set the single TerrainFeature explicitly rather than relying on
            // accessor ordering.
            TerrainFeature feature =
                tile.IsMountain ? TerrainFeature.Mountain :
                tile.IsGold ? TerrainFeature.Gold :
                TerrainFeature.None;
            if (tile.IsGold && tile.IsMountain) normalizedGoldMountain++;
            var hexTile = new HexTile(new HexCoord(tile.Q, tile.R), owner)
            {
                Occupant = DeserializeOccupant(tile.Occupant, players),
                Feature = feature,
            };
            grid.Add(hexTile);
        }
        if (normalizedGoldMountain > 0)
        {
            Log.Info(Log.LogCategory.MapGen,
                $"[save] normalized {normalizedGoldMountain} legacy gold+mountain " +
                $"tile(s) to mountain-only (issue #81)");
        }

        IReadOnlyList<Territory> territories = DeserializeTerritories(data.Territories, players);

        var treasury = new Treasury();
        foreach (CapitalGoldDto g in data.Gold)
        {
            treasury.SetGold(new HexCoord(g.Q, g.R), g.Gold);
        }

        IReadOnlySet<HexCoord> waterCoords = DeserializeWater(data.Water);
        var state = new GameState(
            grid, territories, players, turnState, treasury, waterCoords,
            mode: data.Mode ?? GameMode.Freeform)
        {
            // Restore the locked tide forecast (issue #85); empty for freeform
            // and pre-v14 saves so the reloaded game telegraphs/applies nothing.
            PendingTide = DeserializePendingTide(data.PendingTide),
        };
        IReadOnlyDictionary<PlayerId, int> prompted = DeserializeClaimVictoryPrompted(
            data.ClaimVictoryPromptedHighestByPlayerIndex,
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
            replay: replay,
            campaignLevel: data.CampaignLevel,
            mapHasBakedKinds: mapHasBakedKinds);
    }

    /// <summary>
    /// Read precedence (the three fields are NOT additive — the first
    /// present one wins):
    ///   1. v5 <c>ByPlayerIndex</c> — palette-independent, authoritative.
    ///   2. v4 <c>ByColorHex</c> — map each stored hex back to its slot.
    ///   3. v2/v3 flat <c>ColorHexes</c> — each entry = "prompted at 50%".
    /// Hexes that match no current palette slot are dropped (cosmetic
    /// palette drift between builds — same lossy behavior as the old
    /// Godot.Color round-trip). Empty if none present.
    /// </summary>
    private static IReadOnlyDictionary<PlayerId, int> DeserializeClaimVictoryPrompted(
        Dictionary<int, int>? byIndex,
        Dictionary<string, int>? perColor,
        List<string>? legacyHexes)
    {
        var dict = new Dictionary<PlayerId, int>();
        if (byIndex != null && byIndex.Count > 0)
        {
            foreach (KeyValuePair<int, int> kvp in byIndex)
            {
                if (kvp.Key >= 0) dict[PlayerId.FromIndex(kvp.Key)] = kvp.Value;
            }
            return dict;
        }
        if (perColor != null && perColor.Count > 0)
        {
            foreach (KeyValuePair<string, int> kvp in perColor)
            {
                if (TryPlayerForHex(kvp.Key, out PlayerId id)) dict[id] = kvp.Value;
            }
            return dict;
        }
        if (legacyHexes != null && legacyHexes.Count > 0)
        {
            foreach (string hex in legacyHexes)
            {
                if (TryPlayerForHex(hex, out PlayerId id)) dict[id] = 50;
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

    // --- Pending tide forecast (issue #85) -------------------------------

    private static List<TideStepDto>? SerializePendingTide(IReadOnlyList<TideStep> plan)
    {
        if (plan.Count == 0) return null; // omit when empty (most saves)
        var dtos = new List<TideStepDto>(plan.Count);
        foreach (TideStep step in plan)
        {
            dtos.Add(new TideStepDto { Q = step.Coord.Q, R = step.Coord.R, DemoteOnly = step.DemoteOnly });
        }
        return dtos;
    }

    private static IReadOnlyList<TideStep> DeserializePendingTide(List<TideStepDto>? dtos)
    {
        if (dtos == null || dtos.Count == 0) return System.Array.Empty<TideStep>();
        var plan = new List<TideStep>(dtos.Count);
        foreach (TideStepDto dto in dtos)
        {
            plan.Add(new TideStep(new HexCoord(dto.Q, dto.R), dto.DemoteOnly));
        }
        return plan;
    }

    // --- Players ---------------------------------------------------------

    private static List<PlayerDto> SerializePlayers(IReadOnlyList<Player> players, bool includeKind)
    {
        var dtos = new List<PlayerDto>(players.Count);
        foreach (Player p in players)
        {
            // Index and color track the player's SLOT (PlayerId.Index), not its
            // position in the (possibly compacted) roster list — a 2–6 player
            // game keeps each survivor on its own color (issue #70). For a full
            // 6-player roster slot == list position, so the wire format is
            // byte-identical to the pre-#70 output.
            int slot = p.Id.Index;
            dtos.Add(new PlayerDto
            {
                Index = slot,
                Name = p.Name,
                ColorHex = GameSettings.PlayerConfig[slot].Hex,
                // Null is omitted from JSON via JsonOptions, so pre-#70 starting
                // maps had no per-color role baked into the file.
                Kind = includeKind ? p.Kind.ToString() : null,
                Difficulty = includeKind ? p.Difficulty.ToString() : null,
            });
        }
        return dtos;
    }

    /// <summary>True iff any player dto carried an explicit kind — i.e. an
    /// in-progress save or a post-#70 starting map (vs. a pre-#70 map that
    /// baked no roles). Drives <see cref="LoadedSave.MapHasBakedKinds"/>.</summary>
    private static bool AnyBakedKinds(List<PlayerDto> dtos)
    {
        foreach (PlayerDto dto in dtos)
        {
            if (!string.IsNullOrEmpty(dto.Kind)) return true;
        }
        return false;
    }

    private static IReadOnlyList<Player> DeserializePlayers(List<PlayerDto> dtos)
    {
        var list = new List<Player>(dtos.Count);
        foreach (PlayerDto dto in dtos)
        {
            PlayerKind kind = ParsePlayerKind(dto.Kind);
            // A None slot is not a live player (issue #70): drop it so the
            // active roster compacts. Tiles owned by it never appear in a
            // valid file (the editor's save validation enforces that), and
            // OwnerIndexToId resolves any stray owner to neutral defensively.
            if (kind == PlayerKind.None) continue;
            Difficulty difficulty = ParseDifficulty(dto.Difficulty);
            list.Add(new Player(dto.Name, PlayerId.FromIndex(dto.Index), kind, difficulty));
        }
        return list;
    }

    /// <summary>
    /// Map a saved <see cref="PlayerDto.Kind"/> string to a
    /// <see cref="PlayerKind"/>. Missing/empty = starting map: use Human
    /// as a placeholder (the play-scene load path overrides from
    /// GameSettings). Legacy saves predate the {Human, Computer} collapse
    /// and stored the old AiKind names "Random"/"Heuristic" — both map to
    /// Computer, the game's only AI.
    /// </summary>
    private static PlayerKind ParsePlayerKind(string? kind)
    {
        if (string.IsNullOrEmpty(kind))
            return PlayerKind.Human;
        return kind switch
        {
            "Random" or "Heuristic" => PlayerKind.Computer,
            _ => Enum.Parse<PlayerKind>(kind),
        };
    }

    /// <summary>
    /// Map a saved <see cref="PlayerDto.Difficulty"/> string to a
    /// <see cref="Difficulty"/>. Missing/empty = starting map or pre-v7 save:
    /// default to <see cref="Difficulty.Soldier"/>.
    /// </summary>
    private static Difficulty ParseDifficulty(string? difficulty)
    {
        if (string.IsNullOrEmpty(difficulty))
            return Difficulty.Soldier;
        return Enum.Parse<Difficulty>(difficulty);
    }

    // --- Tiles -----------------------------------------------------------

    private static List<TileDto> SerializeTiles(HexGrid grid)
    {
        var dtos = new List<TileDto>();
        foreach (HexTile tile in grid.Tiles)
        {
            dtos.Add(new TileDto
            {
                Q = tile.Coord.Q,
                R = tile.Coord.R,
                OwnerIndex = IdToOwnerIndex(tile.Owner),
                Occupant = SerializeOccupant(tile.Occupant),
                IsGold = tile.IsGold,
                IsMountain = tile.IsMountain,
            });
        }
        return dtos;
    }

    // --- Occupants -------------------------------------------------------

    private static OccupantDto? SerializeOccupant(HexOccupant? occupant)
    {
        return occupant switch
        {
            null => null,
            Unit u => new OccupantDto
            {
                Type = "Unit",
                OwnerIndex = IdToOwnerIndex(u.Owner),
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
                OwnerIndexToId(dto.OwnerIndex ?? 0, players),
                ParseUnitLevel(dto.Level ?? nameof(UnitLevel.Recruit)))
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
        IReadOnlyList<Territory> territories)
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
                OwnerIndex = IdToOwnerIndex(t.Owner),
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
            PlayerId owner = OwnerIndexToId(dto.OwnerIndex, players);
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

    private static ReplayDto? SerializeReplay(Replay? replay)
    {
        if (replay == null) return null;
        var tileDtos = new List<TileDto>();
        foreach ((HexCoord coord, PlayerId owner, HexOccupant? occupant, bool isGold, bool isMountain) in replay.InitialSnapshot.EnumerateTiles())
        {
            tileDtos.Add(new TileDto
            {
                Q = coord.Q,
                R = coord.R,
                OwnerIndex = IdToOwnerIndex(owner),
                Occupant = SerializeOccupant(occupant),
                IsGold = isGold,
                IsMountain = isMountain,
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
                OwnerIndex = IdToOwnerIndex(t.Owner),
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
            PlayerId owner = OwnerIndexToId(t.OwnerIndex, players);
            grid.Add(new HexTile(new HexCoord(t.Q, t.R), owner)
            {
                Occupant = DeserializeOccupant(t.Occupant, players),
                IsGold = t.IsGold,
                IsMountain = t.IsMountain,
            });
        }
        var territories = new List<Territory>(dto.InitialState.Territories.Count);
        foreach (TerritoryDto td in dto.InitialState.Territories)
        {
            PlayerId owner = OwnerIndexToId(td.OwnerIndex, players);
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
                    Level = ParseUnitLevel(dto.Level ?? throw new InvalidOperationException("BuyUnit beat missing Level")),
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

    /// <summary>
    /// Parse a stored unit-level string into the current
    /// <see cref="UnitLevel"/>. Accepts the current names
    /// (Recruit/Soldier/Captain/Commander, v6+) and the pre-v6 names
    /// (Peasant/Spearman/Knight/Baron) so saves written before the unit
    /// rename still load — the levels are unchanged, only the names are.
    /// </summary>
    private static UnitLevel ParseUnitLevel(string name) => name switch
    {
        "Peasant" => UnitLevel.Recruit,
        "Spearman" => UnitLevel.Soldier,
        "Knight" => UnitLevel.Captain,
        "Baron" => UnitLevel.Commander,
        _ => Enum.Parse<UnitLevel>(name),
    };

    // --- Owner/index helpers --------------------------------------------

    // v5: PlayerId.None encodes as -1 (no owner). Real games never produce
    // a None-owned tile, but the format round-trips it defensively rather
    // than throwing (the v4 behavior).
    private static int IdToOwnerIndex(PlayerId id) => id.IsNone ? -1 : id.Index;

    // The stored owner index is a SLOT (PlayerId.Index), not a position in the
    // roster list. With a 2–6 player game the roster is compacted (e.g. slots
    // 0,2,4), so we match by slot rather than indexing the list (issue #70). For
    // a full 6-player roster slot == list position, so this is unchanged. A slot
    // absent from the active roster resolves to neutral defensively (a valid
    // file never references a None slot's color).
    private static PlayerId OwnerIndexToId(int index, IReadOnlyList<Player> players)
    {
        if (index < 0) return PlayerId.None;
        foreach (Player p in players)
        {
            if (p.Id.Index == index) return p.Id;
        }
        return PlayerId.None;
    }

    /// <summary>
    /// Hex strings that a palette slot used to ship with but no longer does,
    /// mapped to the slot index that inherited the identity. Consulted as a
    /// fallback by <see cref="TryPlayerForHex"/> so legacy v2..v4 claim-victory
    /// data keyed by a retired color still resolves to the right player.
    /// "e3bc3b" was slot 3's Yellow before it was recolored Brown (issue #44);
    /// "8a5a2b" was slot 3's Brown before it was re-tuned to a saturated
    /// chocolate for on-tile glyph contrast (issue #62).
    /// </summary>
    private static readonly Dictionary<string, int> RetiredHexAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["e3bc3b"] = 3,
            ["8a5a2b"] = 3,
        };

    /// <summary>
    /// Map a legacy v2..v4 stored hex back to the palette slot it names — the
    /// live <see cref="GameSettings.PlayerConfig"/> first, then the
    /// <see cref="RetiredHexAliases"/> table for colors a slot has since
    /// dropped. Returns false if neither matches (genuine palette drift — the
    /// entry is dropped, same lossy behavior as the old Godot.Color round-trip).
    /// </summary>
    private static bool TryPlayerForHex(string hex, out PlayerId id)
    {
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            if (string.Equals(GameSettings.PlayerConfig[i].Hex, hex,
                StringComparison.OrdinalIgnoreCase))
            {
                id = PlayerId.FromIndex(i);
                return true;
            }
        }
        if (RetiredHexAliases.TryGetValue(hex, out int aliasIndex))
        {
            id = PlayerId.FromIndex(aliasIndex);
            return true;
        }
        id = PlayerId.None;
        return false;
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
    /// Legacy v4 field: highest claim-victory tier (50/75/90) each human
    /// color hex had already dismissed. Read-only — v5 saves never write
    /// it. Loaders use it only when
    /// <see cref="ClaimVictoryPromptedHighestByPlayerIndex"/> is absent.
    /// </summary>
    public Dictionary<string, int>? ClaimVictoryPromptedHighestByColorHex { get; set; }

    /// <summary>
    /// v5: highest claim-victory tier (50/75/90) each player (by roster
    /// index) has already dismissed this game. Palette-independent —
    /// supersedes the two legacy color-hex fields. Null/missing in v2..v4
    /// and when the dictionary is empty (kept clean for fresh games).
    /// </summary>
    public Dictionary<int, int>? ClaimVictoryPromptedHighestByPlayerIndex { get; set; }

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

    /// <summary>
    /// v8: campaign level index (0..255) for games launched from the
    /// campaign screen (issue #2). Null/missing for freeform games and
    /// all pre-v8 saves.
    /// </summary>
    public int? CampaignLevel { get; set; }

    /// <summary>
    /// v12: the selectable game mode (issue #56). Null/missing for
    /// <see cref="GameMode.Freeform"/> games and all pre-v12 saves (which
    /// load as Freeform); present only for Rising Tides. The grown water set
    /// rides in <see cref="Water"/>, so no separate flood-progress field is
    /// needed. <see cref="WaterCoords"/> is recomputed on replay anyway.
    /// </summary>
    public GameMode? Mode { get; set; }

    /// <summary>
    /// v14: the locked Rising Tides forecast (issue #85) for the current player's
    /// turn — the tiles selected at turn start that demote/submerge at turn end.
    /// Null/missing for freeform games, Rising Tides saves taken between turns,
    /// and all pre-v14 saves (which load as an empty forecast).
    /// </summary>
    public List<TideStepDto>? PendingTide { get; set; }
}

public sealed class TideStepDto
{
    public int Q { get; set; }
    public int R { get; set; }

    /// <summary>True iff this step only demotes a mountain (a reprieve) rather
    /// than submerging the tile. See <see cref="TideStep.DemoteOnly"/>.</summary>
    public bool DemoteOnly { get; set; }
}

public sealed class PlayerDto
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "";
    /// <summary>
    /// AI kind name (one of <see cref="PlayerKind"/>). Null/missing in
    /// "starting map" exports from the editor — the role for each color
    /// is assigned at play time via the Play Game config menu, not
    /// baked into the map. Present in regular in-progress saves.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Per-player <see cref="Difficulty"/> name (issue #11). Null/missing in
    /// starting-map exports (assigned at the Play Game menu) and in pre-v7
    /// saves; both default to <see cref="Difficulty.Soldier"/> on load.
    /// </summary>
    public string? Difficulty { get; set; }
}

public sealed class TileDto
{
    public int Q { get; set; }
    public int R { get; set; }
    public int OwnerIndex { get; set; }
    public OccupantDto? Occupant { get; set; }

    /// <summary>Gold tile (issue #45). Absent in pre-v9 saves → false.</summary>
    public bool IsGold { get; set; }

    /// <summary>Mountain tile (issue #37). Absent in pre-v10 saves → false.</summary>
    public bool IsMountain { get; set; }
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
