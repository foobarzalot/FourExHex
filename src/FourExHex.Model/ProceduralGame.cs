// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// Builds a fresh procedural <see cref="GameState"/> from a seed — the exact
/// pipeline the play scene uses for a "Random Map" Start Game
/// (<c>MapGenerator.BuildInitialGrid</c> → <c>TerritoryFinder.Recompute</c> →
/// a turn-1, empty-treasury <see cref="GameState"/>). Extracted so both
/// <c>Main</c> and the main-menu map thumbnail call one source of truth and the
/// preview cannot drift from what Start Game actually produces. Pure model
/// (Godot-free, integer-only); deterministic in <paramref name="seed"/>.
/// </summary>
public static class ProceduralGame
{
    /// <summary>
    /// Build a fresh game world: carve a seeded landmass, randomly assign its
    /// tiles to <paramref name="players"/>, partition into territories (placing
    /// capitals), and wrap it in a turn-1 <see cref="GameState"/> with an empty
    /// treasury. Same (cols, rows, players, seed) → identical state.
    /// </summary>
    public static GameState Build(
        int cols, int rows, IReadOnlyList<Player> players, int seed,
        MapGenOptions? options = null, GameMode mode = GameMode.Freeform)
    {
        var turnState = new TurnState(players);
        var treasury = new Treasury();
        MapGenResult mapGen = MapGenerator.BuildInitialGrid(cols, rows, players, seed, options);
        HexGrid grid = mapGen.Grid;
        IReadOnlyList<Territory> territories = TerritoryFinder.Recompute(
            grid, new List<Territory>(), treasury: null, randomizeCapital: true);
        return new GameState(
            grid, territories, players, turnState, treasury, mapGen.WaterCoords, mode);
    }
}
