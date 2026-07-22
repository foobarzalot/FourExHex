// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class SaveNamesTests
{
    [Theory]
    [InlineData("canyon", "canyon")]
    [InlineData("my map!", "my_map_")]
    [InlineData("  padded  ", "padded")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("UPPER-lower_09", "UPPER-lower_09")]
    public void Sanitize_ReplacesUnsafeCharsAndTrims(string raw, string expected)
    {
        Assert.Equal(expected, SaveNames.Sanitize(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Sanitize_EmptyOrWhitespaceFallsBackToSave(string? raw)
    {
        Assert.Equal("save", SaveNames.Sanitize(raw!));
    }

    [Fact]
    public void Sanitize_TruncatesTo64Chars()
    {
        string raw = new string('x', 100);
        Assert.Equal(new string('x', 64), SaveNames.Sanitize(raw));
    }
}
