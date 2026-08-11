// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot

namespace FourExHex.Tests;

/// <summary>Canonical achievement-event payloads for tests. Individual
/// tests override just the facts they exercise via <c>with</c>.</summary>
public static class AchievementTestEvents
{
    /// <summary>A plain freeform human win with all other facts at their
    /// defaults.</summary>
    public static GameEndEvent HumanWin() => new() { HumanWon = true };

    /// <summary>A game that ended without a human victory (AI win, stasis,
    /// or viking wipeout).</summary>
    public static GameEndEvent HumanLoss() => new() { HumanWon = false };
}
