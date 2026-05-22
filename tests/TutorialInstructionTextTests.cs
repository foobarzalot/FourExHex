using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Pure tests for <see cref="TutorialInstructionText"/> — the
/// English-instruction lookup driven from the next expected player-0
/// beat, the current <see cref="SessionState"/>, and (for Move beats)
/// the destination occupant read from <see cref="GameState.Grid"/>.
/// </summary>
public class TutorialInstructionTextTests
{
    private static readonly HexCoord A = new(0, 0);
    private static readonly HexCoord B = new(1, 0);
    private static readonly HexCoord C = new(2, 0);
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    // Minimal GameState builder: when a test exercises a code path that
    // doesn't touch the grid, EmptyState() avoids the territory/turn
    // ceremony entirely. Move-combine tests use StateWithGrid() instead.
    private static GameState EmptyState() => StateWithGrid(new HexGrid());

    private static GameState StateWithGrid(HexGrid grid)
    {
        var players = new List<Player>
        {
            new("Red", Red, PlayerKind.Human),
            new("Blue", Blue, PlayerKind.Computer),
        };
        return new GameState(grid, new List<Territory>(), players,
            new TurnState(players, currentPlayerIndex: 0, turnNumber: 1),
            new Treasury());
    }

    [Fact]
    public void EndTurnBeat_AlwaysSamePrompt()
    {
        var session = new SessionState();
        string text = TutorialInstructionText.For(
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 0 },
            EmptyState(), session);
        Assert.Equal("Press End Turn.", text);
    }

    [Theory]
    [InlineData(UnitLevel.Recruit, "Press the Buy Recruit button.")]
    [InlineData(UnitLevel.Soldier, "Press the Buy Recruit button.")]
    [InlineData(UnitLevel.Captain, "Press the Buy Recruit button.")]
    [InlineData(UnitLevel.Commander, "Press the Buy Recruit button.")]
    public void BuyBeat_ModeNone_PromptsButtonPress(UnitLevel level, string expected)
    {
        // First press from Mode=None always lands on Recruit regardless
        // of eventual target. The follow-up beats (BuyingRecruit →
        // upgrade-to-X) handle the escalation messaging, so the first
        // prompt stays plain.
        var session = new SessionState { Mode = SessionState.ActionMode.None };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = level,
            },
            EmptyState(), session);
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(SessionState.ActionMode.BuyingRecruit, UnitLevel.Soldier,
        "Now press the Buy Recruit button again to upgrade to a Soldier.")]
    // Step-by-step: even when the eventual target is Captain, the next
    // press only advances one level, so the instruction names the next
    // level reached (not the final target).
    [InlineData(SessionState.ActionMode.BuyingRecruit, UnitLevel.Captain,
        "Now press the Buy Recruit button again to upgrade to a Soldier.")]
    [InlineData(SessionState.ActionMode.BuyingRecruit, UnitLevel.Commander,
        "Now press the Buy Recruit button again to upgrade to a Soldier.")]
    [InlineData(SessionState.ActionMode.BuyingSoldier, UnitLevel.Captain,
        "Now press the Buy Recruit button again to upgrade to a Captain.")]
    [InlineData(SessionState.ActionMode.BuyingSoldier, UnitLevel.Commander,
        "Now press the Buy Recruit button again to upgrade to a Captain.")]
    [InlineData(SessionState.ActionMode.BuyingCaptain, UnitLevel.Commander,
        "Now press the Buy Recruit button again to upgrade to a Commander.")]
    public void BuyBeat_BuyingModeBelowTarget_PromptsNextPress(
        SessionState.ActionMode currentMode, UnitLevel target, string expected)
    {
        var session = new SessionState { Mode = currentMode };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = target,
            },
            EmptyState(), session);
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(UnitLevel.Recruit, SessionState.ActionMode.BuyingRecruit,
        "Place the Recruit at the highlighted tile.")]
    [InlineData(UnitLevel.Soldier, SessionState.ActionMode.BuyingSoldier,
        "Place the Soldier at the highlighted tile.")]
    [InlineData(UnitLevel.Captain, SessionState.ActionMode.BuyingCaptain,
        "Place the Captain at the highlighted tile.")]
    [InlineData(UnitLevel.Commander, SessionState.ActionMode.BuyingCommander,
        "Place the Commander at the highlighted tile.")]
    public void BuyBeat_MatchingBuyMode_PromptsPlacement(
        UnitLevel level, SessionState.ActionMode mode, string expected)
    {
        var session = new SessionState { Mode = mode };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = level,
            },
            EmptyState(), session);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void BuyBeat_PlaceOntoGrave_PromptsRemoveGrave()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red));
        grid.Add(new HexTile(B, Red) { Occupant = new Grave() });
        var session = new SessionState { Mode = SessionState.ActionMode.BuyingRecruit };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = UnitLevel.Recruit,
            },
            StateWithGrid(grid), session);
        Assert.Equal("Place the Recruit at the highlighted tile to remove the grave.", text);
    }

    [Fact]
    public void BuyBeat_PlaceOntoTree_PromptsClearTree()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red));
        grid.Add(new HexTile(B, Red) { Occupant = new Tree() });
        var session = new SessionState { Mode = SessionState.ActionMode.BuyingRecruit };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = UnitLevel.Recruit,
            },
            StateWithGrid(grid), session);
        Assert.Equal("Place the Recruit at the highlighted tile to clear the tree.", text);
    }

    [Theory]
    [InlineData(UnitLevel.Recruit, UnitLevel.Recruit, UnitLevel.Soldier)]
    [InlineData(UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain)]
    [InlineData(UnitLevel.Recruit, UnitLevel.Captain, UnitLevel.Commander)]
    [InlineData(UnitLevel.Soldier, UnitLevel.Soldier, UnitLevel.Commander)]
    public void BuyBeat_PlaceOntoFriendlyCombinable_PromptsCombine(
        UnitLevel buyLevel, UnitLevel existingLevel, UnitLevel combinedLevel)
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red)); // capital tile
        grid.Add(new HexTile(B, Red) { Occupant = new Unit(Red, existingLevel) });
        var session = new SessionState { Mode = SessionState.BuyModeFor(buyLevel) };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = buyLevel,
            },
            StateWithGrid(grid), session);
        Assert.Equal(
            $"Place the {buyLevel} onto the target {existingLevel} to combine them into a {combinedLevel}.",
            text);
    }

    [Fact]
    public void BuyBeat_PlaceOntoEnemyEmpty_PromptsCapture()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red));
        grid.Add(new HexTile(B, Blue));
        var session = new SessionState { Mode = SessionState.ActionMode.BuyingRecruit };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = UnitLevel.Recruit,
            },
            StateWithGrid(grid), session);
        Assert.Equal("Place the Recruit at the highlighted tile to capture it.", text);
    }

    [Fact]
    public void BuyBeat_PlaceOntoEnemyTree_PromptsClearAndCapture()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red));
        grid.Add(new HexTile(B, Blue) { Occupant = new Tree() });
        var session = new SessionState { Mode = SessionState.ActionMode.BuyingRecruit };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = UnitLevel.Recruit,
            },
            StateWithGrid(grid), session);
        Assert.Equal(
            "Place the Recruit at the highlighted tile to clear the tree and capture the tile.",
            text);
    }

    [Fact]
    public void BuyBeat_PlaceOntoEnemyGrave_PromptsRemoveAndCapture()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red));
        grid.Add(new HexTile(B, Blue) { Occupant = new Grave() });
        var session = new SessionState { Mode = SessionState.ActionMode.BuyingRecruit };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = UnitLevel.Recruit,
            },
            StateWithGrid(grid), session);
        Assert.Equal(
            "Place the Recruit at the highlighted tile to remove the grave and capture the tile.",
            text);
    }

    [Fact]
    public void BuyBeat_PlaceOntoEnemyTower_PromptsDestroyAndCapture()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red));
        grid.Add(new HexTile(B, Blue) { Occupant = new Tower() });
        var session = new SessionState { Mode = SessionState.ActionMode.BuyingCommander };
        string text = TutorialInstructionText.For(
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B, Level = UnitLevel.Commander,
            },
            StateWithGrid(grid), session);
        Assert.Equal(
            "Place the Commander at the highlighted tile to destroy the tower and capture the tile.",
            text);
    }

    [Fact]
    public void BuildTowerBeat_ModeNone_PromptsButtonPress()
    {
        var session = new SessionState { Mode = SessionState.ActionMode.None };
        string text = TutorialInstructionText.For(
            new ReplayBuildTowerBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B,
            },
            EmptyState(), session);
        Assert.Equal("Press the Build Tower button.", text);
    }

    [Fact]
    public void BuildTowerBeat_BuyingMode_StillPromptsButtonPress()
    {
        // BuildTower beat with player in a Buying mode — we want them to
        // press Build Tower (which exits the buy mode).
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.BuyingRecruit,
        };
        string text = TutorialInstructionText.For(
            new ReplayBuildTowerBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B,
            },
            EmptyState(), session);
        Assert.Equal("Press the Build Tower button.", text);
    }

    [Fact]
    public void BuildTowerBeat_BuildingTowerMode_PromptsPlacement()
    {
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.BuildingTower,
        };
        string text = TutorialInstructionText.For(
            new ReplayBuildTowerBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = A, To = B,
            },
            EmptyState(), session);
        Assert.Equal("Place the tower at the highlighted tile.", text);
    }

    [Fact]
    public void MoveBeat_ModeNone_PromptsSourcePickup()
    {
        var session = new SessionState { Mode = SessionState.ActionMode.None };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            EmptyState(), session);
        Assert.Equal("Tap the highlighted unit to pick it up.", text);
    }

    [Fact]
    public void MoveBeat_MovingUnitWrongSource_StillPromptsSourcePickup()
    {
        // Player has picked up a different unit than the script wants;
        // re-prompt them to grab the highlighted one.
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = C,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            EmptyState(), session);
        Assert.Equal("Tap the highlighted unit to pick it up.", text);
    }

    [Fact]
    public void MoveBeat_MovingUnitMatchingSource_NoOccupants_GenericPrompt()
    {
        // Source picked up but the grid has no occupant data — fall
        // through to the generic "Move the unit..." prompt.
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = A,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            EmptyState(), session);
        Assert.Equal("Move the unit to the highlighted tile.", text);
    }

    [Theory]
    [InlineData(UnitLevel.Recruit, UnitLevel.Recruit, UnitLevel.Soldier)]
    [InlineData(UnitLevel.Recruit, UnitLevel.Soldier, UnitLevel.Captain)]
    [InlineData(UnitLevel.Recruit, UnitLevel.Captain, UnitLevel.Commander)]
    [InlineData(UnitLevel.Soldier, UnitLevel.Soldier, UnitLevel.Commander)]
    public void MoveBeat_OntoFriendlyCombinable_PromptsCombine(
        UnitLevel sourceLevel, UnitLevel destLevel, UnitLevel combinedLevel)
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red) { Occupant = new Unit(Red, sourceLevel) });
        grid.Add(new HexTile(B, Red) { Occupant = new Unit(Red, destLevel) });
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = A,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            StateWithGrid(grid), session);
        Assert.Equal(
            $"Move the selected {sourceLevel} onto the target {destLevel} "
            + $"to combine them into a {combinedLevel}.",
            text);
    }

    [Fact]
    public void MoveBeat_OntoFriendlyTree_PromptsClear()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red) { Occupant = new Unit(Red, UnitLevel.Recruit) });
        grid.Add(new HexTile(B, Red) { Occupant = new Tree() });
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = A,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            StateWithGrid(grid), session);
        Assert.Equal("Move the selected Recruit onto the tree to clear it.", text);
    }

    [Fact]
    public void MoveBeat_OntoFriendlyGrave_PromptsBury()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red) { Occupant = new Unit(Red, UnitLevel.Soldier) });
        grid.Add(new HexTile(B, Red) { Occupant = new Grave() });
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = A,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            StateWithGrid(grid), session);
        Assert.Equal("Move the selected Soldier onto the grave to remove it.", text);
    }

    [Fact]
    public void MoveBeat_OntoEnemyTile_PromptsCapture()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red) { Occupant = new Unit(Red, UnitLevel.Captain) });
        grid.Add(new HexTile(B, Blue));
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = A,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            StateWithGrid(grid), session);
        Assert.Equal("Move the selected Captain onto the highlighted tile to capture it.", text);
    }

    [Fact]
    public void MoveBeat_OntoEnemyTower_PromptsDestroyTower()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red) { Occupant = new Unit(Red, UnitLevel.Captain) });
        grid.Add(new HexTile(B, Blue) { Occupant = new Tower() });
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = A,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            StateWithGrid(grid), session);
        Assert.Equal(
            "Move the selected Captain onto the highlighted tile to destroy the tower and capture the tile.",
            text);
    }

    [Fact]
    public void MoveBeat_OntoEnemyTree_PromptsChopTree()
    {
        var grid = new HexGrid();
        grid.Add(new HexTile(A, Red) { Occupant = new Unit(Red, UnitLevel.Recruit) });
        grid.Add(new HexTile(B, Blue) { Occupant = new Tree() });
        var session = new SessionState
        {
            Mode = SessionState.ActionMode.MovingUnit,
            MoveSource = A,
        };
        string text = TutorialInstructionText.For(
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = A, To = B,
            },
            StateWithGrid(grid), session);
        Assert.Equal(
            "Move the selected Recruit onto the highlighted tile to clear the tree and capture the tile.",
            text);
    }

    [Fact]
    public void LongPressRallyBeat_PromptsLongPress()
    {
        var session = new SessionState();
        string text = TutorialInstructionText.For(
            new ReplayLongPressRallyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Target = A,
            },
            EmptyState(), session);
        Assert.Equal("Long-press the highlighted tile to rally recruits there.", text);
    }

    [Fact]
    public void ClaimVictoryBeat_PromptsWinNow()
    {
        var session = new SessionState();
        string text = TutorialInstructionText.For(
            new ReplayClaimVictoryBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                ThresholdPercent = 50,
            },
            EmptyState(), session);
        Assert.Equal("Press Win Now to claim victory.", text);
    }

    [Fact]
    public void DismissClaimBeat_PromptsContinuePlaying()
    {
        var session = new SessionState();
        string text = TutorialInstructionText.For(
            new ReplayDismissClaimBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                ThresholdPercent = 50,
            },
            EmptyState(), session);
        Assert.Equal("Press Continue Playing to keep going.", text);
    }

    [Fact]
    public void DismissDefeatBeat_PromptsContinue()
    {
        var session = new SessionState();
        string text = TutorialInstructionText.For(
            new ReplayDismissDefeatBeat { Index = 0, Turn = 1, Actor = 0 },
            EmptyState(), session);
        Assert.Equal("Press Continue.", text);
    }
}
