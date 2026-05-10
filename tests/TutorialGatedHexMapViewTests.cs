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
}
