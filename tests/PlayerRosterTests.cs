using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class PlayerRosterTests
{
    [Fact]
    public void BuildRoster_ReadsEarnMultipliersOntoSlots()
    {
        int[] saved = GameSettings.EarnMultipliers;
        try
        {
            GameSettings.EarnMultipliers = new[] { 1, 1, 1, 1, 1, 3 };

            List<Player> roster = Player.BuildRoster();

            Assert.Equal(1, roster[0].EarnMultiplier);
            Assert.Equal(3, roster[5].EarnMultiplier);
        }
        finally
        {
            GameSettings.EarnMultipliers = saved;
        }
    }

    [Fact]
    public void BuildRoster_ShortMultiplierArray_FallsBackToOne()
    {
        int[] saved = GameSettings.EarnMultipliers;
        try
        {
            GameSettings.EarnMultipliers = Array.Empty<int>();

            List<Player> roster = Player.BuildRoster();

            foreach (Player p in roster)
            {
                Assert.Equal(1, p.EarnMultiplier);
            }
        }
        finally
        {
            GameSettings.EarnMultipliers = saved;
        }
    }
}
