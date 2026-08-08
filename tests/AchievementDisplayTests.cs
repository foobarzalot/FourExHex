// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Masking rules for hidden achievements. No shipped achievement is hidden
/// today, so the panel never takes this branch — the rules live here, in a
/// tested helper, rather than as an untested <c>if</c> in the view.
/// </summary>
public class AchievementDisplayTests
{
    private static AchievementDefinition Def(bool hidden) => new(
        "test.entry", StringKeys.AchieveVeteranTitle, StringKeys.AchieveVeteranDesc,
        AchievementCategory.Victory, Target: 3, Hidden: hidden, Advance: _ => 1);

    [Fact]
    public void HiddenAndLocked_MasksBothKeys()
    {
        AchievementDefinition def = Def(hidden: true);

        Assert.Equal(StringKeys.AchieveHiddenTitle,
            AchievementDisplay.TitleKeyFor(def, unlocked: false));
        Assert.Equal(StringKeys.AchieveHiddenDesc,
            AchievementDisplay.DescriptionKeyFor(def, unlocked: false));
    }

    [Fact]
    public void HiddenAndUnlocked_RevealsRealKeys()
    {
        AchievementDefinition def = Def(hidden: true);

        Assert.Equal(def.TitleKey, AchievementDisplay.TitleKeyFor(def, unlocked: true));
        Assert.Equal(def.DescriptionKey,
            AchievementDisplay.DescriptionKeyFor(def, unlocked: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NotHidden_AlwaysRevealsRealKeys(bool unlocked)
    {
        AchievementDefinition def = Def(hidden: false);

        Assert.Equal(def.TitleKey, AchievementDisplay.TitleKeyFor(def, unlocked));
        Assert.Equal(def.DescriptionKey, AchievementDisplay.DescriptionKeyFor(def, unlocked));
    }
}
