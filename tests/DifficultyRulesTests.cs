using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class DifficultyRulesTests
{
    // Earn rate is flat 1× at every level for now — difficulty is the
    // human player's upkeep handicap (the UnitUpkeep table). The lever
    // stays plumbed so income can rejoin the mix later.
    [Theory]
    [InlineData(Difficulty.Recruit, 3, 3)]
    [InlineData(Difficulty.Recruit, 5, 5)]
    [InlineData(Difficulty.Recruit, 0, 0)]
    [InlineData(Difficulty.Soldier, 3, 3)]
    [InlineData(Difficulty.Captain, 20, 20)]
    [InlineData(Difficulty.Commander, 20, 20)]
    public void ScaleIncome_IsFlatAtEveryLevel(Difficulty d, int baseIncome, int expected)
    {
        Assert.Equal(expected, DifficultyRules.ScaleIncome(baseIncome, d));
    }

    // The hand-picked upkeep table (#11): rows = unit level, columns = the
    // owner's difficulty. Difficulty is the HUMAN player's handicap — AI
    // opponents always play the Soldier column (the 2/6/18/54 Slay
    // baseline). Recruit = cheaper-than-AI easy mode; Captain ≈ ×1.25
    // (hand-rounded); Commander = ×1.5 exact. Pins all 16 entries.
    [Theory]
    [InlineData(UnitLevel.Recruit, Difficulty.Recruit, 1)]
    [InlineData(UnitLevel.Recruit, Difficulty.Soldier, 2)]
    [InlineData(UnitLevel.Recruit, Difficulty.Captain, 3)]
    [InlineData(UnitLevel.Recruit, Difficulty.Commander, 3)]
    [InlineData(UnitLevel.Soldier, Difficulty.Recruit, 4)]
    [InlineData(UnitLevel.Soldier, Difficulty.Soldier, 6)]
    [InlineData(UnitLevel.Soldier, Difficulty.Captain, 8)]
    [InlineData(UnitLevel.Soldier, Difficulty.Commander, 9)]
    [InlineData(UnitLevel.Captain, Difficulty.Recruit, 13)]
    [InlineData(UnitLevel.Captain, Difficulty.Soldier, 18)]
    [InlineData(UnitLevel.Captain, Difficulty.Captain, 23)]
    [InlineData(UnitLevel.Captain, Difficulty.Commander, 27)]
    [InlineData(UnitLevel.Commander, Difficulty.Recruit, 40)]
    [InlineData(UnitLevel.Commander, Difficulty.Soldier, 54)]
    [InlineData(UnitLevel.Commander, Difficulty.Captain, 68)]
    [InlineData(UnitLevel.Commander, Difficulty.Commander, 81)]
    public void UnitUpkeep_TablePinsAllEntries(UnitLevel level, Difficulty d, int expected)
    {
        Assert.Equal(expected, DifficultyRules.UnitUpkeep(level, d));
    }

    [Fact]
    public void AssignGlobalToHumans_GivesHumanSlotsGlobalAndAiSoldier()
    {
        // Difficulty is the human's self-imposed handicap: the chosen level
        // lands on Human slots; Computer opponents stay at Soldier (normal).
        var kinds = new[]
        {
            PlayerKind.Human,
            PlayerKind.Computer,
            PlayerKind.Computer,
            PlayerKind.Human,
        };

        Difficulty[] result = DifficultyRules.AssignGlobalToHumans(kinds, Difficulty.Commander);

        Assert.Equal(
            new[] { Difficulty.Commander, Difficulty.Soldier, Difficulty.Soldier, Difficulty.Commander },
            result);
    }
}
