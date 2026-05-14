using System;
using System.Collections.Generic;
using Godot;
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
    private static readonly Color Red = new(1f, 0f, 0f);
    private static readonly Color Blue = new(0f, 0f, 1f);

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
                new("Red", Red, AiKind.Human),
                new("Blue", Blue, AiKind.Heuristic),
            };
            var grid = new HexGrid();
            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 6; c++)
                {
                    Color color = c < 3 ? Red : Blue;
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
                Map.ShowMoveTargets(System.Array.Empty<HexCoord>(), UnitLevel.Peasant);
                Map.ShowTowerTargets(System.Array.Empty<HexCoord>());
                Map.ShowMoveSource(null);
            };
            Cues = new TutorialPreviewCues(
                Preview, State, Session, Hud, Map, Red, SelectTerritory, CancelAction);
        }

        public Territory? FindOwned(Color c) =>
            TerritoryLookup.FindOwnedContaining(State.Territories, c,
                FirstCoordOf(c));

        private HexCoord FirstCoordOf(Color c)
        {
            foreach (HexTile tile in State.Grid.Tiles)
            {
                if (tile.Color == c) return tile.Coord;
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
        // Not player 0's turn, but a future player-0 beat still pending
        // (the EndTurn beat the fixture set up) — clear the stale
        // instruction so it doesn't linger through opponent turns.
        Assert.Null(f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void NotPlayer0Turn_NoFurtherBeats_LeavesTutorialMessageAlone()
    {
        // When the script is exhausted, PreviewPane sets
        // "Tutorial complete." via OnFinished. The cues' AI-turn branch
        // must NOT wipe it.
        var f = new Fixture(new List<ReplayBeat>(), currentPlayerIndex: 1);
        f.Hud.ShowTutorialMessage("Tutorial complete.");

        f.Cues.Apply();

        Assert.Equal("Tutorial complete.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void NoNextBeat_ClearsAllCtas()
    {
        var f = new Fixture(new List<ReplayBeat>());
        // Pre-set all CTAs to true to verify they all get cleared.
        f.Hud.SetEndTurnCta(true);
        f.Hud.SetBuyPeasantCta(true);
        f.Hud.SetBuildTowerCta(true);
        f.Hud.SetClaimVictoryWinNowCta(true);
        f.Hud.SetClaimVictoryContinueCta(true);
        f.Hud.SetDefeatContinueCta(true);

        f.Cues.Apply();

        Assert.False(f.Hud.EndTurnCtaActive);
        Assert.False(f.Hud.BuyPeasantCtaActive);
        Assert.False(f.Hud.BuildTowerCtaActive);
        Assert.False(f.Hud.ClaimVictoryWinNowCtaActive);
        Assert.False(f.Hud.ClaimVictoryContinueCtaActive);
        Assert.False(f.Hud.DefeatContinueCtaActive);
        // No further player-0 beats → cues leave the panel alone so the
        // "Tutorial complete." message set by PreviewPane.OnFinished stays.
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
        Assert.False(f.Hud.BuyPeasantCtaActive);
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
            BuyBeat(redCapital, destination, UnitLevel.Peasant),
        };
        var f = new Fixture(script);

        f.Cues.Apply();

        Assert.True(f.Hud.BuyPeasantCtaActive);
        Assert.Equal(f.RedTerritory, f.Session.SelectedTerritory);
        Assert.Equal(1, f.SelectTerritoryCalls);
        // Mode is None → target tile NOT highlighted yet.
        Assert.Empty(f.Map.LastMoveTargets);
        Assert.Equal("Press the Buy Peasant button.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void BuyBeat_AlreadySelected_DoesNotReSelect()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Peasant),
        };
        var f = new Fixture(script);
        // Pre-select correctly without going through the callback.
        f.Session.SelectedTerritory = f.RedTerritory;

        f.Cues.Apply();

        Assert.True(f.Hud.BuyPeasantCtaActive);
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
            BuyBeat(redCapital, destination, UnitLevel.Knight),
        };
        var f = new Fixture(script);
        f.Session.SelectedTerritory = f.RedTerritory;
        f.Session.Mode = SessionState.ActionMode.BuyingKnight;

        f.Cues.Apply();

        Assert.True(f.Hud.BuyPeasantCtaActive);
        Assert.Single(f.Map.LastMoveTargets);
        Assert.Equal(destination, f.Map.LastMoveTargets[0]);
        Assert.Equal(UnitLevel.Knight, f.Map.LastMoveTargetsLevel);
        Assert.Equal("Place the Knight at the highlighted tile.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void BuyBeat_ModeDoesNotMatchLevel_DoesNotOverwriteShowMoveTargets()
    {
        var f0 = new Fixture(new List<ReplayBeat>());
        HexCoord redCapital = f0.RedTerritory.Capital!.Value;
        HexCoord destination = AnyOtherCoord(f0.RedTerritory, redCapital);
        var script = new List<ReplayBeat>
        {
            BuyBeat(redCapital, destination, UnitLevel.Knight),
        };
        var f = new Fixture(script);
        f.Session.SelectedTerritory = f.RedTerritory;
        f.Session.Mode = SessionState.ActionMode.BuyingPeasant;

        f.Cues.Apply();

        Assert.True(f.Hud.BuyPeasantCtaActive);
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

        Assert.True(f.Hud.BuildTowerCtaActive);
        Assert.Single(f.Map.LastTowerTargets);
        Assert.Equal(destination, f.Map.LastTowerTargets[0]);
        Assert.Equal("Place the tower at the highlighted tile.", f.Hud.CurrentTutorialMessage);
    }

    [Fact]
    public void MoveBeat_NoMode_HighlightsFromTileWithUnitLevel()
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
        // Place a Spearman at 'From' so the cue can read its level.
        f.State.Grid.Get(from)!.Occupant = new Unit(Red, UnitLevel.Spearman);

        f.Cues.Apply();

        Assert.Equal(f.RedTerritory, f.Session.SelectedTerritory);
        Assert.Single(f.Map.LastMoveTargets);
        Assert.Equal(from, f.Map.LastMoveTargets[0]);
        Assert.Equal(UnitLevel.Spearman, f.Map.LastMoveTargetsLevel);
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
        f.State.Grid.Get(from)!.Occupant = new Unit(Red, UnitLevel.Knight);
        f.Session.SelectedTerritory = f.RedTerritory;
        f.Session.Mode = SessionState.ActionMode.MovingUnit;
        f.Session.MoveSource = from;

        f.Cues.Apply();

        Assert.Single(f.Map.LastMoveTargets);
        Assert.Equal(to, f.Map.LastMoveTargets[0]);
        Assert.Equal(UnitLevel.Knight, f.Map.LastMoveTargetsLevel);
        Assert.Equal("Move the unit to the highlighted tile.", f.Hud.CurrentTutorialMessage);
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
        Assert.Equal(UnitLevel.Peasant, f.Map.LastMoveTargetsLevel);
        Assert.Equal("Long-press the highlighted tile to rally peasants there.",
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
            BuyBeat(redCapital, destination, UnitLevel.Peasant),
        };
        // Build a fixture whose selectTerritory callback recurses into
        // Cues.Apply (simulating RefreshViews → onAfterRefresh).
        var players = new List<Player>
        {
            new("Red", Red, AiKind.Human),
            new("Blue", Blue, AiKind.Heuristic),
        };
        var grid = new HexGrid();
        for (int r = 0; r < 2; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                Color color = c < 3 ? Red : Blue;
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
        Assert.True(hud.BuyPeasantCtaActive);
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
            BuyBeat(redCapital, destination, UnitLevel.Peasant) with { Index = 1 },
        };
        var f = new Fixture(script);

        f.Cues.Apply();
        Assert.True(f.Hud.EndTurnCtaActive);

        // Advance the cursor past the EndTurn beat. (Simulates a state
        // change in which the prior beat was consumed.)
        f.Cursor.Advance();
        f.Cues.Apply();

        Assert.False(f.Hud.EndTurnCtaActive);
        Assert.True(f.Hud.BuyPeasantCtaActive);
    }

    [Fact]
    public void EndTurnBeat_PlayerInBuyingMode_AutoExitsMode()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuyingPeasant;

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
            BuyBeat(redCapital, destination, UnitLevel.Peasant),
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuildingTower;

        f.Cues.Apply();

        Assert.Equal(1, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.None, f.Session.Mode);
        Assert.True(f.Hud.BuyPeasantCtaActive);
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
            BuyBeat(redCapital, destination, UnitLevel.Knight),
        };
        var f = new Fixture(script);
        f.Session.Mode = SessionState.ActionMode.BuyingPeasant;

        f.Cues.Apply();

        Assert.Equal(0, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.BuyingPeasant, f.Session.Mode);
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
        f.Session.Mode = SessionState.ActionMode.BuyingSpearman;

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
        f.State.Grid.Get(from)!.Occupant = new Unit(Red, UnitLevel.Peasant);
        f.Session.Mode = SessionState.ActionMode.MovingUnit;
        f.Session.MoveSource = wrongSource;

        f.Cues.Apply();

        Assert.Equal(1, f.CancelActionCalls);
        Assert.Equal(SessionState.ActionMode.None, f.Session.Mode);
        Assert.Single(f.Map.LastMoveTargets);
        Assert.Equal(from, f.Map.LastMoveTargets[0]);
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
        f.Session.Mode = SessionState.ActionMode.BuyingKnight;

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
