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
                Difficulty.Normal, Difficulty.Normal, Difficulty.Normal,
                Difficulty.Normal, Difficulty.Normal, Difficulty.Brutal,
            };

            List<Player> roster = Player.BuildRoster();

            Assert.Equal(Difficulty.Normal, roster[0].Difficulty);
            Assert.Equal(Difficulty.Brutal, roster[5].Difficulty);
        }
        finally
        {
            GameSettings.Difficulties = saved;
        }
    }

    [Fact]
    public void BuildRoster_ShortDifficultyArray_FallsBackToNormal()
    {
        Difficulty[] saved = GameSettings.Difficulties;
        try
        {
            GameSettings.Difficulties = Array.Empty<Difficulty>();

            List<Player> roster = Player.BuildRoster();

            foreach (Player p in roster)
            {
                Assert.Equal(Difficulty.Normal, p.Difficulty);
            }
        }
        finally
        {
            GameSettings.Difficulties = saved;
        }
    }
}
