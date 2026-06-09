using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class DifficultyRulesTests
{
    [Theory]
    [InlineData(Difficulty.Easy, 3, 1)]   // truncating divide: 3/2 = 1
    [InlineData(Difficulty.Easy, 5, 2)]   // 5/2 = 2 (truncation, not rounding)
    [InlineData(Difficulty.Easy, 4, 2)]
    [InlineData(Difficulty.Easy, 0, 0)]
    [InlineData(Difficulty.Normal, 3, 3)]
    [InlineData(Difficulty.Hard, 4, 6)]   // 1.5×: 4*3/2 = 6
    [InlineData(Difficulty.Hard, 3, 4)]   // 3*3/2 = 4 (truncation)
    [InlineData(Difficulty.Hard, 5, 7)]   // 5*3/2 = 7 (truncation)
    [InlineData(Difficulty.Brutal, 3, 6)] // 2×
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
