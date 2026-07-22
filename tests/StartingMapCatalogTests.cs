// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class StartingMapCatalogTests
{
    private static SaveSlotInfo User(string name) =>
        new SaveSlotInfo(name, savedAtUnix: 100, turnNumber: 0, isAutosave: false);

    private static SaveSlotInfo Bundled(string name) =>
        new SaveSlotInfo(name, savedAtUnix: 0, turnNumber: 0, isAutosave: false,
            isBundled: true);

    [Fact]
    public void Names_AreNonEmptyAndSanitizeStable()
    {
        Assert.NotEmpty(StartingMapCatalog.Names);
        foreach (string name in StartingMapCatalog.Names)
            Assert.Equal(name, SaveNames.Sanitize(name));
    }

    [Fact]
    public void Names_ContainAtoll6p()
    {
        Assert.Contains("atoll-6p", StartingMapCatalog.Names);
    }

    [Fact]
    public void MergeWithUser_AppendsBundledAfterUserRows()
    {
        var user = new List<SaveSlotInfo> { User("beach"), User("alpha") };
        IReadOnlyList<SaveSlotInfo> merged =
            StartingMapCatalog.MergeWithUser(user, Bundled);

        Assert.Equal("beach", merged[0].SlotName);
        Assert.Equal("alpha", merged[1].SlotName);
        Assert.Equal(
            StartingMapCatalog.Names,
            merged.Skip(2).Select(i => i.SlotName).ToList());
        Assert.All(merged.Skip(2), i => Assert.True(i.IsBundled));
        Assert.All(merged.Take(2), i => Assert.False(i.IsBundled));
    }

    [Fact]
    public void MergeWithUser_UserMapShadowsBundledSameName()
    {
        var user = new List<SaveSlotInfo> { User("atoll-6p") };
        IReadOnlyList<SaveSlotInfo> merged =
            StartingMapCatalog.MergeWithUser(user, Bundled);

        SaveSlotInfo row = Assert.Single(merged, i => i.SlotName == "atoll-6p");
        Assert.False(row.IsBundled);
    }

    [Fact]
    public void MergeWithUser_SkipsUnreadableBundledHeaders()
    {
        IReadOnlyList<SaveSlotInfo> merged = StartingMapCatalog.MergeWithUser(
            new List<SaveSlotInfo>(), _ => null);

        Assert.Empty(merged);
    }

    [Fact]
    public void SaveSlotInfo_DefaultsToNotBundled()
    {
        Assert.False(User("x").IsBundled);
    }
}
