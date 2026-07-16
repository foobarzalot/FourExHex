// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;
using System.Collections.Generic;
using Xunit;

namespace FourExHex.Tests;

/// <summary>
/// Cursor-skip behavior for tutorial-only beats. Both
/// <see cref="TutorialPreview"/> (human side) and
/// <see cref="ReplayDrivenAi"/> (AI side) must skip past
/// <see cref="TutorialOnlyBeat"/>s without advancing the cursor — only
/// <c>TutorialNarrationDriver</c> consumes them.
/// </summary>
public class TutorialPreviewCursorTests
{
    private static readonly PlayerId Red = PlayerId.FromIndex(0);
    private static readonly PlayerId Blue = PlayerId.FromIndex(1);

    private static IReadOnlyList<Player> TwoPlayerRoster() => new List<Player>
    {
        new("Red", Red, PlayerKind.Human),
        new("Blue", Blue, PlayerKind.Computer),
    };

    private static GameState TrivialState(IReadOnlyList<Player> players)
    {
        HexGrid grid = TestHelpers.BuildRectGrid(2, 2, Red);
        IReadOnlyList<Territory> territories = TestHelpers.BuildTerritoriesFromGrid(grid);
        return new GameState(grid, territories, players, new TurnState(players), new Treasury());
    }

    [Fact]
    public void NextPlayer0Beat_ReturnsNull_WhenCursorOnTutorialOnlyBeat()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Welcome" },
            new ReplayEndTurnBeat { Index = 1, Turn = 1, Actor = 0 },
        };
        var cursor = new ScriptCursor();
        var state = TrivialState(TwoPlayerRoster());
        var preview = new TutorialPreview(script, state, cursor);

        // Cursor points at the display-text beat → NextPlayer0Beat must
        // gate (return null) so TutorialPreviewCues doesn't paint while
        // narration is pending.
        Assert.Null(preview.NextPlayer0Beat);

        // After the narration driver advances the cursor, the player-0
        // beat becomes visible.
        cursor.Advance();
        Assert.IsType<ReplayEndTurnBeat>(preview.NextPlayer0Beat);
    }

    [Fact]
    public void NextPlayer0Beat_ReturnsNull_WhenTutorialOnlyBeatBetweenCursorAndPlayer0Beat()
    {
        // Script: non-player-0 beat, tutorial-only beat, player-0 beat.
        // Cursor at index 0 (Blue's beat). The scan walks past Blue
        // (Actor 1, not 0) but should hit the tutorial-only beat next
        // and gate — not look past it to the player-0 beat behind it.
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 1 },
            new ReplayDisplayTextBeat { Index = 1, Turn = 1, Actor = -1, Text = "Now you" },
            new ReplayEndTurnBeat { Index = 2, Turn = 1, Actor = 0 },
        };
        var cursor = new ScriptCursor();
        var state = TrivialState(TwoPlayerRoster());
        var preview = new TutorialPreview(script, state, cursor);

        Assert.Null(preview.NextPlayer0Beat);
    }

    [Fact]
    public void NextPlayer0Beat_FindsBeat_WhenNoTutorialOnlyBeatBlocks()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayEndTurnBeat { Index = 0, Turn = 1, Actor = 1 },
            new ReplayMoveBeat
            {
                Index = 1, Turn = 1, Actor = 0,
                From = new HexCoord(0, 0), To = new HexCoord(1, 0),
            },
        };
        var cursor = new ScriptCursor();
        var preview = new TutorialPreview(script, TrivialState(TwoPlayerRoster()), cursor);

        Assert.IsType<ReplayMoveBeat>(preview.NextPlayer0Beat);
    }

    [Fact]
    public void AllowBuyLevel_AcceptsMatchingLevel_AndRejectsOthers()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayBuyBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                Capital = new HexCoord(0, 0),
                To = new HexCoord(1, 0),
                Level = UnitLevel.Soldier,
            },
        };
        var cursor = new ScriptCursor();
        var preview = new TutorialPreview(script, TrivialState(TwoPlayerRoster()), cursor);

        Assert.True(preview.AllowBuyLevel(UnitLevel.Soldier));
        Assert.False(preview.AllowBuyLevel(UnitLevel.Recruit));
        Assert.False(preview.AllowBuyLevel(UnitLevel.Captain));
        Assert.False(preview.AllowBuyLevel(UnitLevel.Commander));
    }

    [Fact]
    public void AllowBuyLevel_RejectsAnyLevel_WhenNextBeatIsNotBuy()
    {
        var script = new List<ReplayBeat>
        {
            new ReplayMoveBeat
            {
                Index = 0, Turn = 1, Actor = 0,
                From = new HexCoord(0, 0), To = new HexCoord(1, 0),
            },
        };
        var cursor = new ScriptCursor();
        var preview = new TutorialPreview(script, TrivialState(TwoPlayerRoster()), cursor);

        Assert.False(preview.AllowBuyLevel(UnitLevel.Recruit));
        Assert.False(preview.AllowBuyLevel(UnitLevel.Soldier));
    }

    [Fact]
    public void AllowBuyLevel_AllowsAll_WhenScriptComplete()
    {
        // Once the script is exhausted the tutorial graduates to ordinary
        // free play, so the Buy radio guard stops constraining levels.
        var script = new List<ReplayBeat>();
        var cursor = new ScriptCursor();
        var preview = new TutorialPreview(script, TrivialState(TwoPlayerRoster()), cursor);
        Assert.True(preview.IsComplete);

        Assert.True(preview.AllowBuyLevel(UnitLevel.Recruit));
        Assert.True(preview.AllowBuyLevel(UnitLevel.Commander));
    }

    [Fact]
    public void ReplayDrivenAi_ReturnsNullWithoutAdvancing_OnTutorialOnlyBeat()
    {
        var roster = TwoPlayerRoster();
        var script = new List<ReplayBeat>
        {
            new ReplayDisplayTextBeat { Index = 0, Turn = 1, Actor = -1, Text = "Watch" },
            new ReplayMoveBeat
            {
                Index = 1, Turn = 1, Actor = 1,
                From = new HexCoord(0, 0), To = new HexCoord(1, 0),
            },
        };
        var cursor = new ScriptCursor();
        var ai = new ReplayDrivenAi(script, roster, cursor);
        var state = TrivialState(roster);

        // Asking for Blue (or any actor) while cursor sits on a
        // tutorial-only beat must return null AND not advance.
        AiAction? first = ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1));
        Assert.Null(first);
        Assert.Equal(0, cursor.Index);

        AiAction? red = ai.ChooseNextAction(state, Red, new HashSet<HexCoord>(), new Random(1));
        Assert.Null(red);
        Assert.Equal(0, cursor.Index);

        // After the narration driver advances past the display-text
        // beat, the AI sees Blue's move.
        cursor.Advance();
        AiAction? blue = ai.ChooseNextAction(state, Blue, new HashSet<HexCoord>(), new Random(1));
        Assert.IsType<AiMoveAction>(blue);
        Assert.Equal(2, cursor.Index);
    }
}
