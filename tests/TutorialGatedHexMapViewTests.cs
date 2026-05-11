using System.Collections.Generic;
using Godot;
using Xunit;

namespace FourExHex.Tests;

public class TutorialGatedHexMapViewTests
{
    private static (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player)
        Setup(int beatCount = 1)
    {
        var beats = new List<Beat>();
        for (int i = 0; i < beatCount; i++)
            beats.Add(new EndTurnBeat { Index = i, Turn = 1, Actor = 0 });
        var tutorial = new Tutorial { Beats = beats };
        var player = new TutorialPlayer(tutorial);
        var real = new MockHexMapView();
        var gated = new TutorialGatedHexMapView(real, player);
        return (gated, real, player);
    }

    [Fact]
    public void TileClick_Forwards_AndDoesNotReject()
    {
        // Phase 3c: tile clicks always forward to the controller as
        // passive selection — selection doesn't advance the tutorial.
        // Phase 4+ adds gating against Move / BuyPeasant / BuildTower
        // beats; for now any tile click is benign.
        (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player) = Setup();
        HexTile? forwarded = null;
        bool forwardedFired = false;
        bool rejected = false;
        gated.TileClicked += t => { forwarded = t; forwardedFired = true; };
        player.PlayerActionRejected += (_, _) => rejected = true;

        real.SimulateClick(null);

        Assert.True(forwardedFired);
        Assert.Null(forwarded);   // null tile is the input
        Assert.False(rejected);
    }

    [Fact]
    public void TileLongClick_Forwards_AndDoesNotReject()
    {
        (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player) = Setup();
        bool forwarded = false;
        bool rejected = false;
        gated.TileLongClicked += _ => forwarded = true;
        player.PlayerActionRejected += (_, _) => rejected = true;

        real.SimulateLongClick(null);

        Assert.True(forwarded);
        Assert.False(rejected);
    }

    [Fact]
    public void OutputMethods_DelegateToReal()
    {
        (TutorialGatedHexMapView gated, MockHexMapView real, _) = Setup();

        gated.ShowHighlight(null);
        gated.PlayUnitPlaced(new HexCoord(1, 2));
        gated.RebuildAfterTerritoryChange();

        Assert.True(real.HighlightWasCleared);
        Assert.Single(real.UnitPlacedSounds);
        Assert.Equal(1, real.RebuildCount);
    }

    [Fact]
    public void Unbind_StopsForwardingClicks()
    {
        (TutorialGatedHexMapView gated, MockHexMapView real, _) = Setup();
        bool forwarded = false;
        gated.TileClicked += _ => forwarded = true;

        gated.Unbind();
        real.SimulateClick(null);

        Assert.False(forwarded);
    }

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
        Assert.True(player.IsArmedForBuyPeasant);
        Assert.Equal(-1, player.CurrentBeatIndex);
    }

    [Fact]
    public void ShowMoveTargets_WhenArmedForBuyPeasant_ForcesScriptedAtRing()
    {
        // Controller's coord set is action-consuming only (captures /
        // tree-clears). The scripted At may be a friendly empty tile
        // that isn't in that set; the wrapper still force-shows it so
        // the dev sees a single unambiguous ring at the target.
        HexCoord at = new HexCoord(4, 5);
        (TutorialGatedHexMapView gated, MockHexMapView real, _) =
            SetupBuyPeasantArmed(at);
        // Controller's set deliberately omits `at` (action-consuming
        // targets only — friendly empty doesn't make this list).
        var actionConsuming = new[] { new HexCoord(4, 4), new HexCoord(5, 5) };

        gated.ShowMoveTargets(actionConsuming, UnitLevel.Peasant);

        Assert.Equal(new[] { at }, real.LastMoveTargets);
    }

    [Fact]
    public void ShowMoveTargets_WhenNotArmed_ForwardsAll()
    {
        // Not armed → wrapper passes through all legal targets unchanged.
        (TutorialGatedHexMapView gated, MockHexMapView real, _) = Setup();
        var legal = new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1) };

        gated.ShowMoveTargets(legal, UnitLevel.Spearman);

        Assert.Equal(legal, real.LastMoveTargets);
    }

    [Fact]
    public void ShowMoveTargets_WhenNotArmed_EmptyStaysEmpty()
    {
        // Pass-through when not armed includes the empty case — the
        // controller uses ShowMoveTargets(empty) to clear the highlight
        // (e.g., on Cancel, on mode exit), and the wrapper must not
        // force a stray ring just because there's a tutorial loaded.
        (TutorialGatedHexMapView gated, MockHexMapView real, _) = Setup();

        gated.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Peasant);

        Assert.Empty(real.LastMoveTargets);
    }
}
