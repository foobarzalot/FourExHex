using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

public class HudTourStepsTests
{
    private static HudTourSteps ThreeStep() => new(new List<HudTourStep>
    {
        HudTourStep.TurnCounter,
        HudTourStep.ProfitLoss,
        HudTourStep.BuyUnits,
    });

    [Fact]
    public void Ctor_StartsAtFirstStep()
    {
        HudTourSteps steps = ThreeStep();
        Assert.Equal(3, steps.Count);
        Assert.Equal(0, steps.Index);
        Assert.Equal(HudTourStep.TurnCounter, steps.Current);
    }

    [Fact]
    public void Next_AdvancesOneStep()
    {
        HudTourSteps steps = ThreeStep();
        Assert.Equal(HudTourStep.ProfitLoss, steps.Next());
        Assert.Equal(HudTourStep.BuyUnits, steps.Next());
    }

    [Fact]
    public void Next_PastLastStep_WrapsToFirst()
    {
        HudTourSteps steps = ThreeStep();
        steps.Next();          // ProfitLoss
        steps.Next();          // BuyUnits (last)
        Assert.Equal(HudTourStep.TurnCounter, steps.Next());
        Assert.Equal(0, steps.Index);
    }

    [Fact]
    public void Prev_BeforeFirstStep_WrapsToLast()
    {
        HudTourSteps steps = ThreeStep();
        Assert.Equal(HudTourStep.BuyUnits, steps.Prev());
        Assert.Equal(2, steps.Index);
    }

    [Fact]
    public void JumpTo_KnownStep_MovesCurrentAndReturnsTrue()
    {
        HudTourSteps steps = ThreeStep();
        Assert.True(steps.JumpTo(HudTourStep.BuyUnits));
        Assert.Equal(HudTourStep.BuyUnits, steps.Current);
        Assert.Equal(2, steps.Index);
    }

    [Fact]
    public void JumpTo_StepNotInList_IsNoOpAndReturnsFalse()
    {
        HudTourSteps steps = ThreeStep();
        steps.Next();          // move off the first step
        Assert.False(steps.JumpTo(HudTourStep.EndTurn)); // not in the 3-step list
        Assert.Equal(HudTourStep.ProfitLoss, steps.Current); // unchanged
    }

    [Fact]
    public void SingleStepList_NextAndPrevStayPut()
    {
        HudTourSteps steps = new(new List<HudTourStep> { HudTourStep.EndTurn });
        Assert.Equal(HudTourStep.EndTurn, steps.Next());
        Assert.Equal(HudTourStep.EndTurn, steps.Prev());
        Assert.Equal(0, steps.Index);
    }
}
