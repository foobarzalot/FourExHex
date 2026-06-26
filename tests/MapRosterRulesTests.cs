using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FourExHex.Tests;

public class MapRosterRulesTests
{
    // A grid where the listed slots each own one tile; the per-slot kinds
    // are supplied separately so the test can create (in)consistencies.
    private static IReadOnlyList<Territory> TerritoriesOwnedBy(params int[] slots)
    {
        var terr = new List<Territory>();
        int q = 0;
        foreach (int slot in slots)
        {
            terr.Add(new Territory(
                PlayerId.FromIndex(slot),
                new List<HexCoord> { new HexCoord(q, 0), new HexCoord(q, 1) }));
            q += 2;
        }
        return terr;
    }

    private static PlayerKind[] Kinds(params PlayerKind[] kinds) => kinds;

    [Fact]
    public void ValidApportionment_NoProblems()
    {
        // Slots 0,1,2 own land and are non-None; 3,4,5 own nothing and are None.
        IReadOnlyList<Territory> terr = TerritoriesOwnedBy(0, 1, 2);
        PlayerKind[] kinds = Kinds(
            PlayerKind.Human, PlayerKind.Computer, PlayerKind.Computer,
            PlayerKind.None, PlayerKind.None, PlayerKind.None);

        Assert.Empty(MapRosterRules.ValidateForSave(terr, kinds));
    }

    [Fact]
    public void ColorOwningLandButNone_IsFlagged()
    {
        IReadOnlyList<Territory> terr = TerritoriesOwnedBy(0, 1);
        // Slot 1 owns land but is None.
        PlayerKind[] kinds = Kinds(
            PlayerKind.Human, PlayerKind.None, PlayerKind.None,
            PlayerKind.None, PlayerKind.None, PlayerKind.None);

        IReadOnlyList<string> problems = MapRosterRules.ValidateForSave(terr, kinds);
        Assert.Contains(problems, m => m.Contains("Blue") && m.Contains("None"));
    }

    [Fact]
    public void NonNoneColorOwningNoLand_IsFlagged()
    {
        IReadOnlyList<Territory> terr = TerritoriesOwnedBy(0, 1);
        // Slot 2 is set to play but owns no land.
        PlayerKind[] kinds = Kinds(
            PlayerKind.Human, PlayerKind.Computer, PlayerKind.Computer,
            PlayerKind.None, PlayerKind.None, PlayerKind.None);

        IReadOnlyList<string> problems = MapRosterRules.ValidateForSave(terr, kinds);
        Assert.Contains(problems, m => m.Contains("Green"));
    }

    [Fact]
    public void FewerThanTwoActive_IsFlagged()
    {
        IReadOnlyList<Territory> terr = TerritoriesOwnedBy(0);
        PlayerKind[] kinds = Kinds(
            PlayerKind.Human, PlayerKind.None, PlayerKind.None,
            PlayerKind.None, PlayerKind.None, PlayerKind.None);

        IReadOnlyList<string> problems = MapRosterRules.ValidateForSave(terr, kinds);
        Assert.Contains(problems, m => m.Contains("at least 2"));
    }

    [Fact]
    public void ActivePlayersForTerritories_DropsLandlessSlots()
    {
        // 6 candidate players; only slots 0,1,2 own land.
        List<Player> candidates = Player.BuildAllHumanRoster();
        IReadOnlyList<Territory> terr = TerritoriesOwnedBy(0, 1, 2);

        List<Player> active = MapRosterRules.ActivePlayersForTerritories(candidates, terr);

        Assert.Equal(new[] { 0, 1, 2 }, active.Select(p => p.Id.Index));
    }

    [Fact]
    public void ActivePlayersForTerritories_PreservesSlotsAndOrder()
    {
        // Owners at non-contiguous slots: the result keeps those exact slots
        // in candidate order, not a compacted 0,1,2.
        List<Player> candidates = Player.BuildAllHumanRoster();
        IReadOnlyList<Territory> terr = TerritoriesOwnedBy(4, 0, 2);

        List<Player> active = MapRosterRules.ActivePlayersForTerritories(candidates, terr);

        Assert.Equal(new[] { 0, 2, 4 }, active.Select(p => p.Id.Index));
    }

    [Fact]
    public void ActivePlayersForTerritories_NoTerritories_IsEmpty()
    {
        List<Player> candidates = Player.BuildAllHumanRoster();

        List<Player> active = MapRosterRules.ActivePlayersForTerritories(
            candidates, new List<Territory>());

        Assert.Empty(active);
    }

    [Fact]
    public void ActivePlayersForTerritories_AllOwnLand_ReturnsAll()
    {
        List<Player> candidates = Player.BuildAllHumanRoster();
        IReadOnlyList<Territory> terr = TerritoriesOwnedBy(0, 1, 2, 3, 4, 5);

        List<Player> active = MapRosterRules.ActivePlayersForTerritories(candidates, terr);

        Assert.Equal(candidates.Count, active.Count);
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, active.Select(p => p.Id.Index));
    }
}
