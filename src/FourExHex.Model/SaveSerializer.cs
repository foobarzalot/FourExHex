// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
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
    /// True iff the file carries explicit per-player kinds. The starting-map
    /// load path uses this to decide between the baked roster and the legacy
    /// default (6 players, Red human, the rest Computer, all Soldier).
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
    /// already dismissed this game. Empty for fresh games; legacy
    /// flat-color-list saves load with each player mapped to 50.
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
    /// null for freeform games. Restored into
    /// <see cref="GameSettings.CampaignLevel"/> on load so resuming an
    /// autosaved campaign game can still record a win.
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
    public const int CurrentFormatVersion = 18;

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
    /// The per-color kind (Human/Computer/None) and difficulty are
    /// baked in (<paramref name="players"/> carries the chosen roster, including
    /// <see cref="PlayerKind.None"/> slots), so a loaded map restores its exact
    /// player setup. Maps without these fields still load (every
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
            // The locked tide forecast for the current turn. Null
            // (omitted) when empty — every freeform save and any Rising Tides save
            // taken between turns — so the wire format stays clean.
            PendingTide = SerializePendingTide(state.PendingTide),
            // Baked-at-creation flag for randomized capital/tide selection.
            // Absent in pre-15 saves → loads false → those games keep the old
            // deterministic placement so their replays reproduce.
            UseRandomizedSelection = state.UseRandomizedSelection,
            // Baked-at-creation flag for the origin-capital merge rule.
            // Absent in pre-18 saves → loads false → those games keep the
            // largest-wins merge rule so their replays reproduce.
            UseOriginMergeCapital = state.UseOriginMergeCapital,
            // Fog Of War: the human's explored coords. Null (omitted) when
            // empty — every non-fog save — so the wire format stays clean.
            Seen = SerializeSeen(state.Seen),
            // Viking Raiders: raiders at sea + wave-schedule cursors. All
            // null (omitted) at their defaults, so non-viking saves' wire
            // format is unchanged.
            VikingAtSea = SerializeVikingsAtSea(state.Vikings),
            VikingNextWave = state.Vikings.NextWaveIndex == 0
                ? null : state.Vikings.NextWaveIndex,
            VikingLastRound = state.Vikings.LastCompletedRound == 0
                ? null : state.Vikings.LastCompletedRound,
            VikingLastSpawnRound = state.Vikings.LastSpawnRound == 0
                ? null : state.Vikings.LastSpawnRound,
        };
        // Source-gen path (FourExHexJsonContext) so this works under iOS AOT,
        // where reflection-based serialization is disabled. The context's
        // [JsonSourceGenerationOptions] attribute carries WriteIndented +
        // IgnoreNulls so the JSON wire format matches.
        return JsonSerializer.Serialize(data, FourExHexJsonContext.Default.SaveData);
    }

    /// <summary>
    /// Encode the prompted-tier dictionary as a hex→percent map. Returns
    /// null when empty so the field is omitted from JSON entirely (kept
    /// clean for fresh games and starting maps). Keyed by player index
    /// (palette-independent); the legacy color-hex fields are read-only
    /// on the deserialize path for backward compatibility.
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
        // Accept any version in 2..CurrentFormatVersion. Newer fields are all
        // default-absent: missing fields fall back to legacy defaults on load
        // (no Tutorial/Replay block, claim-victory read via the legacy color-hex
        // path, missing IsGold/IsMountain → ordinary tile, missing Mode → Freeform,
        // missing PendingTide → empty forecast). A tile carrying both gold and
        // mountain flags is normalized to mountain-only below.
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
            // Gold and mountain are mutually exclusive. A save carrying both
            // flags on one tile resolves to mountain, so set the single
            // TerrainFeature explicitly rather than relying on accessor ordering.
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
                $"tile(s) to mountain-only");
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
            mode: data.Mode ?? GameMode.Freeform,
            useRandomizedSelection: data.UseRandomizedSelection,
            useOriginMergeCapital: data.UseOriginMergeCapital,
            // Restore the human's explored coords; empty for non-fog/legacy saves.
            seen: DeserializeSeen(data.Seen),
            // Restore raiders at sea + wave cursors; all-default for
            // non-viking / pre-17 saves.
            vikings: DeserializeVikings(data))
        {
            // Restore the locked tide forecast; empty for freeform saves so
            // the reloaded game telegraphs/applies nothing.
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
    ///   1. <c>ByPlayerIndex</c> — palette-independent, authoritative.
    ///   2. <c>ByColorHex</c> — map each stored hex back to its slot.
    ///   3. flat <c>ColorHexes</c> — each entry = "prompted at 50%".
    /// Hexes that match no current palette slot are dropped (cosmetic
    /// palette drift between builds). Empty if none present.
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

    // --- Fog Of War memory ----------------------------------------------

    // The human's explored (ever-seen) coords. Null (omitted) when empty so
    // non-fog saves carry no Seen field. Mirrors the water coord-set shape.
    private static List<CoordDto>? SerializeSeen(IReadOnlySet<HexCoord> seen)
    {
        if (seen.Count == 0) return null;
        var dtos = new List<CoordDto>(seen.Count);
        foreach (HexCoord c in seen)
        {
            dtos.Add(new CoordDto { Q = c.Q, R = c.R });
        }
        return dtos;
    }

    private static IReadOnlySet<HexCoord> DeserializeSeen(List<CoordDto>? dtos)
    {
        var set = new HashSet<HexCoord>();
        if (dtos == null) return set;
        foreach (CoordDto c in dtos)
        {
            set.Add(new HexCoord(c.Q, c.R));
        }
        return set;
    }

    // --- Pending tide forecast -------------------------------

    /// <summary>Viking raiders at sea, or null (field omitted) when none —
    /// every non-viking save and any viking save with an empty sea.</summary>
    private static List<SeaVikingDto>? SerializeVikingsAtSea(VikingState vikings)
    {
        if (vikings.AtSea.Count == 0) return null;
        return SerializeSpawnList(vikings.AtSea);
    }

    private static List<SeaVikingDto> SerializeSpawnList(IReadOnlyList<SeaViking> vikings)
    {
        var dtos = new List<SeaVikingDto>(vikings.Count);
        foreach (SeaViking v in vikings)
        {
            dtos.Add(new SeaVikingDto { Q = v.Coord.Q, R = v.Coord.R, Level = v.Level.ToString() });
        }
        return dtos;
    }

    private static IReadOnlyList<SeaViking> DeserializeSpawnList(List<SeaVikingDto>? dtos)
    {
        if (dtos == null) return Array.Empty<SeaViking>();
        var spawns = new List<SeaViking>(dtos.Count);
        foreach (SeaVikingDto dto in dtos)
        {
            spawns.Add(new SeaViking(
                new HexCoord(dto.Q, dto.R),
                ParseUnitLevel(dto.Level ?? throw new InvalidOperationException(
                    "Sea viking missing Level"))));
        }
        return spawns;
    }

    /// <summary>Rebuild the viking state from the save's fields; all-default
    /// (empty sea, cursors at 0) for non-viking and pre-17 saves.</summary>
    private static VikingState DeserializeVikings(SaveData data)
    {
        var vikings = new VikingState
        {
            NextWaveIndex = data.VikingNextWave ?? 0,
            LastCompletedRound = data.VikingLastRound ?? 0,
            LastSpawnRound = data.VikingLastSpawnRound ?? 0,
        };
        if (data.VikingAtSea != null)
        {
            foreach (SeaVikingDto dto in data.VikingAtSea)
            {
                vikings.AddAtSea(new SeaViking(
                    new HexCoord(dto.Q, dto.R),
                    ParseUnitLevel(dto.Level ?? throw new InvalidOperationException(
                        "Sea viking missing Level"))));
            }
        }
        return vikings;
    }

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
            // game keeps each survivor on its own color. For a full 6-player
            // roster slot == list position.
            int slot = p.Id.Index;
            dtos.Add(new PlayerDto
            {
                Index = slot,
                Name = p.Name,
                ColorHex = GameSettings.PlayerConfig[slot].Hex,
                // Null is omitted from JSON via JsonOptions, so maps with no
                // baked role write no Kind.
                Kind = includeKind ? p.Kind.ToString() : null,
                Difficulty = includeKind ? p.Difficulty.ToString() : null,
            });
        }
        return dtos;
    }

    /// <summary>True iff any player dto carried an explicit kind — i.e. a save
    /// or starting map that baked an explicit kind.
    /// Drives <see cref="LoadedSave.MapHasBakedKinds"/>.</summary>
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
            // A None slot is not a live player: drop it so the
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
    /// GameSettings). Legacy "Random"/"Heuristic" kind names both map to
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
            dtos.Add(ToTileDto(
                tile.Coord.Q, tile.Coord.R, tile.Owner, tile.Occupant, tile.IsGold, tile.IsMountain));
        }
        return dtos;
    }

    // Build a wire TileDto from a tile's fields — shared by the live-grid tile
    // loop and the replay initial-snapshot tile loop.
    private static TileDto ToTileDto(
        int q, int r, PlayerId owner, HexOccupant? occupant, bool isGold, bool isMountain)
        => new TileDto
        {
            Q = q,
            R = r,
            OwnerIndex = IdToOwnerIndex(owner),
            Occupant = SerializeOccupant(occupant),
            IsGold = isGold,
            IsMountain = isMountain,
        };

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
            dtos.Add(ToTerritoryDto(t));
        }
        return dtos;
    }

    // Build a wire TerritoryDto from a Territory — shared by the live-state and
    // replay initial-snapshot territory loops.
    private static TerritoryDto ToTerritoryDto(Territory t)
    {
        var coords = new List<CoordDto>(t.Coords.Count);
        foreach (HexCoord c in t.Coords)
        {
            coords.Add(new CoordDto { Q = c.Q, R = c.R });
        }
        return new TerritoryDto
        {
            OwnerIndex = IdToOwnerIndex(t.Owner),
            Coords = coords,
            CapitalQ = t.Capital?.Q,
            CapitalR = t.Capital?.R,
        };
    }

    private static IReadOnlyList<Territory> DeserializeTerritories(
        List<TerritoryDto> dtos, IReadOnlyList<Player> players)
    {
        var territories = new List<Territory>(dtos.Count);
        foreach (TerritoryDto dto in dtos)
        {
            territories.Add(FromTerritoryDto(dto, players));
        }
        return territories;
    }

    // Rebuild a Territory from a wire TerritoryDto — shared by the live-state
    // and replay initial-snapshot territory rebuild loops.
    private static Territory FromTerritoryDto(TerritoryDto dto, IReadOnlyList<Player> players)
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
        return new Territory(owner, coords, capital);
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
            tileDtos.Add(ToTileDto(coord.Q, coord.R, owner, occupant, isGold, isMountain));
        }
        var goldDtos = new List<CapitalGoldDto>();
        foreach ((HexCoord cap, int gold) in replay.InitialSnapshot.EnumerateGold())
        {
            goldDtos.Add(new CapitalGoldDto { Q = cap.Q, R = cap.R, Gold = gold });
        }
        var territoryDtos = new List<TerritoryDto>();
        foreach (Territory t in replay.InitialSnapshot.Territories)
        {
            territoryDtos.Add(ToTerritoryDto(t));
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
            territories.Add(FromTerritoryDto(td, players));
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
                ReplayVikingMoveBeat vm => new ReplayBeatDto
                {
                    Kind = "VikingMove",
                    FromQ = vm.From.Q,
                    FromR = vm.From.R,
                    ToQ = vm.To.Q,
                    ToR = vm.To.R,
                },
                ReplayVikingDisembarkBeat vd => new ReplayBeatDto
                {
                    Kind = "VikingDisembark",
                    FromQ = vd.Sea.Q,
                    FromR = vd.Sea.R,
                    ToQ = vd.Land.Q,
                    ToR = vd.Land.R,
                },
                ReplayVikingPerishBeat vp => new ReplayBeatDto
                {
                    Kind = "VikingPerish",
                    FromQ = vp.Sea.Q,
                    FromR = vp.Sea.R,
                },
                ReplayVikingSpawnBeat vs => new ReplayBeatDto
                {
                    Kind = "VikingSpawn",
                    WaveIndex = vs.WaveIndex,
                    Spawns = SerializeSpawnList(vs.Spawns),
                },
                ReplayVikingTurnEndBeat _ => new ReplayBeatDto { Kind = "VikingTurnEnd" },
                ReplayDisplayTextBeat dt => new ReplayBeatDto
                {
                    Kind = "DisplayText",
                    Text = dt.Text,
                },
                ReplaySelectTerritoryBeat sel => new ReplayBeatDto
                {
                    Kind = "SelectTerritory",
                    ToQ = sel.Anchor.Q,
                    ToR = sel.Anchor.R,
                },
                ReplayRejectedMoveBeat rj => new ReplayBeatDto
                {
                    Kind = "RejectedMove",
                    FromQ = rj.From.Q,
                    FromR = rj.From.R,
                    ToQ = rj.To.Q,
                    ToR = rj.To.R,
                },
                ReplayDemoStartBeat _ => new ReplayBeatDto { Kind = "DemoStart" },
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
                "VikingMove" => new ReplayVikingMoveBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    From = new HexCoord(
                        dto.FromQ ?? throw new InvalidOperationException("VikingMove beat missing FromQ"),
                        dto.FromR ?? throw new InvalidOperationException("VikingMove beat missing FromR")),
                    To = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("VikingMove beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("VikingMove beat missing ToR")),
                },
                "VikingDisembark" => new ReplayVikingDisembarkBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Sea = new HexCoord(
                        dto.FromQ ?? throw new InvalidOperationException("VikingDisembark beat missing FromQ"),
                        dto.FromR ?? throw new InvalidOperationException("VikingDisembark beat missing FromR")),
                    Land = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("VikingDisembark beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("VikingDisembark beat missing ToR")),
                },
                "VikingPerish" => new ReplayVikingPerishBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Sea = new HexCoord(
                        dto.FromQ ?? throw new InvalidOperationException("VikingPerish beat missing FromQ"),
                        dto.FromR ?? throw new InvalidOperationException("VikingPerish beat missing FromR")),
                },
                "VikingSpawn" => new ReplayVikingSpawnBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    WaveIndex = dto.WaveIndex
                        ?? throw new InvalidOperationException("VikingSpawn beat missing WaveIndex"),
                    Spawns = DeserializeSpawnList(dto.Spawns),
                },
                "VikingTurnEnd" => new ReplayVikingTurnEndBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                },
                "DisplayText" => new ReplayDisplayTextBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Text = dto.Text ?? "",
                },
                "SelectTerritory" => new ReplaySelectTerritoryBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    Anchor = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("SelectTerritory beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("SelectTerritory beat missing ToR")),
                },
                "RejectedMove" => new ReplayRejectedMoveBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
                    From = new HexCoord(
                        dto.FromQ ?? throw new InvalidOperationException("RejectedMove beat missing FromQ"),
                        dto.FromR ?? throw new InvalidOperationException("RejectedMove beat missing FromR")),
                    To = new HexCoord(
                        dto.ToQ ?? throw new InvalidOperationException("RejectedMove beat missing ToQ"),
                        dto.ToR ?? throw new InvalidOperationException("RejectedMove beat missing ToR")),
                },
                "DemoStart" => new ReplayDemoStartBeat
                {
                    Index = dto.Index, Turn = dto.Turn, Actor = dto.Actor,
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
    /// (Recruit/Soldier/Captain/Commander) and the legacy names
    /// (Peasant/Spearman/Knight/Baron), mapping each to the same level.
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

    // PlayerId.None encodes as -1 (no owner). Real games never produce
    // a None-owned tile, but the format round-trips it defensively rather
    // than throwing.
    private static int IdToOwnerIndex(PlayerId id) => id.IsNone ? -1 : id.Index;

    // The stored owner index is a SLOT (PlayerId.Index), not a position in the
    // roster list. With a 2–6 player game the roster is compacted (e.g. slots
    // 0,2,4), so we match by slot rather than indexing the list. A slot
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
    /// Retired palette hex strings mapped to the slot index that inherited
    /// their identity. Consulted as a fallback by <see cref="TryPlayerForHex"/>
    /// so legacy claim-victory data keyed by a retired color still resolves to
    /// the right player.
    /// </summary>
    private static readonly Dictionary<string, int> RetiredHexAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["e3bc3b"] = 3,
            ["8a5a2b"] = 3,
        };

    /// <summary>
    /// Map a legacy stored hex back to the palette slot it names — the
    /// live <see cref="GameSettings.PlayerConfig"/> first, then the
    /// <see cref="RetiredHexAliases"/> table for retired colors. Returns false
    /// if neither matches (genuine palette drift — the entry is dropped).
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
    /// procedural (Random Map) games.
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
    /// Legacy read-only field: flat list of human color hex strings that
    /// dismissed the End-Turn claim-victory prompt. New saves never populate
    /// it. Loaders fall back to this when the per-tier field below is absent
    /// and treat each entry as "prompted at 50%".
    /// </summary>
    public List<string>? ClaimVictoryPromptedColorHexes { get; set; }

    /// <summary>
    /// Legacy read-only field: highest claim-victory tier (50/75/90) each
    /// human color hex had already dismissed. Used only when
    /// <see cref="ClaimVictoryPromptedHighestByPlayerIndex"/> is absent.
    /// </summary>
    public Dictionary<string, int>? ClaimVictoryPromptedHighestByColorHex { get; set; }

    /// <summary>
    /// Highest claim-victory tier (50/75/90) each player (by roster index)
    /// has already dismissed this game. Palette-independent — supersedes the
    /// two legacy color-hex fields. Null when the dictionary is empty (kept
    /// clean for fresh games).
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
    /// Campaign level index (0..255) for games launched from the
    /// campaign screen. Null for freeform games.
    /// </summary>
    public int? CampaignLevel { get; set; }

    /// <summary>
    /// The selectable game mode. Null for
    /// <see cref="GameMode.Freeform"/> games (which load as Freeform);
    /// present only for Rising Tides. The grown water set
    /// rides in <see cref="Water"/>, so no separate flood-progress field is
    /// needed. <see cref="WaterCoords"/> is recomputed on replay anyway.
    /// </summary>
    public GameMode? Mode { get; set; }

    /// <summary>
    /// The locked Rising Tides forecast for the current player's turn — the
    /// tiles selected at turn start that demote/submerge at turn end.
    /// Null for freeform games and Rising Tides saves taken between turns
    /// (which load as an empty forecast).
    /// </summary>
    public List<TideStepDto>? PendingTide { get; set; }

    /// <summary>
    /// Whether this game uses seed-deterministic randomized selection for
    /// capital placement and the Rising Tides submerge tie-break. Baked at game
    /// creation. Absent (false) in pre-15 saves, which keep the old lex-min
    /// placement so their recorded replays reproduce exactly.
    /// </summary>
    public bool UseRandomizedSelection { get; set; }

    /// <summary>
    /// Whether a same-owner territory merge keeps the acting unit's origin
    /// territory's capital (falling back to largest-wins when no origin
    /// capital is among the merged ones). Baked at game creation. Absent
    /// (false) in pre-18 saves, which keep the largest-wins merge rule so
    /// their recorded replays reproduce exactly.
    /// </summary>
    public bool UseOriginMergeCapital { get; set; }

    /// <summary>
    /// Fog Of War: the human player's explored (ever-seen) coords. Null/omitted
    /// for non-fog games and legacy saves, which load with empty memory.
    /// </summary>
    public List<CoordDto>? Seen { get; set; }

    /// <summary>Viking Raiders: raiders currently at sea. Null/omitted when
    /// none (every non-viking save); pre-17 saves load all-default.</summary>
    public List<SeaVikingDto>? VikingAtSea { get; set; }

    /// <summary>Viking Raiders: next wave index (0-based); null/omitted at 0.</summary>
    public int? VikingNextWave { get; set; }

    /// <summary>Viking Raiders: last round whose viking pseudo-turn completed;
    /// null/omitted at 0.</summary>
    public int? VikingLastRound { get; set; }

    /// <summary>Viking Raiders: round the most recent wave spawned in;
    /// null/omitted at 0.</summary>
    public int? VikingLastSpawnRound { get; set; }
}

/// <summary>A raider at sea: coord + unit level. Used by
/// <see cref="SaveData.VikingAtSea"/> and the VikingSpawn replay beat.</summary>
public sealed class SeaVikingDto
{
    public int Q { get; set; }
    public int R { get; set; }
    public string? Level { get; set; }
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
    /// AI kind name (one of <see cref="PlayerKind"/>). Null in "starting map"
    /// exports from the editor — the role for each color is assigned at play
    /// time via the Play Game config menu. Present in regular in-progress saves.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>
    /// Per-player <see cref="Difficulty"/> name. Null in starting-map exports
    /// (assigned at the Play Game menu); defaults to
    /// <see cref="Difficulty.Soldier"/> when absent on load.
    /// </summary>
    public string? Difficulty { get; set; }
}

public sealed class TileDto
{
    public int Q { get; set; }
    public int R { get; set; }
    public int OwnerIndex { get; set; }
    public OccupantDto? Occupant { get; set; }

    /// <summary>Gold tile; absent → false.</summary>
    public bool IsGold { get; set; }

    /// <summary>Mountain tile; absent → false.</summary>
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
/// recorded beat. Embedded inside <see cref="SaveData.Replay"/>.
/// Mirrors the tutorial-<c>Beats</c> shape — kind-
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

    /// <summary>VikingSpawn beats only: the wave index.</summary>
    public int? WaveIndex { get; set; }

    /// <summary>VikingSpawn beats only: the explicit spawn placements.</summary>
    public List<SeaVikingDto>? Spawns { get; set; }
    public int? CapitalQ { get; set; }
    public int? CapitalR { get; set; }
    public string? Level { get; set; }
    public int? ThresholdPercent { get; set; }
    /// <summary>Authored narration for <see cref="ReplayDisplayTextBeat"/>.</summary>
    public string? Text { get; set; }
}
