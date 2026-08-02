// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FourExHex
using System.Collections.Generic;
using FourExHex.Controller;
using Xunit;

public class ShareReceiveRulesTests
{
    [Fact]
    public void FxhmapPaths_MixedList_KeepsOnlyFxhmapInOrder()
    {
        IReadOnlyList<string> result = ShareReceiveRules.FxhmapPaths(new[]
        {
            "/cache/share_received/photo.png",
            "/cache/share_received/alpha.fxhmap",
            "/cache/share_received/notes.txt",
            "/cache/share_received/beta.fxhmap",
        });

        Assert.Equal(new[]
        {
            "/cache/share_received/alpha.fxhmap",
            "/cache/share_received/beta.fxhmap",
        }, result);
    }

    [Theory]
    [InlineData("/tmp/UPPER.FXHMAP")]
    [InlineData("/tmp/mixed.FxHmAp")]
    public void FxhmapPaths_ExtensionMatchIsCaseInsensitive(string path)
    {
        IReadOnlyList<string> result =
            ShareReceiveRules.FxhmapPaths(new[] { path });

        Assert.Equal(new[] { path }, result);
    }

    [Fact]
    public void FxhmapPaths_ExactDuplicatesCollapse()
    {
        IReadOnlyList<string> result = ShareReceiveRules.FxhmapPaths(new[]
        {
            "/tmp/a.fxhmap",
            "/tmp/a.fxhmap",
            "/tmp/b.fxhmap",
            "/tmp/a.fxhmap",
        });

        Assert.Equal(new[] { "/tmp/a.fxhmap", "/tmp/b.fxhmap" }, result);
    }

    [Fact]
    public void FxhmapPaths_NullAndBlankEntriesDropped()
    {
        IReadOnlyList<string> result = ShareReceiveRules.FxhmapPaths(new[]
        {
            null,
            "",
            "   ",
            "/tmp/ok.fxhmap",
        });

        Assert.Equal(new[] { "/tmp/ok.fxhmap" }, result);
    }

    [Fact]
    public void FxhmapPaths_EmptyInput_EmptyResult()
    {
        Assert.Empty(ShareReceiveRules.FxhmapPaths(new string?[0]));
    }

    [Fact]
    public void FxhmapPaths_ExtensionMustBeSuffixNotSubstring()
    {
        IReadOnlyList<string> result = ShareReceiveRules.FxhmapPaths(new[]
        {
            "/tmp/trap.fxhmap.png",
            "/tmp/real.fxhmap",
        });

        Assert.Equal(new[] { "/tmp/real.fxhmap" }, result);
    }
}
