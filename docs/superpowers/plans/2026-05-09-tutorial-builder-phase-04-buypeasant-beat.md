# TutorialBuilder Phase 4 — `BuyPeasant` Beat (author + preview) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Read first:** `CLAUDE.md` (strict TDD; rebuild-before-launch; manual-test-after-every-change; architecture-doc-before-push). The 3a / 3b / 3c plans (Phase 4 extends their types). The master plan: `docs/superpowers/plans/2026-05-09-tutorial-builder-master.md`. The design spec: `docs/superpowers/specs/2026-05-09-tutorial-builder-design.md` §"Data model" (`BuyPeasantBeat`), §"Build mode" (BuyPeasant authoring paragraph), §"Beat pointer advancement" (Human player-action beats). The current `TutorialGatedHudView.cs` and `TutorialGatedHexMapView.cs` (Phase 4 reshapes the HUD's BuyPeasant gate from "always reject" to a real arm-then-tile flow).

**Goal:** Author and preview a single `BuyPeasantBeat` end-to-end. Adds `BuyPeasantBeat` POCO + serializer support; extends `TutorialValidator` / `TutorialPlayer` with the two-step arm-then-place state machine; extends the gated HUD + map wrappers to honour it; grows `BuildPane` with an "Add BuyPeasant" button + tile-pick mode; verifies `AiSimulator.Apply` already produces the right post-buy state (so Phase 11's BuildPane state-after-beat-N cache can reuse it).

**Architecture:** A `BuyPeasantBeat` carries one `HexCoord At`. Real-game purchases are a two-event flow (Buy Peasant button → enter `BuyingPeasant` mode → tile click → `ExecuteBuyAndPlace`); the gating mirrors that. `TutorialPlayer` gains a single-slot `_armedBeat` (the beat we've already issued the HUD click for, awaiting the follow-up tile click). `TutorialGatedHudView.OnRealBuyPeasantClicked` arms the next beat if it's a `BuyPeasantBeat` (and we're not already armed — second click would cycle the controller's level from Peasant to Spearman); cancel pass-through disarms. `TutorialGatedHexMapView.OnRealTileClicked` checks `_armedBeat` first: if set, validates `tile.Coord == beat.At` and advances on match (rejects on miss, leaving the controller in `BuyingPeasant` mode so the dev can retry). `BuildPane`'s "Add BuyPeasant" button enters a `_pendingPick` state and the next `Map.CoordClicked` materialises the beat at that coord.

**Tech Stack:** Godot 4.6.1 (.NET) + C# (net8.0) + xUnit. C# records (`init`-only) for POCOs.

---

## File Structure

**Create:**
- `tests/TutorialBeatSimulatorTests.cs` — verifies that `AiSimulator.Apply(AiBuyUnitAction)` (the existing simulator path Phase 11's BuildPane cache will reuse) produces the same post-state as a `BuyPeasantBeat` describes (peasant placed at `At`, gold deducted from capital).

**Modify:**
- `scripts/Tutorial/Beat.cs` — extend `BeatKind` enum with `BuyPeasant`; add `BuyPeasantBeat` sealed record (`HexCoord At`).
- `scripts/Tutorial/TutorialValidator.cs` — add `MatchesBuyPeasant(BuyPeasantBeat, HexCoord)`.
- `scripts/Tutorial/TutorialPlayer.cs` — add `ArmedBeat` (read-only) + `IsArmedForBuyPeasant` + `TryArmBuyPeasant()` + `TryAdvanceForBuyPeasantTile(HexCoord)` + `DisarmIfAny()`.
- `scripts/Tutorial/TutorialGatedHudView.cs` — replace `OnRealBuyPeasantClicked` (was always-reject) with arm-or-reject; wrap `CancelActionPressed` pass-through to disarm.
- `scripts/Tutorial/TutorialGatedHexMapView.cs` — extend `OnRealTileClicked` to consult `IsArmedForBuyPeasant` and route to `TryAdvanceForBuyPeasantTile`.
- `scripts/SaveSerializer.cs` — extend `BeatDto` with `int? AtQ` + `int? AtR`; add `BuyPeasant` cases to `SerializeBeats` and `DeserializeBeats` switches.
- `scripts/BuildPane.cs` — add "Add BuyPeasant" button + `_pendingPick` enum field + subscribe to `_panel.Map.CoordClicked` on entering pick mode; clicking a coord while in pick mode appends a `BuyPeasantBeat` and exits pick mode.
- `tests/TutorialSerializerTests.cs` — extend with a `BuyPeasantBeat` round-trip test.
- `tests/TutorialValidatorTests.cs` — extend with `MatchesBuyPeasant` tests.
- `tests/TutorialPlayerTests.cs` — extend with `TryArmBuyPeasant` / `TryAdvanceForBuyPeasantTile` / `DisarmIfAny` tests.
- `tests/TutorialGatedHudViewTests.cs` — replace the obsolete `BuyPeasantClick_AlwaysRejects_InPhase3c` test; add arm-and-forward, second-click-rejected, cancel-disarms tests.
- `tests/TutorialGatedHexMapViewTests.cs` — add tile-click-while-armed forwards-on-match / rejects-on-mismatch tests.
- `tests/FourExHex.Tests.csproj` — add `<Compile Include>` for the new `tests/TutorialBeatSimulatorTests.cs` (xUnit auto-discovers the file in the same directory; the csproj `<Compile>` list governs production-source includes, not test files — but check the file picks up; if not, add explicit include) **and** add `<Compile Include>` for `MapGenerator.cs` if `TutorialBeatSimulatorTests` needs it (it doesn't — uses `TestHelpers.BuildRectGrid`).
- `ARCHITECTURE.md` — small incremental update (Tutorial section + file layout note).

**Out of scope for Phase 4 (later phases):**
- `MoveBeat` (Phase 5), `BuildTowerBeat` (Phase 6), overlay beats (Phases 7-9).
- Multi-turn lane state machine — `BuildPane` Phase 4 still hardcodes `(Turn=1, Actor=0)` for every "Add" click. Phase 10 lifts that.
- BuildPane validation that the picked tile is owned by the actor — Phase 12 surfaces this as a warning. For Phase 4 the dev tester just clicks a friendly tile.
- ESC-to-cancel pick mode in BuildPane — Phase 14 polish.
- BuildPane post-beat state cache (`Dictionary<int, GameStateSnapshot>`) — Phase 11. Phase 4 only ships the simulator equivalence test that Phase 11 will rely on.
- Inspector showing `At` for the selected `BuyPeasantBeat` — covered by Phase 11 (kind-specific inspector). Phase 4 keeps the existing `Turn`/`Actor` inspector and lets the chip label carry the `At` coord text for visibility (`#0 T1 A0 BuyPeasant (q,r)`).
- `TutorialPlayer.Snapshots` population — Phase 13.

---

## Task 1: `BuyPeasantBeat` POCO + serializer round-trip (TDD)

**Files:**
- Modify: `scripts/Tutorial/Beat.cs`
- Modify: `scripts/SaveSerializer.cs` (extend `BeatDto`; extend serialize/deserialize switches)
- Modify: `tests/TutorialSerializerTests.cs` (add round-trip test)

The `BeatDto` pattern from Phase 3b is "every kind-specific field is a nullable on the DTO". Phase 4 adds `AtQ` / `AtR` (two ints, not a `CoordDto`, to keep the on-disk shape flat — matches how `TileDto` and `CapitalGoldDto` already inline `Q`/`R`).

- [ ] **Step 1.1: Extend `Beat.cs` with `BuyPeasantBeat`**

Open `scripts/Tutorial/Beat.cs`. Replace the entire file with:

```csharp
/// <summary>
/// Discriminated-union root for tutorial beats. Concrete kinds are
/// sealed records below. JSON (de)serialization in SaveSerializer
/// uses the <see cref="Kind"/> field as the discriminator (mirrors
/// the OccupantDto pattern — hand-written switch, no reflection).
///
/// Phase 3b shipped <see cref="EndTurnBeat"/>. Phase 4 adds
/// <see cref="BuyPeasantBeat"/>. Phase 5 adds MoveBeat, etc. Each new
/// kind appears here, in <see cref="BeatKind"/>, in SaveSerializer's
/// serialize + deserialize switches, and in any consumer that handles
/// it explicitly.
/// </summary>
public abstract record Beat
{
    public int Index { get; init; }            // contiguous from 0
    public int Turn { get; init; }             // 1-based, matches TurnState.TurnNumber
    public int Actor { get; init; }            // index into Players
    public string? Narration { get; init; }    // optional caption shown in timeline
    public abstract BeatKind Kind { get; }
}

public enum BeatKind
{
    EndTurn,
    BuyPeasant,
    // 5+ adds: Move, BuildTower, Prompt, Highlight, CameraFocus
}

public sealed record EndTurnBeat : Beat
{
    public override BeatKind Kind => BeatKind.EndTurn;
}

/// <summary>
/// Player buys a peasant and places it at <see cref="At"/>. In the
/// real game this is a two-event sequence (Buy Peasant button → enter
/// BuyingPeasant mode → tile click → ExecuteBuyAndPlace); the
/// tutorial gating layer mirrors that — see
/// <see cref="TutorialPlayer.TryArmBuyPeasant"/> /
/// <see cref="TutorialPlayer.TryAdvanceForBuyPeasantTile"/>.
/// </summary>
public sealed record BuyPeasantBeat : Beat
{
    public override BeatKind Kind => BeatKind.BuyPeasant;
    public required HexCoord At { get; init; }
}
```

- [ ] **Step 1.2: Build the test assembly to confirm everything still compiles**

Run: `dotnet build tests/FourExHex.Tests.csproj`
Expected: Build succeeded, 0 errors. (No new tests yet; Step 1.4 writes them.)

- [ ] **Step 1.3: Build the game DLL**

Run: `dotnet build FourExHex.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 1.4: Write failing test — single BuyPeasantBeat round-trips**

Open `tests/TutorialSerializerTests.cs`. Append the following test inside the class:

```csharp
[Fact]
public void SerializeMap_RoundTripsTutorialWithBuyPeasantBeat()
{
    (GameState state, IReadOnlyList<Player> players) = BuildMinimalState();
    var tutorial = new Tutorial
    {
        Title = "BuyPeasant smoke",
        Beats = new List<Beat>
        {
            new BuyPeasantBeat
            {
                Index = 0,
                Turn = 1,
                Actor = 0,
                At = new HexCoord(3, 5),
            },
        },
    };

    string json = SaveSerializer.SerializeMap(state, masterSeed: 7, players, "m", tutorial);
    LoadedSave loaded = SaveSerializer.Deserialize(json);

    Assert.NotNull(loaded.Tutorial);
    Assert.Single(loaded.Tutorial!.Beats);
    Beat beat = loaded.Tutorial.Beats[0];
    BuyPeasantBeat bpb = Assert.IsType<BuyPeasantBeat>(beat);
    Assert.Equal(0, bpb.Index);
    Assert.Equal(1, bpb.Turn);
    Assert.Equal(0, bpb.Actor);
    Assert.Equal(BeatKind.BuyPeasant, bpb.Kind);
    Assert.Equal(new HexCoord(3, 5), bpb.At);
}
```

- [ ] **Step 1.5: Run the test — see it fail for the right reason**

Run: `dotnet test --filter FullyQualifiedName~TutorialSerializerTests.SerializeMap_RoundTripsTutorialWithBuyPeasantBeat`
Expected: FAIL with `InvalidOperationException: Unknown beat kind for serialization: BuyPeasantBeat` (thrown from the `_ => throw …` branch of `SerializeBeats`). If you see a `Tutorial = null` failure or a different exception, the round-trip plumbing has drifted — diagnose before continuing.

- [ ] **Step 1.6: Extend `BeatDto` with `AtQ` / `AtR`**

Open `scripts/SaveSerializer.cs`. Find `public sealed class BeatDto` (around line 666):

```csharp
public sealed class BeatDto
{
    public int Index { get; set; }
    public int Turn { get; set; }
    public int Actor { get; set; }
    public string? Narration { get; set; }

    /// <summary>One of <see cref="BeatKind"/>: "EndTurn" today; later phases add more.</summary>
    public string Kind { get; set; } = "";
}
```

Replace with:

```csharp
public sealed class BeatDto
{
    public int Index { get; set; }
    public int Turn { get; set; }
    public int Actor { get; set; }
    public string? Narration { get; set; }

    /// <summary>One of <see cref="BeatKind"/>: "EndTurn", "BuyPeasant"; later phases add more.</summary>
    public string Kind { get; set; } = "";

    // --- Kind-specific fields (nullable; populated only for matching Kind) ---
    /// <summary>Q-coord for tile-anchored beats (BuyPeasant; later BuildTower / Move's Src).</summary>
    public int? AtQ { get; set; }
    /// <summary>R-coord for tile-anchored beats (BuyPeasant; later BuildTower / Move's Src).</summary>
    public int? AtR { get; set; }
}
```

- [ ] **Step 1.7: Add `BuyPeasant` cases to the serialize / deserialize switches**

In the same file, find `SerializeBeats` (around line 478):

```csharp
        BeatDto dto = beat switch
        {
            EndTurnBeat _ => new BeatDto { Kind = "EndTurn" },
            _ => throw new InvalidOperationException(
                $"Unknown beat kind for serialization: {beat.GetType()}"),
        };
```

Replace with:

```csharp
        BeatDto dto = beat switch
        {
            EndTurnBeat _ => new BeatDto { Kind = "EndTurn" },
            BuyPeasantBeat bpb => new BeatDto
            {
                Kind = "BuyPeasant",
                AtQ = bpb.At.Q,
                AtR = bpb.At.R,
            },
            _ => throw new InvalidOperationException(
                $"Unknown beat kind for serialization: {beat.GetType()}"),
        };
```

Then find `DeserializeBeats` (around line 498):

```csharp
        Beat beat = dto.Kind switch
        {
            "EndTurn" => new EndTurnBeat
            {
                Index = dto.Index,
                Turn = dto.Turn,
                Actor = dto.Actor,
                Narration = dto.Narration,
            },
            _ => throw new InvalidOperationException(
                $"Unknown beat kind in save: {dto.Kind}"),
        };
```

Replace with:

```csharp
        Beat beat = dto.Kind switch
        {
            "EndTurn" => new EndTurnBeat
            {
                Index = dto.Index,
                Turn = dto.Turn,
                Actor = dto.Actor,
                Narration = dto.Narration,
            },
            "BuyPeasant" => new BuyPeasantBeat
            {
                Index = dto.Index,
                Turn = dto.Turn,
                Actor = dto.Actor,
                Narration = dto.Narration,
                At = new HexCoord(
                    dto.AtQ ?? throw new InvalidOperationException(
                        "BuyPeasant beat missing AtQ"),
                    dto.AtR ?? throw new InvalidOperationException(
                        "BuyPeasant beat missing AtR")),
            },
            _ => throw new InvalidOperationException(
                $"Unknown beat kind in save: {dto.Kind}"),
        };
```

- [ ] **Step 1.8: Re-run the new test — green**

Run: `dotnet build FourExHex.csproj && dotnet test --filter FullyQualifiedName~TutorialSerializerTests.SerializeMap_RoundTripsTutorialWithBuyPeasantBeat`
Expected: PASS.

- [ ] **Step 1.9: Run the full suite**

Run: `dotnet test`
Expected: 747 tests pass (was 746; +1 new from Step 1.4). All previous Tutorial-related tests still pass.

- [ ] **Step 1.10: Commit**

```bash
git add scripts/Tutorial/Beat.cs scripts/SaveSerializer.cs tests/TutorialSerializerTests.cs
git commit -m "Add BuyPeasantBeat POCO + serializer round-trip (TutorialBuilder Phase 4)"
```

---

## Task 2: `TutorialValidator.MatchesBuyPeasant` + `TutorialPlayer` arm/advance state (TDD)

**Files:**
- Modify: `scripts/Tutorial/TutorialValidator.cs`
- Modify: `scripts/Tutorial/TutorialPlayer.cs`
- Modify: `tests/TutorialValidatorTests.cs`
- Modify: `tests/TutorialPlayerTests.cs`

`TutorialPlayer` grows a single-slot `_armedBeat` (the beat the dev has issued the HUD click for, awaiting the follow-up tile click). The state-machine rules:

| Trigger | Pre-state | Post-state |
|---|---|---|
| `TryArmBuyPeasant()` while next beat is `BuyPeasantBeat` and `_armedBeat == null` | armed=null | armed=that beat; returns true |
| `TryArmBuyPeasant()` while already armed (any beat) | armed=X | armed=X (unchanged); returns false; fires `PlayerActionRejected` |
| `TryArmBuyPeasant()` while next beat is not `BuyPeasantBeat` | armed=null | armed=null; returns false; fires `PlayerActionRejected` |
| `TryAdvanceForBuyPeasantTile(at)` while armed for `BuyPeasantBeat` and `at == beat.At` | armed=beat | armed=null; `_nextBeatIndex++`; fires `BeatApplied`; returns true |
| `TryAdvanceForBuyPeasantTile(at)` while armed for `BuyPeasantBeat` and `at != beat.At` | armed=beat | armed=beat (unchanged — controller stays in BuyingPeasant mode for retry); returns false; fires `PlayerActionRejected` |
| `TryAdvanceForBuyPeasantTile(at)` while not armed | armed=null | unchanged; returns false (caller doesn't forward — but caller should only invoke this when `IsArmedForBuyPeasant` was true) |
| `DisarmIfAny()` | any | armed=null |

Why "second arm rejects" (row 2): a second `BuyPeasantClicked` while in `BuyingPeasant` mode cycles the controller's level (Peasant → Spearman). That would break the tutorial silently (no peasant ever produced). Refusing the second click prevents the cycle. The dev can press Cancel to disarm and re-arm if needed.

- [ ] **Step 2.1: Extend `TutorialValidator.cs`**

Open `scripts/Tutorial/TutorialValidator.cs`. Replace the entire file with:

```csharp
/// <summary>
/// Decides whether a player input matches the next expected
/// scripted beat. Static — no per-instance state. Phase 3c shipped
/// <see cref="MatchesEndTurn"/>; Phase 4 adds <see cref="MatchesBuyPeasant"/>.
/// Phase 5 adds MatchesMove, Phase 6 adds MatchesBuildTower.
/// </summary>
public static class TutorialValidator
{
    /// <summary>
    /// EndTurnBeat carries no per-beat data (no At, no Src/Dst), so
    /// any End-Turn click matches if the next beat is an EndTurnBeat.
    /// The wrapper checks the kind first; this method is the explicit
    /// "yes, this is a match" symbol that mirrors the spec's
    /// MatchesMove / MatchesBuyPeasant / MatchesBuildTower triplet.
    /// </summary>
    public static bool MatchesEndTurn(EndTurnBeat beat) => true;

    /// <summary>
    /// Exact-coord match per scope decision #4: the click's tile coord
    /// must equal the beat's <see cref="BuyPeasantBeat.At"/>. No fuzzy
    /// matching (e.g., "any tile in the same territory" — that would
    /// silently turn the tutorial into a hint engine).
    /// </summary>
    public static bool MatchesBuyPeasant(BuyPeasantBeat beat, HexCoord at) =>
        beat.At == at;

    /// <summary>
    /// Build the soft-reject message shown via
    /// <c>IHudView.ShowTutorialMessage</c> when the player attempts
    /// an action that doesn't match the next expected beat.
    /// </summary>
    public static string ReasonMismatch(Beat? expected, string attempted)
    {
        if (expected == null)
        {
            return "Tutorial complete — no further actions expected.";
        }
        return $"Expected {expected.Kind} (turn {expected.Turn}, actor {expected.Actor}); got {attempted}.";
    }
}
```

- [ ] **Step 2.2: Write failing tests for `MatchesBuyPeasant`**

Open `tests/TutorialValidatorTests.cs`. Append inside the class:

```csharp
[Fact]
public void MatchesBuyPeasant_TrueWhenCoordEqualsAt()
{
    var beat = new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = new HexCoord(2, 3) };
    Assert.True(TutorialValidator.MatchesBuyPeasant(beat, new HexCoord(2, 3)));
}

[Fact]
public void MatchesBuyPeasant_FalseWhenCoordDiffers()
{
    var beat = new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = new HexCoord(2, 3) };
    Assert.False(TutorialValidator.MatchesBuyPeasant(beat, new HexCoord(2, 4)));
    Assert.False(TutorialValidator.MatchesBuyPeasant(beat, new HexCoord(3, 3)));
}
```

- [ ] **Step 2.3: Run validator tests — green (Step 2.1's implementation already satisfies them)**

Run: `dotnet build FourExHex.csproj && dotnet test --filter FullyQualifiedName~TutorialValidatorTests`
Expected: 5 PASS (3 existing + 2 new).

If the tests FAIL because `MatchesBuyPeasant` is missing, Step 2.1 didn't fully land — re-check the file.

- [ ] **Step 2.4: Extend `TutorialPlayer.cs`**

Open `scripts/Tutorial/TutorialPlayer.cs`. Replace the entire file with:

```csharp
using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Runtime state for a tutorial in Preview. Tracks the next-expected-
/// beat pointer, exposes the events the gated view wrappers fire into,
/// and provides the AI chooser delegate handed to GameController.
///
/// Phase 4 grows the single-slot <see cref="ArmedBeat"/> state machine
/// for two-event player actions (BuyPeasant: Buy button → tile click).
/// The wrapper that observed the first event (HUD click) calls
/// <see cref="TryArmBuyPeasant"/>; the wrapper that observed the second
/// event (tile click) calls <see cref="TryAdvanceForBuyPeasantTile"/>.
/// Cancel disarms via <see cref="DisarmIfAny"/>.
///
/// AI chooser still falls through to AiDispatcher (no scripted-AI logic
/// — Phase 10). Snapshots stays empty (Phase 13 populates it).
///
/// Pure C# / Godot-free (only references Godot's Color struct via
/// AiChooser's signature, which is value-type and test-friendly).
/// </summary>
public sealed class TutorialPlayer
{
    private readonly Tutorial _tutorial;
    private int _nextBeatIndex;
    private readonly List<GameStateSnapshot> _snapshots = new();
    private Beat? _armedBeat;

    public TutorialPlayer(Tutorial tutorial)
    {
        _tutorial = tutorial;
        _nextBeatIndex = 0;
        _armedBeat = null;
    }

    /// <summary>Next beat the player is expected to perform, or null if finished.</summary>
    public Beat? NextExpectedPlayerBeat =>
        _nextBeatIndex < _tutorial.Beats.Count ? _tutorial.Beats[_nextBeatIndex] : null;

    /// <summary>Index of the most-recently-applied beat, or -1 before any apply.</summary>
    public int CurrentBeatIndex => _nextBeatIndex - 1;

    /// <summary>
    /// Per-beat state snapshots for the scrubber (Phase 13). Empty in
    /// Phase 4 — population is deferred until the scrubber consumes them.
    /// </summary>
    public IReadOnlyList<GameStateSnapshot> Snapshots => _snapshots;

    /// <summary>
    /// The beat the player has issued the HUD-precursor click for and
    /// is expected to follow up with a tile click. Set by
    /// <see cref="TryArmBuyPeasant"/>; cleared by
    /// <see cref="TryAdvanceForBuyPeasantTile"/> on success and by
    /// <see cref="DisarmIfAny"/> (e.g., via Cancel).
    /// </summary>
    public Beat? ArmedBeat => _armedBeat;

    /// <summary>
    /// True iff the gated tile-click handler should route the next
    /// click through <see cref="TryAdvanceForBuyPeasantTile"/> instead
    /// of forwarding it as a passive selection.
    /// </summary>
    public bool IsArmedForBuyPeasant => _armedBeat is BuyPeasantBeat;

    /// <summary>Fires after a beat is applied. Argument is the beat's index.</summary>
    public event Action<int>? BeatApplied;

    /// <summary>Fires when the player attempts an action that doesn't match the
    /// next expected beat. The PreviewPane subscribes and shows a toast.</summary>
    public event Action<Beat?, string>? PlayerActionRejected;

    /// <summary>Fires once after the last beat is applied.</summary>
    public event Action? TutorialFinished;

    /// <summary>
    /// AI chooser delegate handed to GameController. Phase 4 (like 3c)
    /// always falls through to AiDispatcher (no scripted-AI logic).
    /// Phase 10 adds the scripted-beat-as-AiAction path here.
    /// </summary>
    public AiAction? AiChooser(GameState state, Color forPlayer,
                                HashSet<HexCoord> visitedCapitals, Random rng)
        => AiDispatcher.ChooseForCurrentPlayer(state, forPlayer, visitedCapitals, rng);

    /// <summary>
    /// Called by <see cref="TutorialGatedHudView"/> when the player
    /// clicks End Turn. If the next beat is an EndTurnBeat, advances
    /// the pointer + fires events + returns true (caller forwards the
    /// click to the controller). If the next beat is anything else,
    /// fires PlayerActionRejected and returns false (caller does NOT
    /// forward).
    /// </summary>
    public bool TryAdvanceForEndTurn()
    {
        if (NextExpectedPlayerBeat is EndTurnBeat etb && TutorialValidator.MatchesEndTurn(etb))
        {
            int applied = _nextBeatIndex;
            _nextBeatIndex++;
            BeatApplied?.Invoke(applied);
            if (_nextBeatIndex >= _tutorial.Beats.Count)
            {
                TutorialFinished?.Invoke();
            }
            return true;
        }
        NotifyRejected("End Turn");
        return false;
    }

    /// <summary>
    /// Called by <see cref="TutorialGatedHudView"/> when the player
    /// clicks Buy Peasant. If the next beat is a BuyPeasantBeat AND
    /// we're not already armed, sets <see cref="ArmedBeat"/> and
    /// returns true (caller forwards the click — controller enters
    /// BuyingPeasant mode). Otherwise rejects:
    /// <list type="bullet">
    ///   <item>If already armed, rejection prevents the controller's
    ///   buy-level cycle (re-pressing Buy goes Peasant → Spearman,
    ///   which would silently break the tutorial).</item>
    ///   <item>If next beat isn't BuyPeasant, rejection signals "wrong
    ///   action right now".</item>
    /// </list>
    /// Note this advances no pointer — the actual beat completion is
    /// the follow-up tile click via
    /// <see cref="TryAdvanceForBuyPeasantTile"/>.
    /// </summary>
    public bool TryArmBuyPeasant()
    {
        if (_armedBeat == null && NextExpectedPlayerBeat is BuyPeasantBeat bpb)
        {
            _armedBeat = bpb;
            return true;
        }
        NotifyRejected("Buy Peasant");
        return false;
    }

    /// <summary>
    /// Called by <see cref="TutorialGatedHexMapView"/> when the player
    /// clicks a tile *while the player is armed for BuyPeasant*. Caller
    /// MUST gate on <see cref="IsArmedForBuyPeasant"/> first — if not
    /// armed, the wrapper should forward as passive selection rather
    /// than calling this method.
    /// On match (tile coord == beat.At): clears arm, advances pointer,
    /// fires BeatApplied (and TutorialFinished if last), returns true →
    /// caller forwards the click (controller fires ExecuteBuyAndPlace).
    /// On miss (wrong tile): keeps arm set so the dev can retry without
    /// re-clicking Buy (controller stays in BuyingPeasant mode), fires
    /// PlayerActionRejected, returns false → caller does NOT forward
    /// (preventing the controller from cancelling its pending action).
    /// </summary>
    public bool TryAdvanceForBuyPeasantTile(HexCoord at)
    {
        if (_armedBeat is BuyPeasantBeat bpb && TutorialValidator.MatchesBuyPeasant(bpb, at))
        {
            int applied = _nextBeatIndex;
            _nextBeatIndex++;
            _armedBeat = null;
            BeatApplied?.Invoke(applied);
            if (_nextBeatIndex >= _tutorial.Beats.Count)
            {
                TutorialFinished?.Invoke();
            }
            return true;
        }
        NotifyRejected($"tile click at ({at.Q},{at.R})");
        return false;
    }

    /// <summary>
    /// Clear any armed beat. Called by
    /// <see cref="TutorialGatedHudView"/>'s Cancel pass-through and by
    /// any other path that exits the controller's pending-action mode.
    /// Idempotent — safe to call when not armed.
    /// </summary>
    public void DisarmIfAny()
    {
        _armedBeat = null;
    }

    /// <summary>
    /// Fire the soft-reject event. Used by gated wrappers when an
    /// input can never match (e.g. a tile click while not armed and
    /// next beat is BuyPeasantBeat — handled by the wrapper, not here)
    /// or when the next expected beat is of a different kind.
    /// </summary>
    public void NotifyRejected(string attempted)
    {
        Beat? next = NextExpectedPlayerBeat;
        string reason = TutorialValidator.ReasonMismatch(next, attempted);
        PlayerActionRejected?.Invoke(next, reason);
    }
}
```

- [ ] **Step 2.5: Write failing tests for the new TutorialPlayer API**

Open `tests/TutorialPlayerTests.cs`. Append a new helper and the new tests at the bottom of the class:

```csharp
private static Tutorial MakeBuyPeasantTutorial(HexCoord at, int after = 0)
{
    // `after` EndTurn beats precede the BuyPeasantBeat — used to
    // verify "wrong-kind next beat" rejection.
    var beats = new List<Beat>();
    for (int i = 0; i < after; i++)
    {
        beats.Add(new EndTurnBeat { Index = i, Turn = 1, Actor = 0 });
    }
    beats.Add(new BuyPeasantBeat { Index = after, Turn = 1, Actor = 0, At = at });
    return new Tutorial { Beats = beats };
}

[Fact]
public void TryArmBuyPeasant_HappyPath_ArmsAndReturnsTrue()
{
    Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
    var player = new TutorialPlayer(t);

    bool ok = player.TryArmBuyPeasant();

    Assert.True(ok);
    Assert.True(player.IsArmedForBuyPeasant);
    Assert.Same(t.Beats[0], player.ArmedBeat);
    Assert.Equal(-1, player.CurrentBeatIndex);          // not advanced yet
    Assert.Same(t.Beats[0], player.NextExpectedPlayerBeat);
}

[Fact]
public void TryArmBuyPeasant_AlreadyArmed_RejectsAndDoesNotChangeArm()
{
    Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
    var player = new TutorialPlayer(t);
    player.TryArmBuyPeasant();              // arm
    bool rejected = false;
    player.PlayerActionRejected += (_, _) => rejected = true;

    bool ok = player.TryArmBuyPeasant();    // second click

    Assert.False(ok);
    Assert.True(rejected);
    Assert.True(player.IsArmedForBuyPeasant);   // arm preserved
    Assert.Same(t.Beats[0], player.ArmedBeat);
}

[Fact]
public void TryArmBuyPeasant_NextBeatIsEndTurn_RejectsAndDoesNotArm()
{
    Tutorial t = MakeTutorial(1);           // single EndTurnBeat
    var player = new TutorialPlayer(t);
    bool rejected = false;
    player.PlayerActionRejected += (_, _) => rejected = true;

    bool ok = player.TryArmBuyPeasant();

    Assert.False(ok);
    Assert.True(rejected);
    Assert.False(player.IsArmedForBuyPeasant);
    Assert.Null(player.ArmedBeat);
}

[Fact]
public void TryAdvanceForBuyPeasantTile_HappyPath_AdvancesAndDisarms()
{
    Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
    var player = new TutorialPlayer(t);
    player.TryArmBuyPeasant();
    var beatApplied = new List<int>();
    bool finished = false;
    player.BeatApplied += i => beatApplied.Add(i);
    player.TutorialFinished += () => finished = true;

    bool ok = player.TryAdvanceForBuyPeasantTile(new HexCoord(4, 5));

    Assert.True(ok);
    Assert.False(player.IsArmedForBuyPeasant);
    Assert.Null(player.ArmedBeat);
    Assert.Equal(0, player.CurrentBeatIndex);
    Assert.Equal(new[] { 0 }, beatApplied);
    Assert.True(finished);                  // single-beat tutorial
    Assert.Null(player.NextExpectedPlayerBeat);
}

[Fact]
public void TryAdvanceForBuyPeasantTile_WrongTile_RejectsAndKeepsArm()
{
    Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
    var player = new TutorialPlayer(t);
    player.TryArmBuyPeasant();
    string? reason = null;
    player.PlayerActionRejected += (_, r) => reason = r;

    bool ok = player.TryAdvanceForBuyPeasantTile(new HexCoord(4, 4));

    Assert.False(ok);
    Assert.True(player.IsArmedForBuyPeasant);   // arm preserved (controller stays in BuyingPeasant for retry)
    Assert.Equal(-1, player.CurrentBeatIndex);   // pointer unchanged
    Assert.NotNull(reason);
    Assert.Contains("tile click", reason!);
    Assert.Contains("BuyPeasant", reason);
}

[Fact]
public void TryAdvanceForBuyPeasantTile_NotArmed_RejectsAndPointerUnchanged()
{
    // Defensive: caller is expected to gate on IsArmedForBuyPeasant,
    // but if they don't and the player isn't armed, behaviour is still
    // "reject + don't advance". (No false-positive beat completion.)
    Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
    var player = new TutorialPlayer(t);
    bool rejected = false;
    player.PlayerActionRejected += (_, _) => rejected = true;

    bool ok = player.TryAdvanceForBuyPeasantTile(new HexCoord(4, 5));

    Assert.False(ok);
    Assert.True(rejected);
    Assert.Equal(-1, player.CurrentBeatIndex);
}

[Fact]
public void DisarmIfAny_ClearsArm_AndIsIdempotent()
{
    Tutorial t = MakeBuyPeasantTutorial(new HexCoord(4, 5));
    var player = new TutorialPlayer(t);
    player.TryArmBuyPeasant();
    Assert.True(player.IsArmedForBuyPeasant);

    player.DisarmIfAny();

    Assert.False(player.IsArmedForBuyPeasant);
    Assert.Null(player.ArmedBeat);

    player.DisarmIfAny();   // idempotent — no exception
    Assert.False(player.IsArmedForBuyPeasant);
}
```

- [ ] **Step 2.6: Run the new TutorialPlayer tests — green (Step 2.4's implementation satisfies them)**

Run: `dotnet test --filter FullyQualifiedName~TutorialPlayerTests`
Expected: 12 PASS (5 existing + 7 new). If any of the new tests fail, debug Step 2.4's `TutorialPlayer` body before continuing.

- [ ] **Step 2.7: Run the full suite**

Run: `dotnet test`
Expected: 756 tests pass (was 747 after Task 1; +2 Validator + 7 Player = 756).

- [ ] **Step 2.8: Commit**

```bash
git add scripts/Tutorial/TutorialValidator.cs scripts/Tutorial/TutorialPlayer.cs tests/TutorialValidatorTests.cs tests/TutorialPlayerTests.cs
git commit -m "Add MatchesBuyPeasant + TutorialPlayer arm/advance state (TutorialBuilder Phase 4)"
```

---

## Task 3: Gated wrapper extensions (TDD)

**Files:**
- Modify: `scripts/Tutorial/TutorialGatedHudView.cs` (replace `OnRealBuyPeasantClicked`; wrap Cancel pass-through to disarm)
- Modify: `scripts/Tutorial/TutorialGatedHexMapView.cs` (extend `OnRealTileClicked` to consult `IsArmedForBuyPeasant`)
- Modify: `tests/TutorialGatedHudViewTests.cs` (replace obsolete Phase-3c reject test; add 3 new tests)
- Modify: `tests/TutorialGatedHexMapViewTests.cs` (add 2 new tests for armed flow)

The HUD wrapper's current `OnRealBuyPeasantClicked` always rejects (Phase 3c marker). Phase 4 replaces it with the arm-or-reject path. The Cancel pass-through is currently a bare lambda re-raise; expand it to also disarm. The HexMap wrapper's `OnRealTileClicked` currently passive-forwards every tile click; Phase 4 inserts an `IsArmedForBuyPeasant` short-circuit before the forward.

- [ ] **Step 3.1: Update obsolete `BuyPeasantClick_AlwaysRejects_InPhase3c` test**

The Phase 3c test asserts buy-peasant always rejects — Phase 4's new behaviour breaks it. Open `tests/TutorialGatedHudViewTests.cs`. Find the test:

```csharp
[Fact]
public void BuyPeasantClick_AlwaysRejects_InPhase3c()
{
    (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) = Setup();
    bool forwarded = false;
    bool rejected = false;
    gated.BuyPeasantClicked += () => forwarded = true;
    player.PlayerActionRejected += (_, _) => rejected = true;

    real.ClickBuyPeasant();

    Assert.False(forwarded);
    Assert.True(rejected);
}
```

Replace it with:

```csharp
[Fact]
public void BuyPeasantClick_RejectsWhenNextBeatIsEndTurn()
{
    // Setup() builds a tutorial of EndTurnBeat(s); BuyPeasant is the
    // wrong action for that next beat → reject + don't forward.
    (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) = Setup();
    bool forwarded = false;
    bool rejected = false;
    gated.BuyPeasantClicked += () => forwarded = true;
    player.PlayerActionRejected += (_, _) => rejected = true;

    real.ClickBuyPeasant();

    Assert.False(forwarded);
    Assert.True(rejected);
    Assert.False(player.IsArmedForBuyPeasant);
}
```

- [ ] **Step 3.2: Add a helper for BuyPeasant-tutorial setup, plus 3 new BuyPeasant-flow tests**

Append to `tests/TutorialGatedHudViewTests.cs`, inside the class:

```csharp
private static (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player)
    SetupBuyPeasant(HexCoord at)
{
    var tutorial = new Tutorial
    {
        Beats = new List<Beat>
        {
            new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = at },
        },
    };
    var player = new TutorialPlayer(tutorial);
    var real = new MockHudView();
    var gated = new TutorialGatedHudView(real, player);
    return (gated, real, player);
}

[Fact]
public void BuyPeasantClick_Forwards_AndArms_WhenNextBeatIsBuyPeasant()
{
    (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) =
        SetupBuyPeasant(new HexCoord(2, 3));
    bool forwarded = false;
    gated.BuyPeasantClicked += () => forwarded = true;

    real.ClickBuyPeasant();

    Assert.True(forwarded);
    Assert.True(player.IsArmedForBuyPeasant);
    Assert.Equal(-1, player.CurrentBeatIndex);  // not advanced yet
}

[Fact]
public void BuyPeasantClick_SecondClick_RejectsAndDoesNotForward()
{
    (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) =
        SetupBuyPeasant(new HexCoord(2, 3));
    real.ClickBuyPeasant();                     // arm
    int forwardedCount = 0;
    gated.BuyPeasantClicked += () => forwardedCount++;
    bool rejected = false;
    player.PlayerActionRejected += (_, _) => rejected = true;

    real.ClickBuyPeasant();                     // re-click

    Assert.Equal(0, forwardedCount);            // not forwarded a second time
    Assert.True(rejected);
    Assert.True(player.IsArmedForBuyPeasant);   // arm preserved
}

[Fact]
public void CancelAction_Disarms_AndForwardsCancel()
{
    (TutorialGatedHudView gated, MockHudView real, TutorialPlayer player) =
        SetupBuyPeasant(new HexCoord(2, 3));
    real.ClickBuyPeasant();                     // arm
    Assert.True(player.IsArmedForBuyPeasant);
    bool forwardedCancel = false;
    gated.CancelActionPressed += () => forwardedCancel = true;

    real.PressCancelAction();

    Assert.True(forwardedCancel);
    Assert.False(player.IsArmedForBuyPeasant);
}
```

- [ ] **Step 3.3: Run the HUD-wrapper tests — see them fail for the right reason**

Run: `dotnet test --filter FullyQualifiedName~TutorialGatedHudViewTests`
Expected:
- `BuyPeasantClick_RejectsWhenNextBeatIsEndTurn` PASSES (the existing wrapper still rejects on EndTurn-only tutorials).
- `BuyPeasantClick_Forwards_AndArms_WhenNextBeatIsBuyPeasant` FAILS — the wrapper currently always rejects, so `forwarded = false` and `IsArmedForBuyPeasant = false`.
- `BuyPeasantClick_SecondClick_RejectsAndDoesNotForward` FAILS for the same reason (arm never set).
- `CancelAction_Disarms_AndForwardsCancel` FAILS — the wrapper's Cancel pass-through doesn't disarm yet (and also `IsArmedForBuyPeasant` was never set).

If a different test fails (e.g., `EndTurnClick_Forwards`), Task 2 broke something — diagnose first.

- [ ] **Step 3.4: Update `TutorialGatedHudView.cs`**

Open `scripts/Tutorial/TutorialGatedHudView.cs`. Replace the entire file with:

```csharp
using System;

/// <summary>
/// IHudView wrapper for tutorial Preview. Same shape as
/// <see cref="TutorialGatedHexMapView"/>: subscribes to a real
/// IHudView, gates input events that map to player-action beats
/// (EndTurnClicked / BuyPeasantClicked / BuildTowerClicked),
/// passes through other inputs (Undo/Territory cycling/etc. — dev
/// affordances during Preview), and delegates output methods.
///
/// Phase 4 adds the BuyPeasant arm-or-reject path (the matching tile
/// click is gated by <see cref="TutorialGatedHexMapView"/>).
/// BuildTowerClicked stays "always reject" until Phase 6.
///
/// CancelActionPressed pass-through also calls
/// <see cref="TutorialPlayer.DisarmIfAny"/> so the player's arm state
/// stays in sync with the controller's pending-action mode.
///
/// Output methods (Refresh / SetMapLabel / ShowTutorialMessage /
/// HideTutorialMessage) delegate transparently — the controller's
/// view-update calls reach the real HUD unchanged.
/// </summary>
public sealed class TutorialGatedHudView : IHudView
{
    private readonly IHudView _real;
    private readonly TutorialPlayer _player;

    public TutorialGatedHudView(IHudView real, TutorialPlayer player)
    {
        _real = real;
        _player = player;

        // Gated input events (re-raised conditionally below).
        _real.EndTurnClicked += OnRealEndTurnClicked;
        _real.BuyPeasantClicked += OnRealBuyPeasantClicked;
        _real.BuildTowerClicked += OnRealBuildTowerClicked;

        // Cancel disarms the BuyPeasant arm in addition to passing through.
        // Out-of-order disarm (no current arm) is a no-op via DisarmIfAny.
        _real.CancelActionPressed += OnRealCancelActionPressed;

        // Pass-through input events: re-raise whatever the real fires.
        _real.UndoLastClicked += () => UndoLastClicked?.Invoke();
        _real.UndoTurnClicked += () => UndoTurnClicked?.Invoke();
        _real.RedoLastClicked += () => RedoLastClicked?.Invoke();
        _real.RedoAllClicked += () => RedoAllClicked?.Invoke();
        _real.NewGameClicked += () => NewGameClicked?.Invoke();
        _real.MainMenuClicked += () => MainMenuClicked?.Invoke();
        _real.NextTerritoryClicked += () => NextTerritoryClicked?.Invoke();
        _real.PreviousTerritoryClicked += () => PreviousTerritoryClicked?.Invoke();
        _real.NextUnitClicked += () => NextUnitClicked?.Invoke();
        _real.PreviousUnitClicked += () => PreviousUnitClicked?.Invoke();
        _real.SaveGameClicked += () => SaveGameClicked?.Invoke();
        _real.DefeatContinueClicked += () => DefeatContinueClicked?.Invoke();
        _real.ClaimVictoryWinNowClicked += () => ClaimVictoryWinNowClicked?.Invoke();
        _real.ClaimVictoryContinueClicked += () => ClaimVictoryContinueClicked?.Invoke();
    }

    public void Unbind()
    {
        _real.EndTurnClicked -= OnRealEndTurnClicked;
        _real.BuyPeasantClicked -= OnRealBuyPeasantClicked;
        _real.BuildTowerClicked -= OnRealBuildTowerClicked;
        _real.CancelActionPressed -= OnRealCancelActionPressed;
        // Pass-through lambdas can't be unsubscribed (closures don't
        // compare equal); they keep the real view alive until the
        // real view itself is freed. PreviewPane drops both the real
        // view and the wrapper at the same teardown point, so this is
        // safe — neither outlives the other.
    }

    // --- Input events ---

    public event Action? BuyPeasantClicked;
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
    public event Action? PreviousUnitClicked;
    public event Action? CancelActionPressed;
    public event Action? SaveGameClicked;
    public event Action? DefeatContinueClicked;
    public event Action? ClaimVictoryWinNowClicked;
    public event Action? ClaimVictoryContinueClicked;

    private void OnRealEndTurnClicked()
    {
        if (_player.TryAdvanceForEndTurn())
        {
            EndTurnClicked?.Invoke();   // forward to controller
        }
        // If TryAdvanceForEndTurn returned false it has already fired
        // PlayerActionRejected — PreviewPane's subscription shows the
        // toast via _real.ShowTutorialMessage.
    }

    private void OnRealBuyPeasantClicked()
    {
        // Phase 4: arm if the next beat is BuyPeasantBeat (and we're
        // not already armed); the matching tile click is gated by
        // TutorialGatedHexMapView. TryArmBuyPeasant fires
        // PlayerActionRejected on its own when refusing.
        if (_player.TryArmBuyPeasant())
        {
            BuyPeasantClicked?.Invoke();   // forward — controller enters BuyingPeasant mode
        }
    }

    private void OnRealBuildTowerClicked()
    {
        // Phase 6 will add the BuildTowerBeat arm path. Until then, reject.
        _player.NotifyRejected("Build Tower");
    }

    private void OnRealCancelActionPressed()
    {
        // Cancel exits the controller's pending action mode (whether or
        // not the user was armed); make sure our arm state matches so
        // the next BuyPeasant click can re-arm.
        _player.DisarmIfAny();
        CancelActionPressed?.Invoke();
    }

    // --- Output methods: pure delegation ---

    public void Refresh(GameState state, SessionState session, bool hasActionableRemaining) =>
        _real.Refresh(state, session, hasActionableRemaining);
    public void SetMapLabel(string text) => _real.SetMapLabel(text);
    public void ShowTutorialMessage(string text) => _real.ShowTutorialMessage(text);
    public void HideTutorialMessage() => _real.HideTutorialMessage();
}
```

- [ ] **Step 3.5: Re-run the HUD-wrapper tests — green**

Run: `dotnet build FourExHex.csproj && dotnet test --filter FullyQualifiedName~TutorialGatedHudViewTests`
Expected: **8 PASS** — 4 unchanged (`EndTurnClick_Forwards_WhenNextBeatIsEndTurn`, `EndTurnClick_DoesNotForward_AfterTutorialFinished`, `UndoLastClick_PassesThrough_Unchanged`, `OutputMethods_DelegateToReal`) + 1 in-place swap (`BuyPeasantClick_AlwaysRejects_InPhase3c` → `BuyPeasantClick_RejectsWhenNextBeatIsEndTurn`) + 3 new (`BuyPeasantClick_Forwards_AndArms_WhenNextBeatIsBuyPeasant`, `BuyPeasantClick_SecondClick_RejectsAndDoesNotForward`, `CancelAction_Disarms_AndForwardsCancel`).

- [ ] **Step 3.6: Add tests for the HexMap wrapper's armed-tile flow**

Open `tests/TutorialGatedHexMapViewTests.cs`. Add these tests inside the class:

```csharp
private static (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player)
    SetupBuyPeasantArmed(HexCoord at)
{
    var tutorial = new Tutorial
    {
        Beats = new List<Beat>
        {
            new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = at },
        },
    };
    var player = new TutorialPlayer(tutorial);
    var real = new MockHexMapView();
    var gated = new TutorialGatedHexMapView(real, player);
    player.TryArmBuyPeasant();   // simulate the HUD-wrapper having armed
    return (gated, real, player);
}

[Fact]
public void TileClick_WhenArmed_AndMatches_Forwards_AndAdvances()
{
    HexCoord at = new HexCoord(4, 5);
    (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player) =
        SetupBuyPeasantArmed(at);
    var hex = new HexTile(at, new Color(1, 0, 0));
    HexTile? forwarded = null;
    gated.TileClicked += t => forwarded = t;

    real.SimulateClick(hex);

    Assert.Same(hex, forwarded);
    Assert.False(player.IsArmedForBuyPeasant);
    Assert.Equal(0, player.CurrentBeatIndex);
}

[Fact]
public void TileClick_WhenArmed_AndMismatches_DoesNotForward_AndKeepsArm()
{
    (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player) =
        SetupBuyPeasantArmed(new HexCoord(4, 5));
    var wrong = new HexTile(new HexCoord(4, 4), new Color(1, 0, 0));
    bool forwarded = false;
    bool rejected = false;
    gated.TileClicked += _ => forwarded = true;
    player.PlayerActionRejected += (_, _) => rejected = true;

    real.SimulateClick(wrong);

    Assert.False(forwarded);
    Assert.True(rejected);
    Assert.True(player.IsArmedForBuyPeasant);   // controller stays in BuyingPeasant mode
    Assert.Equal(-1, player.CurrentBeatIndex);
}
```

- [ ] **Step 3.7: Run the HexMap wrapper tests — see new ones fail for the right reason**

Run: `dotnet test --filter FullyQualifiedName~TutorialGatedHexMapViewTests`
Expected:
- 4 existing tests PASS.
- `TileClick_WhenArmed_AndMatches_Forwards_AndAdvances` FAILS — currently `OnRealTileClicked` passive-forwards regardless of arm state, so `forwarded` is true (good) but `IsArmedForBuyPeasant` stays true and `CurrentBeatIndex` stays -1 (the wrapper never called `TryAdvanceForBuyPeasantTile`).
- `TileClick_WhenArmed_AndMismatches_DoesNotForward_AndKeepsArm` FAILS — currently `forwarded` is true (passive forward).

- [ ] **Step 3.8: Update `TutorialGatedHexMapView.cs`**

Open `scripts/Tutorial/TutorialGatedHexMapView.cs`. Replace the body of `OnRealTileClicked` (around line 48):

```csharp
    private void OnRealTileClicked(HexTile? tile)
    {
        // Phase 3c: no tile-action beats exist. Forward all clicks to
        // the controller as passive selection — selection doesn't
        // advance the tutorial, so it's safe to let the dev poke the
        // map. Phase 4+ adds the TutorialValidator gate against
        // Move / BuyPeasant / BuildTower beats.
        TileClicked?.Invoke(tile);
    }
```

with:

```csharp
    private void OnRealTileClicked(HexTile? tile)
    {
        // Phase 4: if the player is armed for BuyPeasant (HUD wrapper
        // forwarded the Buy Peasant click), this tile click is the
        // follow-up that completes the BuyPeasantBeat. Validate the
        // coord; on match, advance + forward (controller fires
        // ExecuteBuyAndPlace); on miss, reject + don't forward
        // (controller stays in BuyingPeasant mode for the dev to retry).
        if (_player.IsArmedForBuyPeasant && tile != null)
        {
            if (_player.TryAdvanceForBuyPeasantTile(tile.Coord))
            {
                TileClicked?.Invoke(tile);
            }
            // else: rejected; PlayerActionRejected already fired with reason.
            return;
        }

        // Phase 4: not armed for any tile-action beat (and BuyPeasant
        // is the only one that exists). Forward as passive selection.
        // Phase 5 (Move) extends this with a Src/Dst two-click sequence.
        TileClicked?.Invoke(tile);
    }
```

Also update the file's class-level XML doc summary (lines 1-25) to reflect Phase 4. Replace the comment block with:

```csharp
/// <summary>
/// IHexMapView wrapper for tutorial Preview. Subscribes to a "real"
/// IHexMapView; for input events (TileClicked, TileLongClicked)
/// decides whether the click matches the next expected scripted beat
/// (via <see cref="TutorialPlayer"/>) and forwards to the controller
/// accordingly. Output methods delegate to the real view unchanged.
///
/// Phase 4: when <see cref="TutorialPlayer.IsArmedForBuyPeasant"/> is
/// true, tile clicks route through
/// <see cref="TutorialPlayer.TryAdvanceForBuyPeasantTile"/> — match
/// advances the beat + forwards; mismatch rejects + doesn't forward
/// (controller stays in BuyingPeasant mode for retry). All other
/// tile clicks pass through as passive selection. Phase 5 adds the
/// MoveBeat Src/Dst two-click sequence on top of this.
///
/// Long-press (rally) stays a passive forward in Phase 4 (no rally
/// beat exists; rally is dev affordance during Preview).
///
/// Call <see cref="Unbind"/> on Preview teardown to release the
/// subscription to the real view (otherwise the real view holds a
/// reference to this wrapper's handler, preventing garbage collection
/// of the wrapper / TutorialPlayer / GameController graph).
/// </summary>
```

- [ ] **Step 3.9: Re-run the HexMap-wrapper tests — green**

Run: `dotnet build FourExHex.csproj && dotnet test --filter FullyQualifiedName~TutorialGatedHexMapViewTests`
Expected: 6 PASS (4 existing + 2 new).

- [ ] **Step 3.10: Run the full suite**

Run: `dotnet test`
Expected: 761 tests pass (was 756 after Task 2; +3 HUD + 2 HexMap = 761). The CS0067 warning on `BuyPeasantClicked` from the build output should be gone (it was unused in 3c; Phase 4 wires it up).

- [ ] **Step 3.11: Commit**

```bash
git add scripts/Tutorial/TutorialGatedHudView.cs scripts/Tutorial/TutorialGatedHexMapView.cs tests/TutorialGatedHudViewTests.cs tests/TutorialGatedHexMapViewTests.cs
git commit -m "Wire BuyPeasant arm/advance through gated wrappers (TutorialBuilder Phase 4)"
```

---

## Task 4: AiSimulator equivalence test (TDD)

**Files:**
- Create: `tests/TutorialBeatSimulatorTests.cs`
- Modify: `tests/FourExHex.Tests.csproj` (add `<Compile Include>` for the new test file *only if* xUnit doesn't auto-discover it — explicit include matches the project's existing pattern)

This task verifies that the existing `AiSimulator.Apply(AiBuyUnitAction)` produces the post-state a `BuyPeasantBeat` describes. Phase 11's `BuildPane` post-beat state cache will use this (`BeatToAiAction` conversion → `AiSimulator.Clone` → `AiSimulator.Apply`); landing the equivalence check now means Phase 11 doesn't have to re-prove the simulator's behaviour.

The test conversion (`BuyPeasantBeat → AiBuyUnitAction`) is inlined here because it's a one-liner and no production-code consumer exists yet; Phase 11 will extract a helper if/when a second consumer arrives.

- [ ] **Step 4.1: Write failing test for the equivalence**

Create `tests/TutorialBeatSimulatorTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Verifies that the existing AiSimulator paths produce the same
/// post-state as the corresponding tutorial beats. Phase 11's
/// BuildPane post-beat state cache will rely on this — given a Beat,
/// it converts to the equivalent AiAction and runs it through
/// AiSimulator.Clone + Apply. The conversion is inlined here as a
/// one-line helper; a production helper is deferred until Phase 11
/// has a second consumer.
/// </summary>
public class TutorialBeatSimulatorTests
{
    [Fact]
    public void ApplyBuyPeasantEquivalent_PlacesPeasant_AndDeductsGold()
    {
        // 5x5 single-color grid → one big red territory; CapitalReconciler
        // assigns the capital somewhere inside it. State construction
        // mirrors TutorialSerializerTests.BuildMinimalState's pattern
        // (HexGrid + BuildTerritoriesFromGrid + TurnState + Treasury).
        var red = new Player("Red", new Color("e53935"), AiKind.Human);
        var players = new List<Player> { red };
        HexGrid grid = TestHelpers.BuildRectGrid(5, 5, red.Color);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var turnState = new TurnState(players, currentPlayerIndex: 0, turnNumber: 0);
        var state = new GameState(grid, territories, players, turnState, new Treasury());

        Territory redTerritory = state.Territories.First(t => t.Owner == red.Color);
        HexCoord capital = redTerritory.Capital!.Value;
        // Pick the first non-capital, currently-empty tile in the territory —
        // any such tile is a legal peasant target.
        HexCoord at = redTerritory.Coords.First(c =>
            c != capital && state.Grid.Get(c)!.Occupant == null);

        // Seed the capital with peasant-cost gold so the buy is affordable.
        state.Treasury.SetGold(capital, PurchaseRules.CostFor(UnitLevel.Peasant));
        int goldBefore = state.Treasury.GetGold(capital);

        // Equivalent of: BuyPeasantBeat { At = at } applied to this state.
        // (The conversion is intentionally inline — a helper would be
        // premature with one consumer; Phase 11 may extract it.)
        var beat = new BuyPeasantBeat { Index = 0, Turn = 1, Actor = 0, At = at };
        var action = new AiBuyUnitAction(capital, beat.At, UnitLevel.Peasant);

        AiSimulator.Apply(action, state);

        // (a) A red Peasant unit appears at At.
        HexTile after = state.Grid.Get(at)!;
        Unit placed = Assert.IsType<Unit>(after.Occupant);
        Assert.Equal(red.Color, placed.Owner);
        Assert.Equal(UnitLevel.Peasant, placed.Level);
        // (b) Capital gold deducted by peasant cost.
        Assert.Equal(goldBefore - PurchaseRules.CostFor(UnitLevel.Peasant),
                     state.Treasury.GetGold(capital));
    }
}
```

- [ ] **Step 4.2: Build the test assembly — verify the file is picked up**

Run: `dotnet build tests/FourExHex.Tests.csproj`

If you see a warning/error about the file not being included, open `tests/FourExHex.Tests.csproj` and check for an `<ItemGroup>` that lists test files. The existing csproj enumerates only production sources via `<Compile Include="..\scripts\...">`; xUnit auto-discovers *.cs files in the `tests/` directory (the csproj has `Microsoft.NET.Sdk` defaults). Building should "just work". If it doesn't, add this line near the existing test-file siblings:

```xml
    <Compile Include="TutorialBeatSimulatorTests.cs" />
```

(Most likely no edit is needed.)

- [ ] **Step 4.3: Run the test — see it pass (production code unchanged; this is a *verification* test)**

Run: `dotnet test --filter FullyQualifiedName~TutorialBeatSimulatorTests.ApplyBuyPeasantEquivalent_PlacesPeasant_AndDeductsGold`
Expected: PASS.

If it FAILS, the failure mode tells you what's wrong:
- `InvalidOperationException: Sequence contains no matching element` from the `.First(...)` on `Coords` → the 5×5 grid produced no non-capital empty tile, which means `BuildRectGrid` / `BuildTerritoriesFromGrid` shape changed. Print the territory size + capital coord to diagnose.
- `Assert.IsType<Unit>` failure (occupant is null or wrong type) → `AiSimulator.Apply(AiBuyUnitAction)` didn't place. Re-read `scripts/AiSimulator.cs:99-119` (`ApplyBuy`) and confirm `MovementRules.PlaceNew` is being called with the right level + owner.
- Gold mismatch → either `PurchaseRules.CostFor(UnitLevel.Peasant)` shifted from 10 or `AiSimulator.ApplyBuy` is double-deducting.

This test is intentionally green-on-arrival. The TDD value here is the documented invariant ("BuyPeasant beat ≡ AiBuyUnitAction with the right capital + level"), not the red→green cycle. If you'd like a stronger TDD shape, briefly mutate `AiSimulator.ApplyBuy` to skip the gold deduct, watch the test go red, then revert — but that's an engineer-discretion exercise, not a plan requirement.

- [ ] **Step 4.4: Run the full suite**

Run: `dotnet test`
Expected: 762 tests pass (was 761 after Task 3; +1 new).

- [ ] **Step 4.5: Commit**

```bash
git add tests/TutorialBeatSimulatorTests.cs
# Also stage tests/FourExHex.Tests.csproj if Step 4.2 needed an explicit <Compile Include>.
git commit -m "Add TutorialBeatSimulatorTests for BuyPeasant simulator equivalence (TutorialBuilder Phase 4)"
```

---

## Task 5: Grow `BuildPane` with "Add BuyPeasant" + tile-pick mode

**Files:**
- Modify: `scripts/BuildPane.cs`

This task is view-layer; verified by Task 6 manual test. The chrome additions:

- A new "Add BuyPeasant" button in the right strip below "Add EndTurn".
- A `_pendingPick` enum field (`None` or `BuyPeasantAt`); when set, the next `Map.CoordClicked` consumes the click and creates a `BuyPeasantBeat` at that coord (then resets to `None`).
- The button's `Pressed` handler subscribes to `_panel.Map.CoordClicked` once (idempotent — disconnect any previous subscription first to handle re-clicks).
- Timeline chip label is extended to show the `At` coord for `BuyPeasantBeat`.

The `_panel` reference (set by `TutorialBuilderScene` via `SetPanel`) is finally consumed in Phase 4 — the suppression `_ = _panel;` line from Phase 3b can be removed.

There's no "what tile is currently owned by actor 0" validation at click time — Phase 12 surfaces invalid `At`s as warnings. For Phase 4 the dev tester is trusted to click a friendly tile.

- [ ] **Step 5.1: Update `scripts/BuildPane.cs`**

Open `scripts/BuildPane.cs`. Apply these changes:

(a) Add a private nested enum + field at the top of the class, just below the field block ending with `_inspectorActor` (around line 30):

```csharp
    private enum PickMode { None, BuyPeasantAt }
    private PickMode _pendingPick = PickMode.None;
```

(b) Replace `SetPanel` (around line 46-50) — drop the `_ = _panel;` suppression:

```csharp
    /// <summary>Called once by TutorialBuilderScene before AddChild.
    /// Phase 4 consumes the panel reference for the "Add BuyPeasant"
    /// tile-pick flow (subscribes to the panel's HexMapView click event).
    /// Phase 11 will additionally use it for the state-after-beat-N cache.</summary>
    public void SetPanel(MapEditorPanel panel)
    {
        _panel = panel;
    }
```

(c) In `BuildRightStrip`, find the block adding `addEndTurnBtn` (around line 123-131):

```csharp
        var addEndTurnBtn = new Button
        {
            Text = "Add EndTurn",
            FocusMode = FocusModeEnum.None,
        };
        addEndTurnBtn.AddThemeFontSizeOverride("font_size", 18);
        addEndTurnBtn.Pressed += OnAddEndTurnPressed;
        AudioBus.AttachClick(addEndTurnBtn);
        content.AddChild(addEndTurnBtn);
```

Add immediately below it (still inside `BuildRightStrip`, before the spacer):

```csharp
        var addBuyPeasantBtn = new Button
        {
            Text = "Add BuyPeasant",
            FocusMode = FocusModeEnum.None,
        };
        addBuyPeasantBtn.AddThemeFontSizeOverride("font_size", 18);
        addBuyPeasantBtn.Pressed += OnAddBuyPeasantPressed;
        AudioBus.AttachClick(addBuyPeasantBtn);
        content.AddChild(addBuyPeasantBtn);
```

(d) Update the chip-label string in `RefreshUI` (around line 222) so `BuyPeasantBeat` carries its `At` coord on the chip. Replace the line:

```csharp
            string label = $"#{beat.Index} T{beat.Turn} A{beat.Actor} {beat.Kind}";
```

with:

```csharp
            string label = beat switch
            {
                BuyPeasantBeat bpb => $"#{beat.Index} T{beat.Turn} A{beat.Actor} BuyPeasant ({bpb.At.Q},{bpb.At.R})",
                _                  => $"#{beat.Index} T{beat.Turn} A{beat.Actor} {beat.Kind}",
            };
```

(e) Add the new handler at the bottom of the class, just before the closing brace:

```csharp
    private void OnAddBuyPeasantPressed()
    {
        // Idempotent: re-pressing while already in pick mode is a no-op
        // (the existing subscription remains active). Cancelling pick
        // mode (ESC, switch mode) is a Phase 14 polish item; for Phase
        // 4 the dev commits the pick by clicking a tile.
        if (_pendingPick == PickMode.BuyPeasantAt) return;
        _pendingPick = PickMode.BuyPeasantAt;
        _panel.Map.CoordClicked += OnPickCoordClicked;
    }

    private void OnPickCoordClicked(HexCoord coord)
    {
        if (_pendingPick != PickMode.BuyPeasantAt) return;

        // Phase 4 accepts any coord (no friendly-territory validation
        // here — Phase 12 surfaces invalid At as a warning). Trust the
        // dev to click a tile they own.
        var beats = new List<Beat>(_tutorial.Beats)
        {
            new BuyPeasantBeat
            {
                Index = _tutorial.Beats.Count,
                Turn = 1,
                Actor = 0,
                At = coord,
            },
        };
        _tutorial = new Tutorial
        {
            Title = _tutorial.Title,
            StartTurn = _tutorial.StartTurn,
            StartPlayer = _tutorial.StartPlayer,
            Beats = beats,
        };

        // Exit pick mode + unsubscribe so subsequent map clicks pass
        // through to whatever owns selection in Build (currently nobody).
        _pendingPick = PickMode.None;
        _panel.Map.CoordClicked -= OnPickCoordClicked;

        RefreshUI();
    }
```

(f) Update the class-level XML doc summary at the top of `BuildPane.cs` (around lines 4-16) so future readers know what's in scope:

Replace:

```csharp
/// Build mode chrome. Phase 3b: right strip with "Add EndTurn" button +
/// selected-beat inspector, plus bottom timeline of beat chips.
/// Phase 4+ extends with more beat-add buttons (BuyPeasant / Move /
/// BuildTower); Phase 7+ adds overlay-beat editors (Prompt / Highlight
/// / CameraFocus); Phase 11 adds editing / reorder / delete; Phase 12
/// adds validation banners.
```

with:

```csharp
/// Build mode chrome. Phase 3b shipped right strip with "Add EndTurn" +
/// inspector + bottom timeline. Phase 4 adds "Add BuyPeasant" with a
/// tile-pick state (`_pendingPick`); next Map.CoordClicked while in
/// pick mode appends a BuyPeasantBeat at that coord. Phase 5 adds
/// MoveBeat (Src/Dst two-click pick), Phase 6 adds BuildTower; Phase
/// 7+ adds overlay-beat editors (Prompt / Highlight / CameraFocus);
/// Phase 11 adds editing / reorder / delete + the kind-specific
/// inspector + the post-beat state cache; Phase 12 adds validation
/// banners; Phase 14 adds keyboard shortcuts + ESC ladder.
```

- [ ] **Step 5.2: Build the game DLL**

Run: `dotnet build FourExHex.csproj`
Expected: Build succeeded, 0 errors. The CS0067 warning on `BuyPeasantClicked` (from Phase 3c's unused event) should also be gone after Task 3.

- [ ] **Step 5.3: Run the full test suite (sanity)**

Run: `dotnet test`
Expected: 762 tests pass. (BuildPane is view-layer, not in the test csproj — Task 5 doesn't shift the count.)

- [ ] **Step 5.4: Commit**

```bash
git add scripts/BuildPane.cs
git commit -m "Grow BuildPane: Add BuyPeasant button + tile-pick mode (TutorialBuilder Phase 4)"
```

---

## Task 6: Manual test + ARCHITECTURE.md update + mark Phase 4 complete

**Files:** `ARCHITECTURE.md` (small incremental updates); `docs/superpowers/plans/2026-05-09-tutorial-builder-master.md` (status flip).

- [ ] **Step 6.1: Confirm full build is current**

Run: `dotnet build FourExHex.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6.2: Hand off to user for manual test**

The game running on the user's machine has stale code. Tell the user it needs to be relaunched and ask them to walk through the manual test, OR (per their session preference) ask whether you should kill + relaunch the background game process.

Manual test sequence:

1. Open Tutorial Builder. Switch to **Map Edit** (kbd `1`). Paint a small territory (≥5 tiles, single colour for player 0 / red) with a capital — Generate Map is fine; otherwise paint a 3×3 red blob, then Save Map / Reload to let the capital placer assign one. Goal: end up with at least one red territory of ≥3 tiles holding a capital + a couple of empty non-capital tiles.
2. Switch to **Build** mode (kbd `2`). The right strip now shows two buttons: "Add EndTurn" and "Add BuyPeasant".
3. Click **Add BuyPeasant**. Expected: no immediate visible change in the strip / timeline; the pane is now waiting for a tile click.
4. Click a non-capital tile in your red territory. Expected: a chip appears in the timeline labeled `#0 T1 A0 BuyPeasant (q,r)` where `(q,r)` is the coord you clicked.
5. Click the chip. Expected: it greys (selected); inspector below shows `Selected beat / Turn: 1 / Actor: 0`. (The kind-specific inspector — showing `At: (q,r)` — is Phase 11; for Phase 4 the chip label carries the coord.)
6. Click **Save Tutorial** → name "test_p4" → Save.
7. Switch to **Preview** mode (kbd `3`). Expected: the topbar disappears; the real HudView appears at the top; the painted map renders below.
8. Click **Buy Peasant** in the HUD. Expected: enters BuyingPeasant mode (you should see green move-target highlights on legal tiles within the territory). No toast.
9. Click a tile that is NOT the designated `At`. Expected: soft-reject toast at the bottom — "Expected BuyPeasant (turn 1, actor 0); got tile click at (q,r)." The green highlights remain (controller still in BuyingPeasant mode); your gold is unchanged.
10. Click the designated `At` tile. Expected: a red Peasant unit appears on that tile; the capital's gold drops by 10; the toast updates to "Tutorial complete." (The single beat finished.)
11. Click another tile. Expected: soft-reject toast — "Tutorial complete — no further actions expected."
12. Press **ESC**. Expected: drops back to Map Edit; topbar reappears; the HudView is gone; the painted map is back to the authored draft (no leftover unit).
13. Switch to Build (kbd `2`). Expected: the BuyPeasant chip is still there (BuildPane state persisted).
14. Switch back to Preview (kbd `3`); this time, click **Buy Peasant**, then the wrong tile (toast), then the right tile (peasant placed, "Tutorial complete"). Confirms the retry path works.
15. Switch back to Build, click **Add BuyPeasant** again, then click a tile, then click **Add EndTurn**. Expected: a second BuyPeasant chip appears (with the second `(q,r)`), then an EndTurn chip after it. Save, switch to Preview, walk through both beats: Buy Peasant + tile, then End Turn → "Tutorial complete."
16. Verify the JSON on disk:
    ```
    head -c 2000 ~/Library/Application\ Support/Godot/app_userdata/FourExHex/tutorials/test_p4.json | tail -c 800
    ```
    Expected: the `"Tutorial":` block has a `"Beats":` array; the BuyPeasant entry has `"Kind": "BuyPeasant"`, `"AtQ": <q>`, `"AtR": <r>`.
17. Click **Cancel** on the in-Preview HUD while armed (after clicking Buy Peasant but before clicking a tile). Expected: BuyingPeasant mode exits (highlights vanish); no toast. Click Buy Peasant again → fresh arm; the cycle continues normally. (This verifies the disarm-on-cancel wiring from Task 3.)

Wait for the user to confirm before proceeding.

- [ ] **Step 6.3: Update ARCHITECTURE.md (after user confirms manual test)**

Per CLAUDE.md's architecture-doc-before-push rule, when the user is ready to push, ask whether `ARCHITECTURE.md` should be updated. If yes, make these incremental edits.

(a) In the Tutorial builder section, find the `Phase 3c scope.` paragraph (added by Phase 3c) and add a sibling paragraph immediately after it:

```markdown
- **Phase 4 scope.** Adds the first tile-action beat: `BuyPeasantBeat`
  (with `HexCoord At`). Authoring: `BuildPane` grows an "Add BuyPeasant"
  button + a `_pendingPick` tile-pick mode that consumes the next
  `Map.CoordClicked` to materialize the beat. Preview: the gating uses
  a single-slot `_armedBeat` on `TutorialPlayer` to mirror the real
  game's two-event sequence — `TutorialGatedHudView`'s
  `OnRealBuyPeasantClicked` calls `TryArmBuyPeasant` (forwards on
  success, controller enters BuyingPeasant mode), and
  `TutorialGatedHexMapView`'s `OnRealTileClicked` short-circuits to
  `TryAdvanceForBuyPeasantTile` when armed (forwards on coord match,
  rejects on miss while keeping the arm so the dev can retry).
  `CancelActionPressed` pass-through also disarms so arm state stays in
  sync with the controller's pending-action mode. New JSON fields on
  `BeatDto`: `AtQ` / `AtR` (nullable, populated only for tile-anchored
  beats — Phase 5/6 reuse them). `TutorialBeatSimulatorTests` documents
  the BuyPeasant-beat ≡ `AiBuyUnitAction` equivalence so Phase 11's
  state-after-beat-N cache can build on `AiSimulator.Apply` without
  re-deriving the invariants.
```

(b) In the file layout, find the `Tutorial/Beat.cs` line and update its inline description:

```
├─ Tutorial/Beat.cs       ─ Beat abstract record + BeatKind enum +
│                           EndTurnBeat + BuyPeasantBeat (Phase 5+
│                           adds more concrete kinds)
```

Then commit:

```bash
git add ARCHITECTURE.md
git commit -m "Update ARCHITECTURE.md for TutorialBuilder Phase 4"
```

- [ ] **Step 6.4: Mark Phase 4 complete in the master plan**

Edit `docs/superpowers/plans/2026-05-09-tutorial-builder-master.md`. Find the `### Phase 4` heading and replace its `**Status:** ⏳ Not yet expanded` line with two lines:

```markdown
- **Status:** ✅ Complete
- **Plan file:** [`2026-05-09-tutorial-builder-phase-04-buypeasant-beat.md`](2026-05-09-tutorial-builder-phase-04-buypeasant-beat.md)
```

Commit:

```bash
git add docs/superpowers/plans/2026-05-09-tutorial-builder-master.md
git commit -m "Mark TutorialBuilder Phase 4 complete"
```

- [ ] **Step 6.5: Report Phase 4 complete**

Tell the user: "Phase 4 (author + preview a `BuyPeasantBeat`) is complete. Phase 5 is next — adds `MoveBeat` (POCO + serializer + BuildPane 'Add Move' two-click Src/Dst pick + gated tile-click sequence + `TutorialValidator.MatchesMove`). It depends on Phase 4 and is currently `⏳ Not yet expanded`. Want me to expand 5 now?"

Stop. Do not start Phase 5 without explicit go-ahead.

---

## Cross-cutting reminders

- **Rebuild before launch:** After any `.cs` change, `dotnet build FourExHex.csproj` before relaunching Godot. (CLAUDE.md.)
- **Strict TDD on logic changes:** Tasks 1, 2, 3 follow the full red-green loop. Task 4 ships a verification test (production code unchanged — the test is documenting an existing invariant). Task 5 is view-layer (Godot `Control` deps; test-excluded) and verified by the manual test in Task 6.
- **No new `.cs.uid` sidecars** — Phase 4 modifies existing scripts under `scripts/Tutorial/` but adds no new production files. (`tests/TutorialBeatSimulatorTests.cs` is a test file; tests don't get UIDs.)
- **Test count tracking:** 746 (start, baseline measured) → 747 (Task 1: +1 serializer) → 756 (Task 2: +2 Validator + 7 Player) → 761 (Task 3: +3 HUD + 2 HexMap) → 762 (Task 4: +1 simulator). Final total at end of Phase 4: **762**.
- **Architecture-doc-before-push rule:** Step 6.3 asks the user before updating `ARCHITECTURE.md`; do not push without confirmation.
- **Manual-test-after-every-change rule:** Step 6.2 hands off to the user. Do not push or move to Phase 5 without an explicit "manual test passed" confirmation.
