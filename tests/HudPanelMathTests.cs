// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class HudPanelMathTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void ClampWidth_WideViewport_KeepsDesignWidth()
    {
        Assert.Equal(720f, HudPanelMath.ClampWidth(720f, 1280f, 24f), Tolerance);
    }

    [Fact]
    public void ClampWidth_IPhone13MiniPortrait_ShrinksToFitWithMargins()
    {
        // 507px logical width (13 mini portrait after display scaling) minus
        // 24px on each side.
        Assert.Equal(459f, HudPanelMath.ClampWidth(720f, 507f, 24f), Tolerance);
    }

    [Fact]
    public void ClampWidth_ViewportExactlyDesignPlusMargins_KeepsDesignWidth()
    {
        Assert.Equal(720f, HudPanelMath.ClampWidth(720f, 768f, 24f), Tolerance);
    }

    [Fact]
    public void FitHeight_ShortText_KeepsDesignHeight()
    {
        // Two wrapped lines (~80px) + 8px insets fit inside the 120px design
        // height, so the panel must not shrink below it.
        Assert.Equal(120f, HudPanelMath.FitHeight(80f, 8f, 120f), Tolerance);
    }

    [Fact]
    public void FitHeight_TallWrappedText_GrowsPanel()
    {
        // Four+ wrapped lines (~140px) + 8px top/bottom insets need 156px.
        Assert.Equal(156f, HudPanelMath.FitHeight(140f, 8f, 120f), Tolerance);
    }

    [Fact]
    public void FitHeight_TextExactlyFillsDesignHeight_KeepsDesignHeight()
    {
        Assert.Equal(120f, HudPanelMath.FitHeight(104f, 8f, 120f), Tolerance);
    }
}
