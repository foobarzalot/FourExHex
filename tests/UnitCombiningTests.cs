// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Xunit;

namespace FourExHex.Tests;

public class UnitCombiningTests
{
    // --- CanCombineWith: level-sum rule ----------------------------------

    [Theory]
    [InlineData(UnitLevel.Recruit,  UnitLevel.Recruit,  true)]   // 1+1=2 → Soldier
    [InlineData(UnitLevel.Recruit,  UnitLevel.Soldier, true)]   // 1+2=3 → Captain
    [InlineData(UnitLevel.Soldier, UnitLevel.Recruit,  true)]   // symmetric
    [InlineData(UnitLevel.Recruit,  UnitLevel.Captain,   true)]   // 1+3=4 → Commander
    [InlineData(UnitLevel.Captain,   UnitLevel.Recruit,  true)]   // symmetric
    [InlineData(UnitLevel.Soldier, UnitLevel.Soldier, true)]   // 2+2=4 → Commander
    public void CanCombineWith_ValidPairs_ReturnsTrue(UnitLevel a, UnitLevel b, bool expected)
    {
        Assert.Equal(expected, a.CanCombineWith(b));
    }

    [Theory]
    [InlineData(UnitLevel.Soldier, UnitLevel.Captain,   false)]  // 2+3=5
    [InlineData(UnitLevel.Captain,   UnitLevel.Soldier, false)]
    [InlineData(UnitLevel.Captain,   UnitLevel.Captain,   false)]  // 3+3=6
    [InlineData(UnitLevel.Recruit,  UnitLevel.Commander,    false)]  // 1+4=5
    [InlineData(UnitLevel.Commander,    UnitLevel.Recruit,  false)]
    [InlineData(UnitLevel.Commander,    UnitLevel.Commander,    false)]  // 4+4=8
    public void CanCombineWith_InvalidPairs_ReturnsFalse(UnitLevel a, UnitLevel b, bool expected)
    {
        Assert.Equal(expected, a.CanCombineWith(b));
    }

    // --- CombinedWith: the sum result ------------------------------------

    [Theory]
    [InlineData(UnitLevel.Recruit,  UnitLevel.Recruit,  UnitLevel.Soldier)]
    [InlineData(UnitLevel.Recruit,  UnitLevel.Soldier, UnitLevel.Captain)]
    [InlineData(UnitLevel.Soldier, UnitLevel.Recruit,  UnitLevel.Captain)]
    [InlineData(UnitLevel.Recruit,  UnitLevel.Captain,   UnitLevel.Commander)]
    [InlineData(UnitLevel.Captain,   UnitLevel.Recruit,  UnitLevel.Commander)]
    [InlineData(UnitLevel.Soldier, UnitLevel.Soldier, UnitLevel.Commander)]
    public void CombinedWith_KnownPairs_ReturnsExpectedLevel(UnitLevel a, UnitLevel b, UnitLevel expected)
    {
        Assert.Equal(expected, a.CombinedWith(b));
    }
}
