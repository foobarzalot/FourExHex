// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="AchievementPanelLayout.SplitTwoColumns"/> — the
/// greedy whole-group two-column balance behind the achievements panel's
/// landscape layout. Weight per group is Rows.Count + 1 (the header);
/// each group goes to the column that is lighter at its turn, ties left.
/// </summary>
public class AchievementPanelLayoutSplitTests
{
    private static AchievementDefinition Def(string id) => new(
        Id: id,
        TitleKey: "t",
        DescriptionKey: "d",
        Category: AchievementCategory.Victory,
        Target: 1,
        Hidden: false,
        Advance: _ => 0);

    private static AchievementPanelLayout.Group Group(string name, int rowCount) => new(
        AchievementCategory.Victory,
        TitleKey: name,
        Rows: Enumerable.Range(0, rowCount).Select(i => Def($"{name}.{i}")).ToList());

    private static int Weight(AchievementPanelLayout.Group g) => g.Rows.Count + 1;

    [Fact]
    public void EmptyInput_YieldsTwoEmptyColumns()
    {
        var (left, right) = AchievementPanelLayout.SplitTwoColumns(
            Array.Empty<AchievementPanelLayout.Group>());
        Assert.Empty(left);
        Assert.Empty(right);
    }

    [Fact]
    public void SingleGroup_GoesLeft()
    {
        var only = Group("a", 3);
        var (left, right) = AchievementPanelLayout.SplitTwoColumns(new[] { only });
        Assert.Equal(new[] { only }, left);
        Assert.Empty(right);
    }

    [Fact]
    public void EveryGroupAppearsExactlyOnce()
    {
        var groups = new[] { Group("a", 6), Group("b", 6), Group("c", 5), Group("d", 4) };
        var (left, right) = AchievementPanelLayout.SplitTwoColumns(groups);
        List<AchievementPanelLayout.Group> all = left.Concat(right).ToList();
        Assert.Equal(groups.Length, all.Count);
        foreach (AchievementPanelLayout.Group g in groups)
        {
            Assert.Single(all, x => ReferenceEquals(x, g));
        }
    }

    [Fact]
    public void DisplayOrderIsPreservedWithinEachColumn()
    {
        var groups = new[]
        {
            Group("a", 6), Group("b", 6), Group("c", 5), Group("d", 4), Group("e", 2),
        };
        var (left, right) = AchievementPanelLayout.SplitTwoColumns(groups);
        List<AchievementPanelLayout.Group> order = groups.ToList();
        foreach (IReadOnlyList<AchievementPanelLayout.Group> column in new[] { left, right })
        {
            List<int> indices = column.Select(g => order.IndexOf(g)).ToList();
            Assert.Equal(indices.OrderBy(i => i).ToList(), indices);
        }
    }

    [Fact]
    public void EachGroupLandsInTheColumnThatWasLighterAtItsTurn()
    {
        var groups = new[]
        {
            Group("a", 6), Group("b", 6), Group("c", 5), Group("d", 4), Group("e", 1),
        };
        var (left, right) = AchievementPanelLayout.SplitTwoColumns(groups);

        int leftWeight = 0, rightWeight = 0;
        foreach (AchievementPanelLayout.Group g in groups)
        {
            bool inLeft = left.Any(x => ReferenceEquals(x, g));
            if (leftWeight <= rightWeight)
            {
                Assert.True(inLeft, $"group {g.TitleKey} should be left (ties go left)");
                leftWeight += Weight(g);
            }
            else
            {
                Assert.False(inLeft, $"group {g.TitleKey} should be right");
                rightWeight += Weight(g);
            }
        }
    }

    [Fact]
    public void CurrentCatalogGroups_SplitRoughlyEvenly()
    {
        // The real catalog (6/6/5/4 rows today) must not degenerate into a
        // one-sided split; row-count balance within one group's weight.
        var (left, right) = AchievementPanelLayout.SplitTwoColumns(
            AchievementPanelLayout.Groups());
        Assert.NotEmpty(left);
        Assert.NotEmpty(right);
        int leftRows = left.Sum(g => g.Rows.Count);
        int rightRows = right.Sum(g => g.Rows.Count);
        Assert.True(Math.Abs(leftRows - rightRows) <= 7,
            $"unbalanced split: left={leftRows} right={rightRows}");
    }
}
