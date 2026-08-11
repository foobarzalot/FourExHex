// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Structural invariants for <see cref="AchievementCatalog"/>. These are
/// what make "adding an achievement is one table row plus a predicate"
/// true rather than hoped for — every rule below holds for the whole
/// table, so a new entry that breaks one fails the build.
/// </summary>
public class AchievementCatalogTests
{
    /// <summary>Namespaced, lowercase, dot-separated — the shape a platform
    /// console will eventually register.</summary>
    private static readonly Regex IdPattern =
        new(@"^[a-z][a-z0-9_]*\.[a-z][a-z0-9_]*$");

    [Fact]
    public void Catalog_IsNotEmpty()
    {
        Assert.NotEmpty(AchievementCatalog.All);
    }

    [Fact]
    public void AllIds_AreUnique()
    {
        var seen = new HashSet<string>();
        foreach (AchievementDefinition def in AchievementCatalog.All)
        {
            Assert.True(seen.Add(def.Id), $"Duplicate achievement id '{def.Id}'.");
        }
    }

    [Fact]
    public void AllIds_MatchNamespacedPattern()
    {
        foreach (AchievementDefinition def in AchievementCatalog.All)
        {
            Assert.True(IdPattern.IsMatch(def.Id),
                $"Id '{def.Id}' is not a lowercase namespaced id (e.g. 'victory.veteran').");
        }
    }

    [Fact]
    public void AllTargets_AreAtLeastOne()
    {
        foreach (AchievementDefinition def in AchievementCatalog.All)
        {
            Assert.True(def.Target >= 1, $"Id '{def.Id}' has a target below 1.");
        }
    }

    [Fact]
    public void AllDisplayKeys_ResolveToRealEnglish()
    {
        foreach (AchievementDefinition def in AchievementCatalog.All)
        {
            Assert.NotEqual(def.TitleKey, Strings.Get(def.TitleKey));
            Assert.NotEqual(def.DescriptionKey, Strings.Get(def.DescriptionKey));
        }
    }

    [Fact]
    public void ById_KnownId_ReturnsDefinition_UnknownId_ReturnsNull()
    {
        Assert.NotNull(AchievementCatalog.ById(AchievementCatalog.Veteran));
        Assert.Null(AchievementCatalog.ById("no.such_achievement"));
    }

    // --- The shipped proof-of-concept achievement ---

    [Fact]
    public void Veteran_IsACounterOfThree()
    {
        AchievementDefinition def = AchievementCatalog.ById(AchievementCatalog.Veteran)!;

        Assert.Equal(3, def.Target);
        Assert.True(def.IsCounter);
        Assert.False(def.Hidden);
    }

    [Fact]
    public void Veteran_AdvancesByOnePerHumanWin()
    {
        AchievementDefinition def = AchievementCatalog.ById(AchievementCatalog.Veteran)!;

        Assert.Equal(1, def.Advance(AchievementTestEvents.HumanWin()));
    }

    [Fact]
    public void Veteran_DoesNotAdvance_WhenTheGameEndedWithoutAHumanWin()
    {
        AchievementDefinition def = AchievementCatalog.ById(AchievementCatalog.Veteran)!;

        Assert.Equal(0, def.Advance(AchievementTestEvents.HumanLoss()));
    }

    [Fact]
    public void SingleTargetDefinition_IsNotACounter()
    {
        // Target derives the kind: 1 is a boolean achievement, >1 a counter.
        var boolean = new AchievementDefinition(
            "test.boolean", StringKeys.AchieveVeteranTitle, StringKeys.AchieveVeteranDesc,
            AchievementCategory.Victory, Target: 1, Hidden: false, Advance: _ => 1);

        Assert.False(boolean.IsCounter);
    }
}
