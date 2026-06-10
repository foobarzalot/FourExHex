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
}
