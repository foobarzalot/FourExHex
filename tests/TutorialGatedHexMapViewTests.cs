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
    public void TileClick_DoesNotForward_AndFiresRejected()
    {
        (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player) = Setup();
        bool forwarded = false;
        Beat? rejected = null;
        gated.TileClicked += _ => forwarded = true;
        player.PlayerActionRejected += (b, _) => rejected = b;

        real.SimulateClick(null);

        Assert.False(forwarded);
        Assert.NotNull(rejected);
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
        (TutorialGatedHexMapView gated, MockHexMapView real, TutorialPlayer player) = Setup();
        bool rejected = false;
        player.PlayerActionRejected += (_, _) => rejected = true;

        gated.Unbind();
        real.SimulateClick(null);

        Assert.False(rejected);
    }
}
