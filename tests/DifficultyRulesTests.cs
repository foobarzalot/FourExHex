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
    // Captain/Commander income is flat 100% — cheaper upkeep (the
    // UnitUpkeep table) is the sole hard-level lever.
    [InlineData(Difficulty.Captain, 5, 5)]
    [InlineData(Difficulty.Captain, 20, 20)]
    [InlineData(Difficulty.Commander, 8, 8)]
    [InlineData(Difficulty.Commander, 20, 20)]
    public void ScaleIncome_AppliesPerDifficultyTransform(Difficulty d, int baseIncome, int expected)
    {
        Assert.Equal(expected, DifficultyRules.ScaleIncome(baseIncome, d));
    }

    // The hand-picked upkeep table (#11): rows = unit level (Soldier-column
    // baseline 2/6/18/54 per Slay), columns = owner difficulty. Captain
    // difficulty pays ~3/4, Commander ~1/2. Pins all 16 entries.
    [Theory]
    [InlineData(UnitLevel.Recruit, Difficulty.Recruit, 2)]
    [InlineData(UnitLevel.Recruit, Difficulty.Soldier, 2)]
    [InlineData(UnitLevel.Recruit, Difficulty.Captain, 1)]
    [InlineData(UnitLevel.Recruit, Difficulty.Commander, 1)]
    [InlineData(UnitLevel.Soldier, Difficulty.Recruit, 6)]
    [InlineData(UnitLevel.Soldier, Difficulty.Soldier, 6)]
    [InlineData(UnitLevel.Soldier, Difficulty.Captain, 4)]
    [InlineData(UnitLevel.Soldier, Difficulty.Commander, 3)]
    [InlineData(UnitLevel.Captain, Difficulty.Recruit, 18)]
    [InlineData(UnitLevel.Captain, Difficulty.Soldier, 18)]
    [InlineData(UnitLevel.Captain, Difficulty.Captain, 13)]
    [InlineData(UnitLevel.Captain, Difficulty.Commander, 9)]
    [InlineData(UnitLevel.Commander, Difficulty.Recruit, 54)]
    [InlineData(UnitLevel.Commander, Difficulty.Soldier, 54)]
    [InlineData(UnitLevel.Commander, Difficulty.Captain, 40)]
    [InlineData(UnitLevel.Commander, Difficulty.Commander, 27)]
    public void UnitUpkeep_TablePinsAllEntries(UnitLevel level, Difficulty d, int expected)
    {
        Assert.Equal(expected, DifficultyRules.UnitUpkeep(level, d));
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
