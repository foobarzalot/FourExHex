// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Copy rules for the unlock banner. Also pins the shipped English wording
/// for <c>achieve.unlocked_banner</c>.
/// </summary>
public class AchievementBannerContentTests
{
    [Fact]
    public void For_KnownId_RendersTheAchievementTitle()
    {
        string? text = AchievementBannerContent.For(AchievementCatalog.Veteran);

        Assert.NotNull(text);
        Assert.Contains(Strings.Get(StringKeys.AchieveVeteranTitle), text!);
    }

    [Fact]
    public void For_UnknownId_ReturnsNull()
    {
        // A rename miss, or an id from a newer record, must never crash
        // mid-game — the banner simply does not show.
        Assert.Null(AchievementBannerContent.For("no.such_achievement"));
    }

    [Fact]
    public void For_HiddenAchievement_StillNamesItOnceEarned()
    {
        // Unlocking is exactly when a hidden achievement stops being
        // masked, so the banner shows its real title rather than "???".
        var hidden = new AchievementDefinition(
            "test.hidden", StringKeys.AchieveVeteranTitle, StringKeys.AchieveVeteranDesc,
            AchievementCategory.Victory, Target: 1, Hidden: true, Advance: _ => 1);

        string text = AchievementBannerContent.ForDefinition(hidden);

        Assert.Contains(Strings.Get(StringKeys.AchieveVeteranTitle), text);
        Assert.DoesNotContain(Strings.Get(StringKeys.AchieveHiddenTitle), text);
    }
}
