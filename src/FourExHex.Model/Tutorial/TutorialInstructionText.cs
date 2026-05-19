/// <summary>
/// Maps the next expected player-0 <see cref="ReplayBeat"/> + current
/// <see cref="SessionState"/> to a plain-English instruction string for
/// the Tutorial Preview bottom-center message panel. Pure: no Godot
/// types, no side effects. Called from <see cref="TutorialPreviewCues"/>
/// after every <c>RefreshViews</c> so the text tracks the player's
/// progress through each multi-step beat (e.g. Buy beat shows
/// "Press the Buy Peasant button." then "Place the peasant at the
/// highlighted tile." once the player enters buy mode).
/// </summary>
public static class TutorialInstructionText
{
    public static string For(ReplayBeat next, GameState state, SessionState session) => next switch
    {
        ReplayEndTurnBeat _ => "Press End Turn.",
        ReplayBuyBeat bu => ForBuy(bu, state, session),
        ReplayBuildTowerBeat _ => session.Mode == SessionState.ActionMode.BuildingTower
            ? "Place the tower at the highlighted tile."
            : "Press the Build Tower button.",
        ReplayMoveBeat mv => ForMove(mv, state, session),
        ReplayLongPressRallyBeat _ => "Long-press the highlighted tile to rally peasants there.",
        ReplayClaimVictoryBeat _ => "Press Win Now to claim victory.",
        ReplayDismissClaimBeat _ => "Press Continue Playing to keep going.",
        ReplayDismissDefeatBeat _ => "Press Continue.",
        _ => "",
    };

    // Move text adapts to the destination occupant when the player has
    // already picked up the source unit: combining same-color units,
    // clearing a tree, burying a grave, or capturing an enemy tile all
    // get specific phrasing so the player knows the outcome before
    // they click. Falls back to the generic prompt when the source
    // hasn't been picked up yet or when grid state can't disambiguate.
    private static string ForMove(ReplayMoveBeat mv, GameState state, SessionState session)
    {
        bool inMovingMode = session.Mode == SessionState.ActionMode.MovingUnit
            && session.MoveSource.HasValue
            && session.MoveSource.Value.Equals(mv.From);
        if (!inMovingMode)
        {
            return "Tap the highlighted unit to pick it up.";
        }

        HexTile? srcTile = state.Grid.Get(mv.From);
        HexTile? dstTile = state.Grid.Get(mv.To);
        if (srcTile?.Occupant is Unit srcUnit && dstTile != null)
        {
            if (dstTile.Owner == srcTile.Owner)
            {
                if (dstTile.Occupant is Unit dstUnit
                    && srcUnit.Level.CanCombineWith(dstUnit.Level))
                {
                    UnitLevel combined = srcUnit.Level.CombinedWith(dstUnit.Level);
                    return $"Move the selected {srcUnit.Level} onto the target {dstUnit.Level} "
                        + $"to combine them into a {combined}.";
                }
                if (dstTile.Occupant is Tree)
                {
                    return $"Move the selected {srcUnit.Level} onto the tree to clear it.";
                }
                if (dstTile.Occupant is Grave)
                {
                    return $"Move the selected {srcUnit.Level} onto the grave to remove it.";
                }
            }
            else
            {
                string suffix = dstTile.Occupant switch
                {
                    Tower => "to destroy the tower and capture the tile.",
                    Tree => "to clear the tree and capture the tile.",
                    _ => "to capture it.",
                };
                return $"Move the selected {srcUnit.Level} onto the highlighted tile " + suffix;
            }
        }
        return "Move the unit to the highlighted tile.";
    }

    // The Buy button is single — pressing it cycles Peasant → Spearman →
    // Knight → Baron. From Mode=None the first press lands on Peasant,
    // so higher targets need extra presses; the text walks the player
    // one level at a time so they're never asked to look at a level
    // beyond the next press.
    private static string ForBuy(ReplayBuyBeat bu, GameState state, SessionState session)
    {
        UnitLevel? current = SessionState.BuyModeLevel(session.Mode);
        if (current == bu.Level)
        {
            return ForBuyPlacement(bu, state);
        }
        if (current == null)
        {
            // First press always lands on Peasant regardless of eventual
            // target — the BuyingPeasant follow-up beat introduces the
            // upgrade-to-X messaging when needed.
            return "Press the Buy Peasant button.";
        }
        if ((int)current.Value < (int)bu.Level)
        {
            UnitLevel nextLevel = NextLevelUp(current.Value);
            return $"Now press the Buy Peasant button again to upgrade to a {nextLevel}.";
        }
        // Wrap-around case (target is at or below current cycle position
        // but not equal — e.g. mode=BuyingKnight, target=Peasant). The
        // cycle eventually loops back; the dev presses through.
        return $"Press the Buy Peasant button to cycle to a {bu.Level}.";
    }

    private static UnitLevel NextLevelUp(UnitLevel current) => current switch
    {
        UnitLevel.Peasant => UnitLevel.Spearman,
        UnitLevel.Spearman => UnitLevel.Knight,
        UnitLevel.Knight => UnitLevel.Baron,
        _ => UnitLevel.Baron,
    };

    // Placement text for a Buy beat once the player is in the matching
    // buy mode. Mirrors Move's destination-aware phrasing: friendly
    // combine + clear tree / remove grave; enemy plain capture; enemy
    // capture-with-clear (tree / grave / tower). Falls back to the
    // bare "Place the X at the highlighted tile." when the dest has no
    // notable occupant or the grid lookup fails.
    private static string ForBuyPlacement(ReplayBuyBeat bu, GameState state)
    {
        HexTile? toTile = state.Grid.Get(bu.To);
        HexTile? capTile = state.Grid.Get(bu.Capital);
        if (toTile == null || capTile == null)
        {
            return $"Place the {bu.Level} at the highlighted tile.";
        }
        bool isCapture = toTile.Owner != capTile.Owner;
        HexOccupant? occ = toTile.Occupant;

        if (!isCapture && occ is Unit existing && bu.Level.CanCombineWith(existing.Level))
        {
            UnitLevel combined = bu.Level.CombinedWith(existing.Level);
            return $"Place the {bu.Level} onto the target {existing.Level} "
                + $"to combine them into a {combined}.";
        }

        string suffix = (occ, isCapture) switch
        {
            (Tower, true) => " to destroy the tower and capture the tile",
            (Tree, true) => " to clear the tree and capture the tile",
            (Grave, true) => " to remove the grave and capture the tile",
            (_, true) => " to capture it",
            (Tree, false) => " to clear the tree",
            (Grave, false) => " to remove the grave",
            _ => "",
        };
        return $"Place the {bu.Level} at the highlighted tile{suffix}.";
    }
}
