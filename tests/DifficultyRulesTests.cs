using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class DifficultyRulesTests
{
    [Theory]
    [InlineData(Difficulty.Recruit, 3, 1)]    // 50%: 3*50/100 = 1 (truncation)
    [InlineData(Difficulty.Recruit, 5, 2)]    // 50%: 5*50/100 = 2 (truncation, not rounding)
    [InlineData(Difficulty.Recruit, 4, 2)]
    [InlineData(Difficulty.Recruit, 0, 0)]
    [InlineData(Difficulty.Soldier, 3, 3)]
    [InlineData(Difficulty.Captain, 3, 3)]    // 120%: 3*120/100 = 3 (truncated, no bonus)
    [InlineData(Difficulty.Captain, 5, 6)]    // 120%: 5*120/100 = 6
    [InlineData(Difficulty.Captain, 10, 12)]
    [InlineData(Difficulty.Captain, 20, 24)]
    [InlineData(Difficulty.Commander, 3, 4)]  // 140%: 3*140/100 = 4 (truncated from 4.2)
    [InlineData(Difficulty.Commander, 8, 11)] // 140%: 8*140/100 = 11 (truncated from 11.2)
    [InlineData(Difficulty.Commander, 20, 28)]
    public void ScaleIncome_AppliesPerDifficultyTransform(Difficulty d, int baseIncome, int expected)
    {
        Assert.Equal(expected, DifficultyRules.ScaleIncome(baseIncome, d));
    }

    [Fact]
    public void AssignGlobalToAi_GivesAiSlotsGlobalAndHumansSoldier()
    {
        var kinds = new[]
        {
            PlayerKind.Human,
            PlayerKind.Computer,
            PlayerKind.Computer,
            PlayerKind.Human,
        };

        Difficulty[] result = DifficultyRules.AssignGlobalToAi(kinds, Difficulty.Commander);

        Assert.Equal(
            new[] { Difficulty.Soldier, Difficulty.Commander, Difficulty.Commander, Difficulty.Soldier },
            result);
    }
}
