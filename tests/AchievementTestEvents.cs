// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

namespace FourExHex.Tests;

/// <summary>Canonical achievement-event payloads for tests. Individual
/// tests override just the facts they exercise via <c>with</c>.</summary>
public static class AchievementTestEvents
{
    /// <summary>A plain human win on campaign <paramref name="level"/>
    /// with all other facts at their defaults.</summary>
    public static GameEndEvent HumanWin(int level = 0) => new(level) { HumanWon = true };

    /// <summary>A game on campaign <paramref name="level"/> that ended
    /// without a human victory (AI win, stasis, or viking wipeout).</summary>
    public static GameEndEvent HumanLoss(int level = 0) => new(level) { HumanWon = false };
}
