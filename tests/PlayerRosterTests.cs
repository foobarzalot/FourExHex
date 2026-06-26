using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class PlayerRosterTests
{
    [Fact]
    public void BuildRoster_ReadsDifficultiesOntoSlots()
    {
        Difficulty[] saved = GameSettings.Difficulties;
        try
        {
            GameSettings.Difficulties = new[]
            {
                Difficulty.Soldier, Difficulty.Soldier, Difficulty.Soldier,
                Difficulty.Soldier, Difficulty.Soldier, Difficulty.Commander,
            };

            List<Player> roster = Player.BuildRoster();

            Assert.Equal(Difficulty.Soldier, roster[0].Difficulty);
            Assert.Equal(Difficulty.Commander, roster[5].Difficulty);
        }
        finally
        {
            GameSettings.Difficulties = saved;
        }
    }

    [Fact]
    public void BuildRoster_ShortDifficultyArray_FallsBackToSoldier()
    {
        Difficulty[] saved = GameSettings.Difficulties;
        try
        {
            GameSettings.Difficulties = Array.Empty<Difficulty>();

            List<Player> roster = Player.BuildRoster();

            foreach (Player p in roster)
            {
                Assert.Equal(Difficulty.Soldier, p.Difficulty);
            }
        }
        finally
        {
            GameSettings.Difficulties = saved;
        }
    }

    [Fact]
    public void BuildRoster_AllNonNone_ReturnsAllSixInSlotOrder()
    {
        // Regression: the default 6-player setup is unchanged — every slot
        // present, in order, each player's Id matching its slot index.
        PlayerKind[] saved = GameSettings.PlayerKinds;
        try
        {
            GameSettings.PlayerKinds = new[]
            {
                PlayerKind.Human, PlayerKind.Computer, PlayerKind.Computer,
                PlayerKind.Computer, PlayerKind.Computer, PlayerKind.Computer,
            };

            List<Player> roster = Player.BuildRoster();

            Assert.Equal(6, roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                Assert.Equal(i, roster[i].Id.Index);
            }
        }
        finally
        {
            GameSettings.PlayerKinds = saved;
        }
    }

    [Fact]
    public void BuildCampaignRoster_IsDerivedFromLevel_NotFreeformPlayerKinds()
    {
        // The campaign roster must come from the level alone — its size, color
        // slots, and human are the level's deterministic per-level roster, and
        // playing a level never bleeds into the freeform New Game default
        // (#70). Poison the freeform kinds to prove independence.
        PlayerKind[] savedKinds = GameSettings.PlayerKinds;
        try
        {
            GameSettings.PlayerKinds = new[]
            {
                PlayerKind.None, PlayerKind.None, PlayerKind.None,
                PlayerKind.None, PlayerKind.None, PlayerKind.Human,
            };
            int level = 0x43;
            int[] activeSlots = CampaignProgress.ActiveColorSlotsForLevel(level);
            int humanSlot = CampaignProgress.HumanColorSlotForLevel(level);

            List<Player> roster = Player.BuildCampaignRoster(level);

            // A compact 2–6 roster over exactly the level's active color slots.
            Assert.Equal(activeSlots, roster.ConvertAll(p => p.Id.Index).ToArray());
            Assert.InRange(roster.Count, 2, 6);
            Assert.Single(roster, p => p.Kind == PlayerKind.Human);
            foreach (Player p in roster)
            {
                bool isHuman = p.Id.Index == humanSlot;
                Assert.Equal(isHuman ? PlayerKind.Human : PlayerKind.Computer, p.Kind);
                Assert.Equal(
                    isHuman ? CampaignProgress.DifficultyForLevel(level) : Difficulty.Soldier,
                    p.Difficulty);
            }
        }
        finally
        {
            GameSettings.PlayerKinds = savedKinds;
        }
    }

    [Fact]
    public void BuildRoster_ExcludesNoneSlots_AndPreservesSlotIndices()
    {
        // None slots are dropped entirely; survivors keep their original
        // slot index (so colors stay correct) and the list compacts.
        PlayerKind[] savedKinds = GameSettings.PlayerKinds;
        Difficulty[] savedDiff = GameSettings.Difficulties;
        try
        {
            GameSettings.PlayerKinds = new[]
            {
                PlayerKind.Human, PlayerKind.None, PlayerKind.Computer,
                PlayerKind.None, PlayerKind.Computer, PlayerKind.None,
            };
            GameSettings.Difficulties = new[]
            {
                Difficulty.Captain, Difficulty.Soldier, Difficulty.Soldier,
                Difficulty.Soldier, Difficulty.Commander, Difficulty.Soldier,
            };

            List<Player> roster = Player.BuildRoster();

            Assert.Equal(3, roster.Count);
            Assert.Equal(new[] { 0, 2, 4 }, roster.ConvertAll(p => p.Id.Index));
            Assert.Equal(PlayerKind.Human, roster[0].Kind);
            Assert.Equal(PlayerKind.Computer, roster[1].Kind);
            Assert.Equal(Difficulty.Captain, roster[0].Difficulty);   // slot 0
            Assert.Equal(Difficulty.Commander, roster[2].Difficulty); // slot 4
            Assert.DoesNotContain(roster, p => p.Kind == PlayerKind.None);
        }
        finally
        {
            GameSettings.PlayerKinds = savedKinds;
            GameSettings.Difficulties = savedDiff;
        }
    }
}
