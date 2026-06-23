using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pure tests for <see cref="InteractionVerb"/> — the platform-aware
/// interaction-verb helper. <c>Configure(true)</c> (mobile) ⇒ "Tap"/"tap";
/// <c>Configure(false)</c> (desktop) ⇒ "Click"/"click". Both capitalizations
/// are covered (sentence-start <see cref="InteractionVerb.Capitalized"/> and
/// mid-sentence <see cref="InteractionVerb.Lowercase"/>).
/// </summary>
public class InteractionVerbTests
{
    [Fact]
    public void Mobile_UsesTap()
    {
        InteractionVerb.Configure(isMobile: true);
        Assert.Equal("Tap", InteractionVerb.Capitalized);
        Assert.Equal("tap", InteractionVerb.Lowercase);
    }

    [Fact]
    public void Desktop_UsesClick()
    {
        InteractionVerb.Configure(isMobile: false);
        Assert.Equal("Click", InteractionVerb.Capitalized);
        Assert.Equal("click", InteractionVerb.Lowercase);
    }
}
