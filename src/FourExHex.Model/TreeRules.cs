using System.Collections.Generic;

/// <summary>
/// Pure rules for tree behavior on the board:
///   - <see cref="ConvertGravesToTrees"/>: turn graves owned by a given
///     player into trees (tile color identifies the owner).
///   - <see cref="RunStartOfTurnGrowth"/>: the full start-of-turn tree
///     phase for a single player. Graves on the player's tiles convert
///     to trees, and any empty cell on their tiles with at least two
///     neighboring trees becomes a tree. Both rules evaluate against
///     a single snapshot taken at the start of the call, so newly-
///     converted graves and newly-spread trees do NOT seed further
///     conversions within the same call.
///   - <see cref="CountIncomeProducingTiles"/>: how many tiles in a
///     territory actually produce income (trees and graves both block
///     income on their tile).
/// Trees do not block unit placement: moving a unit onto a tree clears
/// it and consumes the unit's action — that rule lives in
/// <see cref="MovementRules"/>.
/// </summary>
public static class TreeRules
{
    /// <summary>
    /// Replace each <see cref="Grave"/> on a tile owned by
    /// <paramref name="owner"/> (i.e. <c>tile.Owner == owner</c>) with a
    /// <see cref="Tree"/>. Graves on other players' tiles are left in
    /// place — they only rot into trees at the start of their own
    /// owner's turn. Used internally by
    /// <see cref="RunStartOfTurnGrowth"/>; exposed so unit tests can
    /// exercise the rule in isolation.
    /// </summary>
    public static void ConvertGravesToTrees(HexGrid grid, PlayerId owner)
    {
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Occupant is Grave && tile.Owner == owner)
            {
                tile.Occupant = new Tree();
            }
        }
    }

    /// <summary>
    /// The start-of-turn tree-growth phase for the player whose turn is
    /// beginning. Both sub-rules below operate only on tiles whose color
    /// matches <paramref name="owner"/>:
    ///
    ///   1. Every <see cref="Grave"/> becomes a <see cref="Tree"/>.
    ///   2. An empty cell becomes a tree if EITHER:
    ///        - it has two or more neighboring trees (inland spread,
    ///          original behavior), OR
    ///        - it has at least one neighboring tree AND at least one
    ///          neighboring water tile (coastal spread — a single
    ///          tree neighbor next to water is enough).
    ///      "Empty" means no occupant at all (units, capitals, towers,
    ///      existing trees, and graves are not overwritten).
    ///
    /// Both rules evaluate against a single tree-snapshot captured at
    /// the start of the call, so neither newly-converted graves nor
    /// newly-spawned trees count toward another cell's neighbor tally
    /// in the same call. All conversions are then applied together.
    /// </summary>
    public static void RunStartOfTurnGrowth(HexGrid grid, PlayerId owner, IReadOnlySet<HexCoord> waterCoords)
    {
        // Snapshot trees BEFORE either rule fires. Trees produced by
        // this call do not seed further conversions within the same
        // call — both grave-conversions and spread spawns evaluate
        // against the original tree positions.
        var treeSnapshot = new HashSet<HexCoord>();
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Occupant is Tree)
            {
                treeSnapshot.Add(tile.Coord);
            }
        }

        // Rule 1: graves on owner's tiles become trees.
        ConvertGravesToTrees(grid, owner);

        // Rule 2: collect every empty owner-owned tile that either
        // has >= 2 tree neighbors (inland) or >= 1 tree neighbor and
        // >= 1 water neighbor (coastal). Apply simultaneously so a
        // new tree from this rule does not affect another cell's
        // count in the same call.
        var newTrees = new List<HexCoord>();
        foreach (HexTile tile in grid.Tiles)
        {
            if (tile.Owner != owner) continue;
            if (tile.Occupant != null) continue;

            int treeNeighbors = 0;
            bool hasWaterNeighbor = false;
            foreach (HexCoord nbr in tile.Coord.Neighbors())
            {
                if (treeSnapshot.Contains(nbr)) treeNeighbors++;
                if (waterCoords.Contains(nbr)) hasWaterNeighbor = true;
            }
            bool spreads = treeNeighbors >= 2 || (treeNeighbors >= 1 && hasWaterNeighbor);
            if (spreads) newTrees.Add(tile.Coord);
        }

        foreach (HexCoord coord in newTrees)
        {
            HexTile? tile = grid.Get(coord);
            if (tile != null && tile.Occupant == null)
            {
                tile.Occupant = new Tree();
            }
        }
    }


    /// <summary>
    /// Number of tiles in <paramref name="territory"/> that produce
    /// income for their owner. Tree and grave tiles are excluded —
    /// they are dead ground and pay nothing. Units, capitals, towers,
    /// and empty tiles all count as one income each.
    /// </summary>
    public static int CountIncomeProducingTiles(Territory territory, HexGrid grid)
    {
        int count = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile == null) continue;
            if (BlocksIncome(tile)) continue;
            count++;
        }
        return count;
    }

    /// <summary>
    /// Number of <see cref="HexTile.IsGold"/> tiles in <paramref name="territory"/>
    /// that actually produce income — i.e. gold tiles NOT occupied by a
    /// <see cref="Tree"/>/<see cref="Grave"/>. This is the count of tiles
    /// eligible for the gold income bonus (issue #45); it never exceeds
    /// <see cref="CountIncomeProducingTiles"/> for the same territory, and a
    /// gold tile under dead ground contributes to neither. Used by
    /// <see cref="IncomeRules.IncomeFor"/>.
    /// </summary>
    public static int CountGoldIncomeTiles(Territory territory, HexGrid grid)
    {
        int count = 0;
        foreach (HexCoord coord in territory.Coords)
        {
            HexTile? tile = grid.Get(coord);
            if (tile == null) continue;
            if (!tile.IsGold) continue;
            if (BlocksIncome(tile)) continue;
            count++;
        }
        return count;
    }

    /// <summary>Trees and graves are the only income-blockers — dead ground
    /// that pays nothing regardless of owner or gold status.</summary>
    private static bool BlocksIncome(HexTile tile)
        => tile.Occupant is Tree || tile.Occupant is Grave;
}
