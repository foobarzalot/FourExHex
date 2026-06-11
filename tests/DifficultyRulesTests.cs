using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class DifficultyRulesTests
{
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
