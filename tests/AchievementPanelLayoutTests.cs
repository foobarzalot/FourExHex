// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// The achievements panel's category grouping: catalog rows partitioned
/// into titled sections, categories in enum (display) order, rows in
/// catalog order within each.
/// </summary>
public class AchievementPanelLayoutTests
{
    [Fact]
    public void Groups_CoverEveryCatalogRowExactlyOnce()
    {
        List<AchievementDefinition> flattened = AchievementPanelLayout.Groups()
            .SelectMany(g => g.Rows)
            .ToList();

        Assert.Equal(AchievementCatalog.All.Count, flattened.Count);
        Assert.Equal(
            AchievementCatalog.All.Select(d => d.Id).OrderBy(id => id),
            flattened.Select(d => d.Id).OrderBy(id => id));
    }

    [Fact]
    public void Groups_AreInCategoryDisplayOrder()
    {
        var categories = AchievementPanelLayout.Groups()
            .Select(g => g.Category)
            .ToList();

        Assert.Equal(categories.OrderBy(c => (int)c), categories);
        Assert.Equal(categories.Distinct().Count(), categories.Count);
    }

    [Fact]
    public void RowsWithinAGroup_KeepCatalogOrder()
    {
        foreach (var group in AchievementPanelLayout.Groups())
        {
            List<int> catalogIndices = group.Rows
                .Select(r => AchievementCatalog.All.ToList().FindIndex(d => d.Id == r.Id))
                .ToList();
            Assert.Equal(catalogIndices.OrderBy(i => i), catalogIndices);
        }
    }

    [Fact]
    public void EveryGroupTitleKey_ResolvesToRealEnglish()
    {
        foreach (var group in AchievementPanelLayout.Groups())
        {
            Assert.NotEqual(group.TitleKey, Strings.Get(group.TitleKey));
        }
    }
}
