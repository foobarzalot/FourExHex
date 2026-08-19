// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Copy for the Rising Tides level banner shown at each human turn start.
/// Godot-free so the wording rules are unit-testable; the controller hands
/// the result to <c>IHudView.ShowTransientBanner</c>. Everything derives
/// from <see cref="RisingTidesRules.SubmergeBudgetForRound"/> and
/// <see cref="RisingTidesRules.RoundsUntilTideRise"/> — interval changes
/// need no edits here.
/// </summary>
public static class TideBannerContent
{
    /// <summary>
    /// The banner text for the current state, or null outside Rising Tides:
    /// "Tide level L — rising in N turns" where L is this round's submerge
    /// budget and N counts rounds until the level next rises, with
    /// turn/turns singular-plural by N.
    /// </summary>
    public static string? For(GameState state)
    {
        if (state.Mode != GameMode.RisingTides) return null;
        int round = state.Turns.TurnNumber;
        int level = RisingTidesRules.SubmergeBudgetForRound(round);
        int risesIn = RisingTidesRules.RoundsUntilTideRise(round);
        string turns = risesIn == 1
            ? Strings.Get(StringKeys.TideTurnsOne)
            : Strings.Get(StringKeys.TideTurnsMany, ("n", risesIn.ToString()));
        return Strings.Get(StringKeys.TideLevelBanner,
            ("level", level.ToString()),
            ("turns", turns));
    }
}
