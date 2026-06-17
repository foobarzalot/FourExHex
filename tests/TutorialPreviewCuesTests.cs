using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Tests for <see cref="TutorialPreviewCues"/> — the visual-cue driver
/// for Tutorial Preview. Each test sets up a script whose next player-0
/// beat is the one under test, calls <c>Apply()</c>, and asserts the
/// right HUD / map calls were issued.
/// </summary>
public class TutorialPreviewCuesTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    // Fixture: a 6x2 rectangular grid split into two territories along
    // the middle column. Red owns the left half, Blue owns the right.
    // Each side has a capital (placed by reconciler).
    private sealed class Fixture
    {
        public GameState State { get; }
        public SessionState Session { get; }
        public MockHudView Hud { get; }
        public MockHexMapView Map { get; }
        public TutorialPreview Preview { get; }
        public ScriptCursor Cursor { get; }
        public int SelectTerritoryCalls { get; private set; }
        public int CancelActionCalls { get; private set; }
        public Action<Territory?> SelectTerritory { get; }
        public Action CancelAction { get; }
        public Territory RedTerritory { get; }
        public Territory BlueTerritory { get; }
        public TutorialPreviewCues Cues { get; }

        public Fixture(IReadOnlyList<ReplayBeat> script, int currentPlayerIndex = 0)
        {
            var players = new List<Player>
            {
                new("Red", Red, PlayerKind.Human),
                new("Blue", Blue, PlayerKind.Computer),
            };
            var grid = new HexGrid();
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    PlayerId color = c < 3 ? Red : Blue;
                    grid.Add(new HexTile(HexCoord.FromOffset(c, r), color));
                }
            }
            IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
            State = new GameState(grid, territories, players,
                new TurnState(players, currentPlayerIndex, turnNumber: 1),
                new Treasury());
            // Seed treasury so Buy/BuildTower beats are theoretically affordable.
            foreach (Territory t in territories)
            {
                if (t.HasCapital) State.Treasury.SetGold(t.Capital!.Value, 100);
            }
            RedTerritory = FindOwned(Red)!;
            BlueTerritory = FindOwned(Blue)!;

            Session = new SessionState();
            Hud = new MockHudView();
            Map = new MockHexMapView();
            Cursor = new ScriptCursor();
            Preview = new TutorialPreview(script, State, Cursor);
            SelectTerritory = t =>
            {
                SelectTerritoryCalls++;
                Session.SelectedTerritory = t;
                Map.ShowHighlight(t);
            };
            CancelAction = () =>
            {
                CancelActionCalls++;
                Session.ClearPendingAction();
                Map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Recruit);
                Map.ShowTowerTargets(System.Array.Empty<HexCoord>());
                Map.ShowMoveSource(null);
            };
            Cues = new TutorialPreviewCues(
                Preview, State, Session, Hud, Map, Red, SelectTerritory, CancelAction);
        }

        public Territory? FindOwned(PlayerId c) =>
            TerritoryLookup.FindOwnedContaining(State.Territories, c,
                FirstCoordOf(c));

        private HexCoord FirstCoordOf(PlayerId c)
        {
            foreach (HexTile tile in State.Grid.Tiles)
            {
                if (tile.Owner == c) return tile.Coord;
            }
            return new HexCoord(0, 0);
        }
    }

    private static ReplayBuyBeat BuyBeat(HexCoord capital, HexCoord to, UnitLevel level)
        => new() { Index = 0, Turn = 1, Actor = 0, Capital = capital, To = to, Level = level };

    [Fact]
    public void NotPlayer0Turn_AppliesNoCues()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var f = new Fixture(script, currentPlayerIndex: 1);
        // Simulate a stale player-0 instruction left over from the
        // previous turn — the cue must wipe it when an opponent takes
        // over.
        f.Hud.ShowTutorialMessage("Press End Turn.");

        f.Cues.Apply();

        Assert.False(f.Hud.EndTurnCtaActive);
        Assert.Empty(f.Map.LastMoveTargets);
        Assert.Equal(0, f.SelectTerritoryCalls);
        // Not player 0's turn — replace the stale player-0 instruction
        // with the passive "opponents acting" indicator.
        Assert.Equal("Opponents are taking their turns…", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void Complete_DuringAiTurn_CuesHandOff_NoMessage()
    {
        // Once the script is exhausted the tutorial graduates to ordinary
        // gameplay: cues paint nothing — no "Opponents…", no completion
        // banner — so the normal HUD / game-end handling takes over. The
        // empty script is complete from the start.
        var f = new Fixture(new List<ReplayBeat>(), currentPlayerIndex: 1);
        Assert.True(f.Preview.IsComplete);
        f.Hud.ShowTutorialMessage("sentinel");

        f.Cues.Apply();

        Assert.Equal("sentinel", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void NotPlayer0Turn_NarrationPresenting_DoesNotOverwriteMessage()
    {
        // A display-text beat can land mid opponent turn. The narration
        // driver presents it (tappable, blocks on a click); Cues must
        // NOT clobber it with the "Opponents…" indicator.
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat
            {
                Index = 0, Turn = 1, Actor = 0, Text = "Watch the enemy advance.",
            },
        };
        var f = new Fixture(script, currentPlayerIndex: 1);
        var narration = new TutorialNarrationDriver(script, f.Cursor, f.Hud, () => { });
        f.Cues.SetNarrationDriver(narration);

        narration.Tick(); // presents the display-text beat
        Assert.True(narration.IsPresenting);
        Assert.Equal("Watch the enemy advance.", f.Hud.CurrentTutorialMessage);

        f.Cues.Apply();

        // Narration text survived — not overwritten by "Opponents…".
        Assert.Equal("Watch the enemy advance.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void NarrationPresenting_SuppressesPlacementPreviews()
    {
        // While a narration beat blocks input, unit/tower placement
        // previews (the green target highlights) must be cleared so the
        // board reads cleanly behind the text. They're repainted by the
        // cue once the player dismisses the narration.
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Read this." },
        };
        var f = new Fixture(script, currentPlayerIndex: 0);
        var narration = new TutorialNarrationDriver(script, f.Cursor, f.Hud, () => { });
        f.Cues.SetNarrationDriver(narration);
        narration.Tick(); // presenting

        // Placement previews already painted on the map.
        f.Map.ShowMoveTargets(new[] { new HexCoord(1, 1) }, UnitLevel.Recruit);
        f.Map.ShowTowerTargets(new[] { new HexCoord(2, 2) });

        f.Cues.Apply();

        Assert.Empty(f.Map.LastMoveTargets);
        Assert.Empty(f.Map.LastTowerTargets);
    }

    [Fact]
    public void Player0Turn_NarrationBeatPendingNotComplete_DoesNotShowComplete()
    {
        // Player 0's turn, but the next beat is a pending DisplayText
        // (narration) with a player-0 beat behind it. NextPlayer0Beat is
        // null due to the narration gate, but the tutorial is NOT done —
        // must not prematurely show "Tutorial complete." (The narration
        // driver normally owns the panel here; this verifies the
        // IsComplete gate independently of that guard.)
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Read me." },
            BuyBeat(redCapital, destination, UnitLevel.Recruit) with { Index = 1 },
        };
        var f = new Fixture(script, currentPlayerIndex: 0);

        f.Cues.Apply();

        Assert.False(f.Preview.IsComplete);
        Assert.Null(f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void NoNextBeat_ClearsAllCtas()
    {
        var f = new Fixture(new List<ReplayBeat>());
        // Pre-set all CTAs to true to verify they all get cleared.
        f.Hud.SetCta(CtaButton.EndTurn, true, pulse: true);
        f.Hud.SetCta(CtaButton.BuyRecruit, true);
        f.Hud.SetCta(CtaButton.BuildTower, true);
        f.Hud.SetCta(CtaButton.ClaimVictoryWinNow, true);
        f.Hud.SetCta(CtaButton.ClaimVictoryContinue, true);
        f.Hud.SetCta(CtaButton.DefeatContinue, true);

        f.Cues.Apply();

        Assert.False(f.Hud.EndTurnCtaActive);
        Assert.False(f.Hud.BuyRecruitCtaActive);
        Assert.False(f.Hud.BuildTowerCtaActive);
        Assert.False(f.Hud.ClaimVictoryWinNowCtaActive);
        Assert.False(f.Hud.ClaimVictoryContinueCtaActive);
        Assert.False(f.Hud.DefeatContinueCtaActive);
        // Empty script is complete → cues hand off to ordinary gameplay:
        // CTAs cleared, no completion banner painted.
        Assert.Null(f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void EndTurnBeat_LightsEndTurnCta()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.True(f.Hud.EndTurnCtaActive);
        // Tutorial-driven End Turn CTA pulses, distinct from the
        // game-side auto-out-of-moves CTA which sets pulse=false.
        Assert.True(f.Hud.EndTurnCtaPulse);
        Assert.False(f.Hud.BuyRecruitCtaActive);
        Assert.Equal("Press End Turn.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void BuyBeat_AutoSelectsCapitalTerritoryAndLightsBuyCta()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        // Pick any other tile inside the red territory as destination.
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Recruit),
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.True(f.Hud.BuyRecruitCtaActive);
        Assert.Equal(f.RedTerritory, f.Session.SelectedTerritory);
        Assert.Equal(1, f.SelectTerritoryCalls);
        // Mode is None → target tile NOT highlighted yet.
        Assert.Empty(f.Map.LastMoveTargets);
        Assert.Equal("Press the Buy Recruit button.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void BuyBeat_AlreadySelected_DoesNotReSelect()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Recruit),
        };
        var f = new Fixture(script);
        // Pre-select correctly without going through the callback.
        f.Session.SelectedTerritory = f.RedTerritory;

        f.Cues.Apply();

        Assert.True(f.Hud.BuyRecruitCtaActive);
        Assert.Equal(0, f.SelectTerritoryCalls);
    }

    [Fact]
    public void BuyBeat_ModeMatchesLevel_OverwritesShowMoveTargetsWithSingleTile()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Captain),
        };
        var f = new Fixture(script);
        f.Session.SelectedTerritory = f.RedTerritory;
        f.Session.Mode = SessionState.ActionMode.BuyingCaptain;

        f.Cues.Apply();

        // Mode matches the target level → the player has moved past
        // the button-press step. Drop the button CTA so attention
        // shifts to the highlighted target tile.
        Assert.False(f.Hud.BuyRecruitCtaActive);
        Assert.Single(f.Map.LastMoveTargets);
        Assert.Equal(destination, f.Map.LastMoveTargets[0]);
        Assert.Equal(UnitLevel.Captain, f.Map.LastMoveTargetsLevel);
        Assert.Equal("Place the Captain at the highlighted tile.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void BuyBeat_ModeDoesNotMatchLevel_DoesNotOverwriteShowMoveTargets()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Captain),
        };
        var f = new Fixture(script);
        f.Session.SelectedTerritory = f.RedTerritory;
        f.Session.Mode = SessionState.ActionMode.BuyingRecruit;

        f.Cues.Apply();

        Assert.True(f.Hud.BuyRecruitCtaActive);
        // Mode mismatch — cue should NOT overwrite the controller's full
        // target set with a single tile.
        Assert.Empty(f.Map.LastMoveTargets);
    }

    [Fact]
    public void BuildTowerBeat_LightsCtaAndSelectsTerritory()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            new ReplayBuildTowerBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = redCapital, To = destination,
            },
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.True(f.Hud.BuildTowerCtaActive);
        Assert.Equal(f.RedTerritory, f.Session.SelectedTerritory);
        // Mode is None — tower target NOT highlighted yet.
        Assert.Empty(f.Map.LastTowerTargets);
        Assert.Equal("Press the Build Tower button.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void BuildTowerBeat_ModeBuildingTower_OverwritesShowTowerTargetsWithSingleTile()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            new ReplayBuildTowerBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = redCapital, To = destination,
            },
        };
        var f = new Fixture(script);
        f.Session.SelectedTerritory = f.RedTerritory;
        f.Session.Mode = SessionState.ActionMode.BuildingTower;

        f.Cues.Apply();

        // Mode is BuildingTower → the player has moved past the
        // button-press step. Drop the CTA in favor of the
        // highlighted target tile.
        Assert.False(f.Hud.BuildTowerCtaActive);
        Assert.Single(f.Map.LastTowerTargets);
        Assert.Equal(destination, f.Map.LastTowerTargets[0]);
        Assert.Equal("Place the tower at the highlighted tile.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void MoveBeat_NoMode_FlashesSelectCueOnFromTile_NotMoveRings()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord from = AnyOtherCoord(f0.RedTerritory, redCapital);
        // 'To' must be inside the red territory for this test setup, but
        // the cue only inspects 'From' when not in MovingUnit mode.
        HexCoord to = AnyOtherCoord(f0.RedTerritory, redCapital, from);
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = from, To = to,
            },
        };
        var f = new Fixture(script);
        f.State.Grid.Get(from)!.Occupant = new Unit(Red, UnitLevel.Soldier);

        f.Cues.Apply();

        Assert.Equal(f.RedTerritory, f.Session.SelectedTerritory);
        // The "pick me up" cue is now the flashing select-unit highlight on
        // the source tile — NOT the green move-target rings (which mean
        // "move TO here" and stay reserved for the destination).
        Assert.Equal(from, f.Map.LastSelectUnitCue);
        Assert.Empty(f.Map.LastMoveTargets);
        Assert.Equal("Tap the highlighted unit to pick it up.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void MoveBeat_MovingUnitMatchingSource_HighlightsToTile()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord from = AnyOtherCoord(f0.RedTerritory, redCapital);
        HexCoord to = AnyOtherCoord(f0.RedTerritory, redCapital, from);
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = from, To = to,
            },
        };
        var f = new Fixture(script);
        f.State.Grid.Get(from)!.Occupant = new Unit(Red, UnitLevel.Captain);
        f.Session.SelectedTerritory = f.RedTerritory;
        f.Session.Mode = SessionState.ActionMode.MovingUnit;
        f.Session.MoveSource = from;

        f.Cues.Apply();

        Assert.Single(f.Map.LastMoveTargets);
        Assert.Equal(to, f.Map.LastMoveTargets[0]);
        Assert.Equal(UnitLevel.Captain, f.Map.LastMoveTargetsLevel);
        // Once the unit is picked up, the select-unit cue lifts so only the
        // destination move-target rings remain.
        Assert.Null(f.Map.LastSelectUnitCue);
        Assert.Equal("Move the unit to the highlighted tile.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void NonMoveBeat_LeavesSelectUnitCueClear()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.Null(f.Map.LastSelectUnitCue);
    }

    [Fact]
    public void LongPressRallyBeat_SelectsAndHighlightsTarget()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord target = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            new ReplayLongPressRallyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Target = target,
            },
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.Equal(f.RedTerritory, f.Session.SelectedTerritory);
        Assert.Single(f.Map.LastMoveTargets);
        Assert.Equal(target, f.Map.LastMoveTargets[0]);
        Assert.Equal(UnitLevel.Recruit, f.Map.LastMoveTargetsLevel);
        Assert.Equal("Long-press the highlighted tile to rally recruits there.",
            f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void ClaimVictoryBeat_LightsWinNowCta()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayClaimVictoryBeat { Index = 0, Turn = 1, Actor = 0, ThresholdPercent = 50 },
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.True(f.Hud.ClaimVictoryWinNowCtaActive);
        Assert.False(f.Hud.ClaimVictoryContinueCtaActive);
        Assert.Equal("Press Win Now to claim victory.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void DismissClaimBeat_LightsContinuePlayingCta()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayDismissClaimBeat { Index = 0, Turn = 1, Actor = 0, ThresholdPercent = 50 },
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.True(f.Hud.ClaimVictoryContinueCtaActive);
        Assert.False(f.Hud.ClaimVictoryWinNowCtaActive);
        Assert.Equal("Press Continue Playing to keep going.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void DismissDefeatBeat_LightsDefeatContinueCta()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayDismissDefeatBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.True(f.Hud.DefeatContinueCtaActive);
        Assert.Equal("Press Continue.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void ReentryFromSelectTerritory_DoesNotInfiniteLoop()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Recruit),
        };
        // Build a fixture whose selectTerritory callback recurses into
        // Cues.Apply (simulating RefreshViews → onAfterRefresh).
        var players = new List<Player>
        {
            new("Red", Red, PlayerKind.Human),
            new("Blue", Blue, PlayerKind.Computer),
        };
        var grid = new HexGrid();
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                PlayerId color = c < 3 ? Red : Blue;
                grid.Add(new HexTile(HexCoord.FromOffset(c, r), color));
            }
        }
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        var state = new GameState(grid, territories, players,
            new TurnState(players, 0, 1), new Treasury());
        foreach (Territory t in territories)
        {
            if (t.HasCapital) state.Treasury.SetGold(t.Capital!.Value, 100);
        }
        var session = new SessionState();
        var hud = new MockHudView();
        var map = new MockHexMapView();
        var preview = new TutorialPreview(script, state);
        TutorialPreviewCues? cuesRef = null;
        int callbackCalls = 0;
        var cues = new TutorialPreviewCues(preview, state, session, hud, map, Red,
            t =>
            {
                callbackCalls++;
                session.SelectedTerritory = t;
                map.ShowHighlight(t);
                cuesRef!.Apply(); // recursive — must short-circuit
            },
            () => { });
        cuesRef = cues;

        cues.Apply(); // would StackOverflow without re-entrancy guard

        Assert.Equal(1, callbackCalls);
        Assert.True(hud.BuyRecruitCtaActive);
    }

    [Fact]
    public void TransitionBetweenBeats_ClearsPriorCtas()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            BuyBeat(redCapital, destination, UnitLevel.Recruit) with { Index = 1 },
        };
        var f = new Fixture(script);

        f.Cues.Apply();
        Assert.True(f.Hud.EndTurnCtaActive);

        // Advance the cursor past the EndTurn beat. (Simulates a state
        // change in which the prior beat was consumed.)
        f.Cursor.Advance();
        f.Cues.Apply();

        Assert.False(f.Hud.EndTurnCtaActive);
        Assert.True(f.Hud.BuyRecruitCtaActive);
    }

    [Fact]
    public void EndTurnBeat_PlayerInBuyingMode_AutoExitsMode()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuyingRecruit;

        f.Cues.Apply();

        Assert.True(f.Hud.EndTurnCtaActive);
        Assert.Equal(1, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.None, f.Session.Mode);
        Assert.Empty(f.Map.LastMoveTargets);
    }

    [Fact]
    public void EndTurnBeat_PlayerInMovingUnitMode_AutoExitsMode()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.MovingUnit;
        f.Session.MoveSource = new HexCoord(0, 0);

        f.Cues.Apply();

        Assert.True(f.Hud.EndTurnCtaActive);
        Assert.Equal(1, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.None, f.Session.Mode);
        Assert.Null(f.Session.MoveSource);
    }

    [Fact]
    public void BuyBeat_PlayerInBuildingTowerMode_AutoExitsMode()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Recruit),
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuildingTower;

        f.Cues.Apply();

        Assert.Equal(1, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.None, f.Session.Mode);
        Assert.True(f.Hud.BuyRecruitCtaActive);
    }

    [Fact]
    public void BuyBeat_PlayerInDifferentBuyingMode_DoesNotExitMode()
    {
        // Cycling between Buy levels is part of the design — Buy beat
        // is compatible with ANY BuyingXxx mode. Cue does not cancel.
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Captain),
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuyingRecruit;

        f.Cues.Apply();

        Assert.Equal(0, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.BuyingRecruit, f.Session.Mode);
    }

    [Fact]
    public void BuildTowerBeat_PlayerInBuyingMode_AutoExitsMode()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            new ReplayBuildTowerBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = redCapital, To = destination,
            },
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuyingSoldier;

        f.Cues.Apply();

        Assert.Equal(1, f.CancelActionCalls);
        Assert.True(f.Hud.BuildTowerCtaActive);
    }

    [Fact]
    public void MoveBeat_PlayerInMovingUnitModeWithDifferentSource_AutoExitsMode()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord from = AnyOtherCoord(f0.RedTerritory, redCapital);
        HexCoord to = AnyOtherCoord(f0.RedTerritory, redCapital, from);
        HexCoord wrongSource = AnyOtherCoord(f0.RedTerritory, redCapital, from, to);
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = from, To = to,
            },
        };
        var f = new Fixture(script);
        f.State.Grid.Get(from)!.Occupant = new Unit(Red, UnitLevel.Recruit);
        f.Session.Mode = SessionState.ActionMode.MovingUnit;
        f.Session.MoveSource = wrongSource;

        f.Cues.Apply();

        Assert.Equal(1, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.None, f.Session.Mode);
        // Back in None mode, the "pick me up" affordance is the flashing
        // select-unit cue on the source — not green move-target rings.
        Assert.Equal(from, f.Map.LastSelectUnitCue);
        Assert.Empty(f.Map.LastMoveTargets);
    }

    [Fact]
    public void LongPressRallyBeat_PlayerInBuyingMode_AutoExitsMode()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord target = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            new ReplayLongPressRallyBeat
            {
                Index = 0, Turn = 1, Actor = 0, Target = target,
            },
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuyingCaptain;

        f.Cues.Apply();

        Assert.Equal(1, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.None, f.Session.Mode);
    }

    // Helper: pick any coord inside <paramref name="t"/> that isn't in
    // <paramref name="exclude"/>.
    private static HexCoord AnyOtherCoord(Territory t, params HexCoord[] exclude)
    {
        var ex = new HashSet<HexCoord>(exclude);
        foreach (HexCoord c in t.Coords)
        {
            if (!ex.Contains(c)) return c;
        }
        throw new System.InvalidOperationException("No non-excluded coord in territory.");
    }
}
