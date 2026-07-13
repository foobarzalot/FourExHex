using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// During paced replay playback, a Move beat's preview phase must show
/// the unit being picked up (<c>ShowMoveSource</c> — the same pickup
/// pulse a live player sees when selecting a unit to move), cleared
/// once the move executes. This is what makes the Instructions demos
/// read "select the unit, then it moves" instead of units teleporting.
/// </summary>
public class ReplayMoveSourcePreviewTests
{
    [Fact]
    public void Replay_MoveBeatPreview_ShowsMoveSourceThenClearsIt()
    {
        var pacer = new QueuedAiPacer();
        ControllerHarness h = TestHelpers.BuildControllerGame(aiPacer: pacer);
        pacer.DrainAll();

        // Live script: Red buys a recruit on its non-capital tile, then
        // moves it to capture the adjacent enemy tile — [Buy, Move].
        HexCoord capital = h.State.Territories
            .First(t => t.Owner == h.Players[0].Id).Capital!.Value;
        HexCoord from = HexCoord.FromOffset(0, 1).Equals(capital)
            ? HexCoord.FromOffset(1, 1)
            : HexCoord.FromOffset(0, 1);
        HexCoord to = HexCoord.FromOffset(2, 1);

        h.Map.SimulateClick(h.State.Grid.Get(capital)!);
        h.Hud.ClickBuyRecruit();
        h.Map.SimulateClick(h.State.Grid.Get(from)!);
        h.Map.SimulateClick(h.State.Grid.Get(from)!);   // pick the unit up
        h.Map.SimulateClick(h.State.Grid.Get(to)!);     // capture move
        pacer.DrainAll();

        Assert.Equal(2, h.Controller.ReplayBeats.Count);
        Assert.IsType<ReplayMoveBeat>(h.Controller.ReplayBeats[1]);

        h.Controller.BeginReplay();

        pacer.StepOne();                                 // preview: Buy
        Assert.Null(h.Map.LastMoveSource);
        pacer.StepOne();                                 // execute: Buy
        pacer.StepOne();                                 // preview: Move
        Assert.Equal(from, h.Map.LastMoveSource);        // pickup pulse on the unit
        pacer.StepOne();                                 // execute: Move
        Assert.Null(h.Map.LastMoveSource);               // cleared after the move
    }
}
