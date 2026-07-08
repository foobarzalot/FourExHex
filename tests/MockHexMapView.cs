using System;
using System.Collections.Generic;
using System.Linq;

namespace FourExHex.Tests;

/// <summary>
/// In-memory <see cref="IHexMapView"/> for controller tests. Records the
/// last value passed to each method so tests can assert what the
/// controller told the view to do, and exposes a <c>SimulateClick</c>
/// helper to raise the <c>TileClicked</c> event.
/// </summary>
public class MockHexMapView : IHexMapView
{
    public event Action<HexTile?>? TileClicked;
    public event Action<HexTile?>? TileLongClicked;
    public event Action<HexCoord>? OffGridClicked;

    public List<HexCoord> LastMoveTargets { get; private set; } = new();
    /// <summary>The <see cref="UnitLevel"/> the controller most recently
    /// passed to <see cref="ShowMoveTargets"/>, or null if it has never
    /// been called. Used to verify the destination preview is sized for
    /// the source unit's level (e.g., a Soldier's preview should render
    /// two rings, not one).</summary>
    public UnitLevel? LastMoveTargetsLevel { get; private set; }
    /// <summary>Test hook: invoked-and-cleared at the top of the next
    /// <see cref="ShowMoveTargets"/> call. Used to simulate a mid-handler
    /// failure and verify the controller doesn't push a recovery snapshot.</summary>
    public Action? ThrowOnNextShowMoveTargets { get; set; }
    public void ShowMoveTargets(IEnumerable<HexCoord> coords, UnitLevel level)
    {
        Action? hook = ThrowOnNextShowMoveTargets;
        ThrowOnNextShowMoveTargets = null;
        hook?.Invoke();
        LastMoveTargets = coords.ToList();
        LastMoveTargetsLevel = level;
    }

    public List<HexCoord> LastTowerTargets { get; private set; } = new();
    public void ShowTowerTargets(IEnumerable<HexCoord> coords) =>
        LastTowerTargets = coords.ToList();

    public List<HexCoord> LastTowerCoverage { get; private set; } = new();
    public void ShowTowerCoverage(IEnumerable<HexCoord> coords) =>
        LastTowerCoverage = coords.ToList();

    public List<HexCoord> LastTideForecast { get; private set; } = new();
    public void ShowTideForecast(IEnumerable<TideStep> steps) =>
        LastTideForecast = steps.Select(s => s.Coord).ToList();

    /// <summary>The most recent at-sea raider + sea-grave lists the controller pushed.</summary>
    public List<SeaViking> LastSeaVikings { get; private set; } = new();
    public List<HexCoord> LastSeaGraves { get; private set; } = new();
    public void ShowSeaVikings(IReadOnlyList<SeaViking> atSea, IReadOnlyList<HexCoord> seaGraves)
    {
        LastSeaVikings = atSea.ToList();
        LastSeaGraves = seaGraves.ToList();
    }

    /// <summary>The most recent fog projection the controller pushed, or null
    /// if fog is off (the last <see cref="ShowFog"/> arg). <see cref="ShowFogCount"/>
    /// counts calls so tests can assert it was pushed.</summary>
    public FogView? LastFog { get; private set; }
    public int ShowFogCount { get; private set; }
    public void ShowFog(FogView? fog)
    {
        LastFog = fog;
        ShowFogCount++;
    }

    public HexCoord? LastMoveSource { get; private set; }
    public void ShowMoveSource(HexCoord? coord) => LastMoveSource = coord;

    public HexCoord? LastSelectUnitCue { get; private set; }
    public void ShowSelectUnitCue(HexCoord? coord) => LastSelectUnitCue = coord;

    public Territory? LastHighlight { get; private set; }
    public bool HighlightWasCleared { get; private set; }
    public void ShowHighlight(Territory? selected)
    {
        LastHighlight = selected;
        HighlightWasCleared = selected == null;
    }

    public Territory? LastCenteredTerritory { get; private set; }
    public HexCoord? LastCenteredCoord { get; private set; }
    public int CenterCount { get; private set; }
    public void CenterOnTerritory(Territory territory)
    {
        LastCenteredTerritory = territory;
        CenterCount++;
    }

    public void CenterOnCoord(HexCoord coord)
    {
        LastCenteredCoord = coord;
        CenterCount++;
    }

    public HexCoord? LastFocusPulseCoord { get; private set; }
    public int FocusPulseCount { get; private set; }
    public void ShowTerrainFocusPulse(HexCoord? coord)
    {
        LastFocusPulseCoord = coord;
        FocusPulseCount++;
    }

    public int RebuildCount { get; private set; }
    public void RebuildAfterTerritoryChange() => RebuildCount++;

    public int RefreshOccupantCount { get; private set; }
    public PlayerId? LastOccupantRefreshPlayer { get; private set; }
    public IReadOnlyCollection<HexCoord> LastVisitedCapitals { get; private set; } =
        System.Array.Empty<HexCoord>();
    public void RefreshOccupantVisuals(PlayerId? currentPlayer, Treasury treasury,
        IReadOnlySet<HexCoord> visitedCapitals)
    {
        RefreshOccupantCount++;
        LastOccupantRefreshPlayer = currentPlayer;
        LastVisitedCapitals = visitedCapitals.ToArray();
    }

    /// <summary>
    /// The last silent flag the controller pushed via <see cref="SetSilentMode"/>.
    /// Purely a recorded value now — the drop decision lives controller-side
    /// (<c>GameOperations.IsSilent</c> gates <c>EmitSound</c>/<c>EmitDestruction</c>),
    /// so this mock plays back everything it is told. <c>InstantAiTests</c> /
    /// <c>ReplayPlaybackTests</c> assert on this flag to verify the controller
    /// still drives the view's silent lifecycle (Instant AI / instant replay).
    /// </summary>
    public bool SilentMode { get; private set; }
    public void SetSilentMode(bool silent) => SilentMode = silent;

    public List<(HexCoord Coord, HexOccupant Destroyed)> DestructionEffects { get; } = new();
    public void PlayDestructionEffect(HexCoord coord, HexOccupant destroyed)
    {
        DestructionEffects.Add((coord, destroyed));
    }

    // Per-sound recording surface — tests assert against these. Each
    // property records every fire of the matching SoundEffect routed
    // through PlaySound. The mock records unconditionally; the controller
    // decides whether to emit (silent-mode gating is controller-side).
    public List<HexCoord> UnitPlacedSounds { get; } = new();
    public List<HexCoord> TowerPlacedSounds { get; } = new();
    public List<HexCoord> UnitCombinedSounds { get; } = new();
    public List<HexCoord> UnitDestroyedSounds { get; } = new();
    public List<HexCoord> TileSubmergedSounds { get; } = new();
    public List<HexCoord> VikingArrivalSounds { get; } = new();
    public List<HexCoord> TowerDestroyedSounds { get; } = new();
    public List<HexCoord> TreeClearedSounds { get; } = new();
    public List<HexCoord> CapitalDestroyedSounds { get; } = new();
    public int BankruptcySoundCount { get; private set; }
    public int GameWonSoundCount { get; private set; }
    public int RallySoundCount { get; private set; }
    public int PlayerDefeatedSoundCount { get; private set; }

    public void PlaySound(SoundEffect kind, HexCoord? at = null)
    {
        HexCoord coord = at ?? default;
        switch (kind)
        {
            case SoundEffect.UnitPlaced: UnitPlacedSounds.Add(coord); break;
            case SoundEffect.TowerPlaced: TowerPlacedSounds.Add(coord); break;
            case SoundEffect.UnitCombined: UnitCombinedSounds.Add(coord); break;
            case SoundEffect.UnitDestroyed: UnitDestroyedSounds.Add(coord); break;
            case SoundEffect.TileSubmerged: TileSubmergedSounds.Add(coord); break;
            case SoundEffect.VikingArrival: VikingArrivalSounds.Add(coord); break;
            case SoundEffect.TowerDestroyed: TowerDestroyedSounds.Add(coord); break;
            case SoundEffect.TreeCleared: TreeClearedSounds.Add(coord); break;
            case SoundEffect.CapitalDestroyed: CapitalDestroyedSounds.Add(coord); break;
            case SoundEffect.Bankruptcy: BankruptcySoundCount++; break;
            case SoundEffect.GameWon: GameWonSoundCount++; break;
            case SoundEffect.Rally: RallySoundCount++; break;
            case SoundEffect.PlayerDefeated: PlayerDefeatedSoundCount++; break;
        }
    }

    /// <summary>
    /// Records every rejection feedback event the controller raised. Each
    /// entry holds the target hex, the shape the player was trying to
    /// place, and the coords of defenders (empty for non-defense rejections).
    /// Tests assert against the last entry to verify the controller routed
    /// the right shape + defender set for each rejection site.
    /// </summary>
    public List<(HexCoord Target, RejectionShape Shape, HexCoord[] Defenders)> Rejections { get; } = new();
    public (HexCoord Target, RejectionShape Shape, HexCoord[] Defenders)? LastRejection =>
        Rejections.Count == 0 ? null : Rejections[Rejections.Count - 1];
    public void FlashRejection(HexCoord target, RejectionShape shape, IEnumerable<HexCoord> blockingDefenders) =>
        Rejections.Add((target, shape, blockingDefenders.ToArray()));

    /// <summary>Raise the TileClicked event, as if the user clicked.</summary>
    public void SimulateClick(HexTile? tile) => TileClicked?.Invoke(tile);

    /// <summary>Raise the TileLongClicked event, as if the user long-pressed.</summary>
    public void SimulateLongClick(HexTile? tile) => TileLongClicked?.Invoke(tile);

    /// <summary>Raise the OffGridClicked event, as if the user clicked a water or
    /// off-grid coord.</summary>
    public void SimulateOffGridClick(HexCoord coord) => OffGridClicked?.Invoke(coord);
}
