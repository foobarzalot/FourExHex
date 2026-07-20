// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class DemoCatalogTests
{
    [Fact]
    public void FilterDemoNames_KeepsOnlyDemoPrefixedJsonAndStripsExtension()
    {
        List<string> result = DemoCatalog.FilterDemoNames(new[]
        {
            "full_tutorial.json",
            "demo_expansion.json",
            "instr_income.json",
            "demo_towers.json",
        });

        Assert.Equal(new List<string> { "demo_expansion", "demo_towers" }, result);
    }

    [Fact]
    public void FilterDemoNames_SortsOrdinally()
    {
        List<string> result = DemoCatalog.FilterDemoNames(new[]
        {
            "demo_zebra.json",
            "demo_alpha.json",
            "demo_mid.json",
        });

        Assert.Equal(new List<string> { "demo_alpha", "demo_mid", "demo_zebra" }, result);
    }

    [Fact]
    public void FilterDemoNames_PrefixMatchIsCaseInsensitiveButNamesKeepCasing()
    {
        List<string> result = DemoCatalog.FilterDemoNames(new[]
        {
            "Demo_Capitals.JSON",
            "DEMO_gold.json",
        });

        Assert.Equal(new List<string> { "DEMO_gold", "Demo_Capitals" }, result);
    }

    [Fact]
    public void FilterDemoNames_IgnoresNonJsonAndBarePrefix()
    {
        List<string> result = DemoCatalog.FilterDemoNames(new[]
        {
            "demo_notes.txt",
            "demo_.json",
            "demo.json",
            "demonstration.json",
        });

        Assert.Equal(new List<string> { "demo_" }, result);
    }

    [Fact]
    public void FilterDemoNames_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(DemoCatalog.FilterDemoNames(new List<string>()));
    }
}
