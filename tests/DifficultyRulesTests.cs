using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class DifficultyRulesTests
{
    [Theory]
    [InlineData(Difficulty.Easy, 3, 1)]    // 50%: 3*50/100 = 1 (truncation)
    [InlineData(Difficulty.Easy, 5, 2)]    // 50%: 5*50/100 = 2 (truncation, not rounding)
    [InlineData(Difficulty.Easy, 4, 2)]
    [InlineData(Difficulty.Easy, 0, 0)]
    [InlineData(Difficulty.Normal, 3, 3)]
    [InlineData(Difficulty.Hard, 3, 3)]    // 120%: 3*120/100 = 3 (truncated, no bonus)
    [InlineData(Difficulty.Hard, 5, 6)]    // 120%: 5*120/100 = 6
    [InlineData(Difficulty.Hard, 10, 12)]
    [InlineData(Difficulty.Hard, 20, 24)]
    [InlineData(Difficulty.Brutal, 3, 4)]  // 140%: 3*140/100 = 4 (truncated from 4.2)
    [InlineData(Difficulty.Brutal, 8, 11)] // 140%: 8*140/100 = 11 (truncated from 11.2)
    [InlineData(Difficulty.Brutal, 20, 28)]
    public void ScaleIncome_AppliesPerDifficultyTransform(Difficulty d, int baseIncome, int expected)
    {
        Assert.Equal(expected, DifficultyRules.ScaleIncome(baseIncome, d));
    }

    [Fact]
    public void AssignGlobalToAi_GivesAiSlotsGlobalAndHumansNormal()
    {
        var kinds = new[]
        {
            PlayerKind.Human,
            PlayerKind.Computer,
            PlayerKind.Computer,
            PlayerKind.Human,
        };

        Difficulty[] result = DifficultyRules.AssignGlobalToAi(kinds, Difficulty.Brutal);

        Assert.Equal(
            new[] { Difficulty.Normal, Difficulty.Brutal, Difficulty.Brutal, Difficulty.Normal },
            result);
    }
}
