// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// <see cref="GameController.HumanEliminated"/>: the moment a human's last
/// capital falls. The campaign records the loss and stores the attempt
/// here, because the AIs may play on for many turns before GameEnded.
/// </summary>
public class GameControllerHumanEliminatedTests
{
    private static ControllerHarness EliminationGame(
        Player red, Player blue, QueuedAiPacer pacer, out List<PlayerId> fired)
    {
        var events = new List<PlayerId>();
        AiAction? scriptedKill = new AiMoveAction(
            HexCoord.FromOffset(2, 0), HexCoord.FromOffset(3, 0));
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { red, blue }, cols: 5, rows: 1,
            defaultOwner: PlayerId.None,
            ownerOverrides: new[]
            {
                (0, 0, blue.Id), (1, 0, blue.Id), (2, 0, blue.Id),
                (3, 0, red.Id), (4, 0, red.Id),
            },
            seed: 0,
            aiChooser: (s, c, v, ru, r) => { AiAction? n = scriptedKill; scriptedKill = null; return n; },
            aiPacer: pacer,
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));
        h.Controller.HumanEliminated += id => events.Add(id);
        fired = events;
        return h;
    }

    [Fact]
    public void HumanEliminated_FiresOnceWithTheHumansId_WhenTheirLastCapitalFalls()
    {
        var red = new Player("Red", PlayerId.FromIndex(0));
        var blue = new Player("Blue", PlayerId.FromIndex(1), isAi: true);
        var pacer = new QueuedAiPacer();
        ControllerHarness h = EliminationGame(red, blue, pacer, out List<PlayerId> fired);

        h.Hud.ClickEndTurn();
        pacer.StepOne(); // preview
        Assert.Empty(fired);
        pacer.StepOne(); // execute: capture → Red eliminated

        Assert.Equal(red.Id, h.Session.PendingDefeatScreen);
        Assert.Equal(new[] { red.Id }, fired);

        // Draining the pause and dismissing the overlay never re-fires it.
        pacer.DrainAll();
        h.Hud.ClickDefeatContinue();
        pacer.DrainAll();
        Assert.Single(fired);
    }

    [Fact]
    public void HumanEliminated_DoesNotFire_WhenAnAiIsEliminated()
    {
        // Same board, kinds swapped: the AI (Red) loses its last capital to
        // the human's scripted-equivalent move — a defeat cue, no event.
        var red = new Player("Red", PlayerId.FromIndex(0), isAi: true);
        var blue = new Player("Blue", PlayerId.FromIndex(1));
        var fired = new List<PlayerId>();
        ControllerHarness h = TestHelpers.BuildControllerGame(
            players: new List<Player> { blue, red }, cols: 5, rows: 1,
            defaultOwner: PlayerId.None,
            ownerOverrides: new[]
            {
                (0, 0, blue.Id), (1, 0, blue.Id), (2, 0, blue.Id),
                (3, 0, red.Id), (4, 0, red.Id),
            },
            seed: 0,
            aiChooser: (s, c, v, ru, r) => null,
            aiPacer: new QueuedAiPacer(),
            suppressClaimVictory: false,
            beforeStart: s => s.Grid.Get(HexCoord.FromOffset(2, 0))!.Occupant =
                new Unit(blue.Id, UnitLevel.Soldier));
        h.Controller.HumanEliminated += id => fired.Add(id);

        // Human Blue: select the soldier and capture Red's capital tile.
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(2, 0))!);
        h.Map.SimulateClick(h.State.Grid.Get(HexCoord.FromOffset(3, 0))!);

        Assert.Null(h.Session.PendingDefeatScreen);
        Assert.Empty(fired);
    }
}
