using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Behavior of <see cref="TutorialNarrationDriver"/>: consumes
/// tutorial-only beats from the shared <see cref="ScriptCursor"/> during
/// Tutorial Preview, presenting them through the HUD and gating
/// <see cref="TutorialPreviewCues"/> until the player taps.
/// </summary>
public class TutorialNarrationDriverTests
{
    [Fact]
    public void Tick_OnDisplayTextBeat_ShowsTappableMessageAndSetsIsPresenting()
    {
        var hud = new MockHudView();
        var cursor = new ScriptCursor();
        int refreshCount = 0;
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Hello" },
        };
        var driver = new TutorialNarrationDriver(script, cursor, hud, () => refreshCount++);

        driver.Tick();

        Assert.True(driver.IsPresenting);
        Assert.True(hud.TutorialMessageTappable);
        Assert.Equal("Hello", hud.CurrentTutorialMessage);
        Assert.Equal(1, hud.ShowTappableTutorialMessageCount);
        Assert.Equal(0, cursor.Index); // cursor not advanced yet
        Assert.Equal(0, refreshCount); // refresh callback not fired yet
    }

    [Fact]
    public void Tick_WhilePresenting_IsNoOp()
    {
        var hud = new MockHudView();
        var cursor = new ScriptCursor();
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Hi" },
        };
        var driver = new TutorialNarrationDriver(script, cursor, hud, () => { });

        driver.Tick();
        driver.Tick();
        driver.Tick();

        Assert.Equal(1, hud.ShowTappableTutorialMessageCount);
        Assert.True(driver.IsPresenting);
    }

    [Fact]
    public void TutorialMessageTapped_AdvancesCursorAndCallsRefresh()
    {
        var hud = new MockHudView();
        var cursor = new ScriptCursor();
        int refreshCount = 0;
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Tap me" },
        };
        var driver = new TutorialNarrationDriver(script, cursor, hud, () => refreshCount++);

        driver.Tick();
        hud.RaiseTutorialMessageTapped();

        Assert.False(driver.IsPresenting);
        Assert.Equal(1, cursor.Index);
        Assert.Equal(1, refreshCount);
        Assert.Equal(1, hud.HideTutorialMessageCount);
        Assert.Null(hud.CurrentTutorialMessage);
    }

    [Fact]
    public void TutorialMessageTapped_AfterDismiss_DoesNotDoubleAdvance()
    {
        // Defense against the one-shot subscription mis-firing if the
        // HUD raises the event a second time after we've already
        // dismissed (e.g., a double-tap on the panel).
        var hud = new MockHudView();
        var cursor = new ScriptCursor();
        int refreshCount = 0;
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "X" },
            new ReplayDisplayTextBeat { Index = 1, Turn = 1, Actor = -1, Text = "Y" },
        };
        var driver = new TutorialNarrationDriver(script, cursor, hud, () => refreshCount++);

        driver.Tick();
        hud.RaiseTutorialMessageTapped();
        hud.RaiseTutorialMessageTapped(); // stray duplicate

        Assert.Equal(1, cursor.Index);
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public void Tick_OnPlayerActionBeat_IsNoOp()
    {
        var hud = new MockHudView();
        var cursor = new ScriptCursor();
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var driver = new TutorialNarrationDriver(script, cursor, hud, () => { });

        driver.Tick();

        Assert.False(driver.IsPresenting);
        Assert.Equal(0, hud.ShowTappableTutorialMessageCount);
        Assert.Null(hud.CurrentTutorialMessage);
        Assert.Equal(0, cursor.Index);
    }

    [Fact]
    public void Tick_AtEndOfScript_IsNoOp()
    {
        var hud = new MockHudView();
        var cursor = new ScriptCursor();
        cursor.Advance(); // past the only beat
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Skipped" },
        };
        var driver = new TutorialNarrationDriver(script, cursor, hud, () => { });

        driver.Tick();

        Assert.False(driver.IsPresenting);
        Assert.Equal(0, hud.ShowTappableTutorialMessageCount);
    }

    [Fact]
    public void Tick_SecondBeatAfterFirstDismissed_Presents()
    {
        // Two display-text beats back-to-back. After dismissing the
        // first, the next Tick (driven by onAfterRefresh) should
        // present the second.
        var hud = new MockHudView();
        var cursor = new ScriptCursor();
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "First" },
            new ReplayDisplayTextBeat { Index = 1, Turn = 1, Actor = -1, Text = "Second" },
        };
        var driver = new TutorialNarrationDriver(script, cursor, hud, () => { });

        driver.Tick();
        Assert.Equal("First", hud.CurrentTutorialMessage);
        hud.RaiseTutorialMessageTapped();

        driver.Tick();
        Assert.Equal("Second", hud.CurrentTutorialMessage);
        Assert.True(driver.IsPresenting);
        Assert.Equal(2, hud.ShowTappableTutorialMessageCount);
    }
}
