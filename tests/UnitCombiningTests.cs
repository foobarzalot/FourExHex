using Xunit;

namespace FourExHex.Tests;

public class UnitCombiningTests
{
    // --- CanCombineWith: level-sum rule ----------------------------------

    [Theory]
    [InlineData(UnitLevel.Peasant,  UnitLevel.Peasant,  true)]   // 1+1=2 → Spearman
    [InlineData(UnitLevel.Peasant,  UnitLevel.Spearman, true)]   // 1+2=3 → Knight
    [InlineData(UnitLevel.Spearman, UnitLevel.Peasant,  true)]   // symmetric
    [InlineData(UnitLevel.Peasant,  UnitLevel.Knight,   true)]   // 1+3=4 → Baron
    [InlineData(UnitLevel.Knight,   UnitLevel.Peasant,  true)]   // symmetric
    [InlineData(UnitLevel.Spearman, UnitLevel.Spearman, true)]   // 2+2=4 → Baron
    public void CanCombineWith_ValidPairs_ReturnsTrue(UnitLevel a, UnitLevel b, bool expected)
    {
        Assert.Equal(expected, a.CanCombineWith(b));
    }

    [Theory]
    [InlineData(UnitLevel.Spearman, UnitLevel.Knight,   false)]  // 2+3=5
    [InlineData(UnitLevel.Knight,   UnitLevel.Spearman, false)]
    [InlineData(UnitLevel.Knight,   UnitLevel.Knight,   false)]  // 3+3=6
    [InlineData(UnitLevel.Peasant,  UnitLevel.Baron,    false)]  // 1+4=5
    [InlineData(UnitLevel.Baron,    UnitLevel.Peasant,  false)]
    [InlineData(UnitLevel.Baron,    UnitLevel.Baron,    false)]  // 4+4=8
    public void CanCombineWith_InvalidPairs_ReturnsFalse(UnitLevel a, UnitLevel b, bool expected)
    {
        Assert.Equal(expected, a.CanCombineWith(b));
    }

    // --- CombinedWith: the sum result ------------------------------------

    [Theory]
    [InlineData(UnitLevel.Peasant,  UnitLevel.Peasant,  UnitLevel.Spearman)]
    [InlineData(UnitLevel.Peasant,  UnitLevel.Spearman, UnitLevel.Knight)]
    [InlineData(UnitLevel.Spearman, UnitLevel.Peasant,  UnitLevel.Knight)]
    [InlineData(UnitLevel.Peasant,  UnitLevel.Knight,   UnitLevel.Baron)]
    [InlineData(UnitLevel.Knight,   UnitLevel.Peasant,  UnitLevel.Baron)]
    [InlineData(UnitLevel.Spearman, UnitLevel.Spearman, UnitLevel.Baron)]
    public void CombinedWith_KnownPairs_ReturnsExpectedLevel(UnitLevel a, UnitLevel b, UnitLevel expected)
    {
        Assert.Equal(expected, a.CombinedWith(b));
    }
}
