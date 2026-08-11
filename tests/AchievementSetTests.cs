// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Predicate specs for the initial achievement set (issue #230): what each
/// catalog row's <c>Advance</c> reads from the game-end facts. Synthetic
/// events only — the controller-side facts assembly is covered by
/// <see cref="AchievementAwardTests"/>.
/// </summary>
public class AchievementSetTests
{
    private static int Advance(string id, AchievementEvent evt)
        => AchievementCatalog.ById(id)!.Advance(evt);

    private static GameEndEvent Win() => AchievementTestEvents.HumanWin();
    private static GameEndEvent Loss() => AchievementTestEvents.HumanLoss();

    // --- Victory ------------------------------------------------------------

    [Fact]
    public void FirstWin_AdvancesOnAnyHumanWin_NotOnALoss()
    {
        Assert.Equal(1, Advance(AchievementCatalog.FirstWin, Win()));
        Assert.Equal(0, Advance(AchievementCatalog.FirstWin, Loss()));
        Assert.Equal(1, AchievementCatalog.ById(AchievementCatalog.FirstWin)!.Target);
    }

    [Fact]
    public void WarHero_IsACounterOfTwentyFiveWins()
    {
        AchievementDefinition def = AchievementCatalog.ById(AchievementCatalog.WarHero)!;
        Assert.Equal(25, def.Target);
        Assert.Equal(1, def.Advance(Win()));
        Assert.Equal(0, def.Advance(Loss()));
    }

    [Fact]
    public void CaptainCommission_RequiresCaptainDifficultyOrAbove()
    {
        Assert.Equal(0, Advance(AchievementCatalog.CaptainCommission,
            Win() with { WinnerDifficulty = Difficulty.Soldier }));
        Assert.Equal(1, Advance(AchievementCatalog.CaptainCommission,
            Win() with { WinnerDifficulty = Difficulty.Captain }));
        Assert.Equal(1, Advance(AchievementCatalog.CaptainCommission,
            Win() with { WinnerDifficulty = Difficulty.Commander }));
        Assert.Equal(0, Advance(AchievementCatalog.CaptainCommission,
            Loss() with { WinnerDifficulty = Difficulty.Commander }));
    }

    [Fact]
    public void CommanderCommission_RequiresCommanderDifficulty()
    {
        Assert.Equal(0, Advance(AchievementCatalog.CommanderCommission,
            Win() with { WinnerDifficulty = Difficulty.Captain }));
        Assert.Equal(1, Advance(AchievementCatalog.CommanderCommission,
            Win() with { WinnerDifficulty = Difficulty.Commander }));
    }

    [Fact]
    public void TotalDomination_RequiresAnOutrightWin()
    {
        Assert.Equal(1, Advance(AchievementCatalog.TotalDomination, Win()));
        Assert.Equal(0, Advance(AchievementCatalog.TotalDomination,
            Win() with { WonByClaim = true }));
        Assert.Equal(0, Advance(AchievementCatalog.TotalDomination, Loss()));
    }

    // --- Modes --------------------------------------------------------------

    [Theory]
    [InlineData(GameMode.RisingTides)]
    [InlineData(GameMode.FogOfWar)]
    [InlineData(GameMode.VikingRaiders)]
    public void ModeWins_RequireTheirModeAndAHumanWin(GameMode mode)
    {
        string id = mode switch
        {
            GameMode.RisingTides => AchievementCatalog.DryFeet,
            GameMode.FogOfWar => AchievementCatalog.ThroughTheMist,
            _ => AchievementCatalog.RaidersRepelled,
        };
        Assert.Equal(1, Advance(id, Win() with { Mode = mode }));
        Assert.Equal(0, Advance(id, Win())); // Freeform
        Assert.Equal(0, Advance(id, Loss() with { Mode = mode }));
    }

    [Fact]
    public void LastHill_RequiresRisingTidesAndTwentyOrFewerTiles()
    {
        Assert.Equal(1, Advance(AchievementCatalog.LastHill,
            Win() with { Mode = GameMode.RisingTides, LandTilesRemaining = 20 }));
        Assert.Equal(0, Advance(AchievementCatalog.LastHill,
            Win() with { Mode = GameMode.RisingTides, LandTilesRemaining = 21 }));
        Assert.Equal(0, Advance(AchievementCatalog.LastHill,
            Win() with { LandTilesRemaining = 20 })); // Freeform
    }

    [Fact]
    public void VikingSlayer_AdvancesByThePerGameKillCount_EvenOnALoss()
    {
        AchievementDefinition def = AchievementCatalog.ById(AchievementCatalog.VikingSlayer)!;
        Assert.Equal(50, def.Target);
        Assert.Equal(7, def.Advance(Win() with { VikingKills = 7 }));
        Assert.Equal(3, def.Advance(Loss() with { VikingKills = 3 }));
        Assert.Equal(0, def.Advance(Win()));
    }

    // --- Skill --------------------------------------------------------------

    [Fact]
    public void Untouchable_RequiresAWinWithNoUnitsLost()
    {
        Assert.Equal(1, Advance(AchievementCatalog.Untouchable, Win()));
        Assert.Equal(0, Advance(AchievementCatalog.Untouchable,
            Win() with { WinnerUnitsLost = 1 }));
        Assert.Equal(0, Advance(AchievementCatalog.Untouchable, Loss()));
    }

    [Fact]
    public void OpenField_RequiresAWinWithNoTowersBuilt()
    {
        Assert.Equal(1, Advance(AchievementCatalog.OpenField, Win()));
        Assert.Equal(0, Advance(AchievementCatalog.OpenField,
            Win() with { WinnerTowersBuilt = 1 }));
        Assert.Equal(0, Advance(AchievementCatalog.OpenField, Loss()));
    }

    [Fact]
    public void Blitz_RequiresWinningByTurnTwenty()
    {
        Assert.Equal(1, Advance(AchievementCatalog.Blitz,
            Win() with { TurnNumber = 20 }));
        Assert.Equal(0, Advance(AchievementCatalog.Blitz,
            Win() with { TurnNumber = 21 }));
        Assert.Equal(0, Advance(AchievementCatalog.Blitz,
            Loss() with { TurnNumber = 5 }));
    }

    [Fact]
    public void ChainOfCommand_RequiresFieldingACommander_WinOrLose()
    {
        Assert.Equal(1, Advance(AchievementCatalog.ChainOfCommand,
            Loss() with { MaxHumanUnitLevel = (int)UnitLevel.Commander }));
        Assert.Equal(1, Advance(AchievementCatalog.ChainOfCommand,
            Win() with { MaxHumanUnitLevel = (int)UnitLevel.Commander }));
        Assert.Equal(0, Advance(AchievementCatalog.ChainOfCommand,
            Win() with { MaxHumanUnitLevel = (int)UnitLevel.Captain }));
    }

    // --- Campaign -----------------------------------------------------------

    private static CampaignLevelWonEvent LevelWon(
        int level = 0, int wonCount = 1, int tierIndex = 0, int tierWonCount = 1)
        => new(level, wonCount, tierIndex, tierWonCount);

    [Fact]
    public void CampaignFirst_AdvancesOnAnyNewlyWonLevel()
    {
        Assert.Equal(1, Advance(AchievementCatalog.CampaignFirst, LevelWon()));
        Assert.Equal(0, Advance(AchievementCatalog.CampaignFirst, Win()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TierCleared_RequiresAllSixtyFourLevelsOfItsTier(int tier)
    {
        string id = tier switch
        {
            0 => AchievementCatalog.TierRecruit,
            1 => AchievementCatalog.TierSoldier,
            2 => AchievementCatalog.TierCaptain,
            _ => AchievementCatalog.TierCommander,
        };
        Assert.Equal(1, Advance(id,
            LevelWon(tierIndex: tier, tierWonCount: CampaignProgress.TierSize)));
        Assert.Equal(0, Advance(id,
            LevelWon(tierIndex: tier, tierWonCount: CampaignProgress.TierSize - 1)));
        Assert.Equal(0, Advance(id,
            LevelWon(tierIndex: (tier + 1) % 4, tierWonCount: CampaignProgress.TierSize)));
    }

    [Fact]
    public void CampaignComplete_RequiresAllTwoHundredFiftySixLevels()
    {
        Assert.Equal(1, Advance(AchievementCatalog.CampaignComplete,
            LevelWon(wonCount: CampaignProgress.LevelCount)));
        Assert.Equal(0, Advance(AchievementCatalog.CampaignComplete,
            LevelWon(wonCount: CampaignProgress.LevelCount - 1)));
    }

    // --- Set shape ----------------------------------------------------------

    [Fact]
    public void TheInitialSet_IsTwentyOneRowsAcrossFourCategories()
    {
        Assert.Equal(21, AchievementCatalog.All.Count);
        Assert.Equal(6, AchievementCatalog.All.Count(d => d.Category == AchievementCategory.Victory));
        Assert.Equal(6, AchievementCatalog.All.Count(d => d.Category == AchievementCategory.Campaign));
        Assert.Equal(5, AchievementCatalog.All.Count(d => d.Category == AchievementCategory.Modes));
        Assert.Equal(4, AchievementCatalog.All.Count(d => d.Category == AchievementCategory.Skill));
    }

    [Fact]
    public void NothingInTheInitialSet_IsHidden()
    {
        Assert.All(AchievementCatalog.All, d => Assert.False(d.Hidden));
    }
}
