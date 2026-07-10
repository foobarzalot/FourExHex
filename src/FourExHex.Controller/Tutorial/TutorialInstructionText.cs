/// <summary>
/// Maps the next expected player-0 <see cref="ReplayBeat"/> + current
/// <see cref="SessionState"/> to a plain-English instruction string for
/// the Tutorial Preview bottom-center message panel. Pure: no Godot
/// types, no side effects. Called from <see cref="TutorialPreviewCues"/>
/// after every <c>RefreshViews</c> so the text tracks the player's
/// progress through each multi-step beat (e.g. Buy beat shows
/// "Press the Buy Recruit button." then "Place the recruit at the
/// highlighted tile." once the player enters buy mode).
///
/// This class owns only the WHICH-message logic — the English itself
/// lives in the string store (<c>tutorial.*</c> keys), with unit levels
/// injected via <see cref="Strings.UnitName"/> tokens.
/// </summary>
public static class TutorialInstructionText
{
    public static string For(ReplayBeat next, GameState state, SessionState session) => next switch
    {
        ReplayEndTurnBeat _ => Strings.Get(StringKeys.TutorialEndTurn),
        ReplayBuyBeat bu => ForBuy(bu, state, session),
        ReplayBuildTowerBeat _ => session.Mode == SessionState.ActionMode.BuildingTower
            ? Strings.Get(StringKeys.TutorialTowerPlace)
            : Strings.Get(StringKeys.TutorialTowerPressButton),
        ReplayMoveBeat mv => ForMove(mv, state, session),
        ReplayLongPressRallyBeat _ => Strings.Get(StringKeys.TutorialRally),
        ReplayClaimVictoryBeat _ => Strings.Get(StringKeys.TutorialClaimWinNow),
        ReplayDismissClaimBeat _ => Strings.Get(StringKeys.TutorialClaimContinue),
        ReplayDismissDefeatBeat _ => Strings.Get(StringKeys.TutorialDefeatContinue),
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
            return Strings.Get(StringKeys.TutorialMovePickUp);
        }

        HexTile? srcTile = state.Grid.Get(mv.From);
        HexTile? dstTile = state.Grid.Get(mv.To);
        if (srcTile?.Occupant is Unit srcUnit && dstTile != null)
        {
            (string, string) level = ("level", Strings.UnitName(srcUnit.Level));
            if (dstTile.Owner == srcTile.Owner)
            {
                if (dstTile.Occupant is Unit dstUnit
                    && srcUnit.Level.CanCombineWith(dstUnit.Level))
                {
                    UnitLevel combined = srcUnit.Level.CombinedWith(dstUnit.Level);
                    return Strings.Get(StringKeys.TutorialMoveCombine, level,
                        ("target", Strings.UnitName(dstUnit.Level)),
                        ("combined", Strings.UnitName(combined)));
                }
                if (dstTile.Occupant is Tree)
                {
                    return Strings.Get(StringKeys.TutorialMoveClearTree, level);
                }
                if (dstTile.Occupant is Grave)
                {
                    return Strings.Get(StringKeys.TutorialMoveRemoveGrave, level);
                }
            }
            else
            {
                string key = dstTile.Occupant switch
                {
                    Tower => StringKeys.TutorialMoveCaptureTower,
                    Tree => StringKeys.TutorialMoveCaptureTree,
                    _ => StringKeys.TutorialMoveCapture,
                };
                return Strings.Get(key, level);
            }
        }
        return Strings.Get(StringKeys.TutorialMoveGeneric);
    }

    // The Buy button is single — pressing it cycles Recruit → Soldier →
    // Captain → Commander. From Mode=None the first press lands on Recruit,
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
            // First press always lands on Recruit regardless of eventual
            // target — the BuyingRecruit follow-up beat introduces the
            // upgrade-to-X messaging when needed.
            return Strings.Get(StringKeys.TutorialBuyPressRecruit);
        }
        if ((int)current.Value < (int)bu.Level)
        {
            UnitLevel nextLevel = NextLevelUp(current.Value);
            return Strings.Get(StringKeys.TutorialBuyUpgrade,
                ("level", Strings.UnitName(nextLevel)));
        }
        // Wrap-around case (target is at or below current cycle position
        // but not equal — e.g. mode=BuyingCaptain, target=Recruit). The
        // cycle eventually loops back; the dev presses through.
        return Strings.Get(StringKeys.TutorialBuyCycle,
            ("level", Strings.UnitName(bu.Level)));
    }

    private static UnitLevel NextLevelUp(UnitLevel current) => current switch
    {
        UnitLevel.Recruit => UnitLevel.Soldier,
        UnitLevel.Soldier => UnitLevel.Captain,
        UnitLevel.Captain => UnitLevel.Commander,
        _ => UnitLevel.Commander,
    };

    // Placement text for a Buy beat once the player is in the matching
    // buy mode. Mirrors Move's destination-aware phrasing: friendly
    // combine + clear tree / remove grave; enemy plain capture; enemy
    // capture-with-clear (tree / grave / tower). Falls back to the
    // bare "Place the X at the highlighted tile." when the dest has no
    // notable occupant or the grid lookup fails.
    private static string ForBuyPlacement(ReplayBuyBeat bu, GameState state)
    {
        (string, string) level = ("level", Strings.UnitName(bu.Level));
        HexTile? toTile = state.Grid.Get(bu.To);
        HexTile? capTile = state.Grid.Get(bu.Capital);
        if (toTile == null || capTile == null)
        {
            return Strings.Get(StringKeys.TutorialPlaceGeneric, level);
        }
        bool isCapture = toTile.Owner != capTile.Owner;
        HexOccupant? occ = toTile.Occupant;

        if (!isCapture && occ is Unit existing && bu.Level.CanCombineWith(existing.Level))
        {
            UnitLevel combined = bu.Level.CombinedWith(existing.Level);
            return Strings.Get(StringKeys.TutorialPlaceCombine, level,
                ("target", Strings.UnitName(existing.Level)),
                ("combined", Strings.UnitName(combined)));
        }

        string key = (occ, isCapture) switch
        {
            (Tower, true) => StringKeys.TutorialPlaceCaptureTower,
            (Tree, true) => StringKeys.TutorialPlaceCaptureTree,
            (Grave, true) => StringKeys.TutorialPlaceCaptureGrave,
            (_, true) => StringKeys.TutorialPlaceCapture,
            (Tree, false) => StringKeys.TutorialPlaceClearTree,
            (Grave, false) => StringKeys.TutorialPlaceRemoveGrave,
            _ => StringKeys.TutorialPlaceGeneric,
        };
        return Strings.Get(key, level);
    }
}
