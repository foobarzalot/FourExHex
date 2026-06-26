using System.Collections.Generic;

/// <summary>
/// Validation for a starting map's baked player roster (issue #70). A map
/// editor bakes each color's <see cref="PlayerKind"/> into the saved file;
/// for the file to describe a playable game the kinds and the painted
/// territory must agree. Pure, Godot-free, so the editor's save path can
/// validate before writing and the rule is unit-tested.
/// </summary>
public static class MapRosterRules
{
    /// <summary>
    /// Returns a human-readable problem for each inconsistency between the
    /// painted <paramref name="territories"/> and the chosen per-slot
    /// <paramref name="kinds"/>; an empty list means the map is valid to save.
    /// Flags: a color that owns land but is <see cref="PlayerKind.None"/>; a
    /// non-<c>None</c> color that owns no land (it would start eliminated); a
    /// non-<c>None</c> color that owns land but holds no capital (issue #82);
    /// and fewer than two active (non-<c>None</c>) colors.
    /// </summary>
    public static IReadOnlyList<string> ValidateForSave(
        IReadOnlyCollection<Territory> territories, PlayerKind[] kinds)
    {
        var problems = new List<string>();

        var owned = new HashSet<int>();
        var hasCapital = new HashSet<int>();
        foreach (Territory t in territories)
        {
            if (t.Owner.IsNone) continue;
            owned.Add(t.Owner.Index);
            if (t.HasCapital) hasCapital.Add(t.Owner.Index);
        }

        int active = 0;
        for (int slot = 0; slot < kinds.Length; slot++)
        {
            string name = slot < GameSettings.PlayerConfig.Length
                ? GameSettings.PlayerConfig[slot].Name
                : $"Slot {slot}";
            bool isNone = kinds[slot] == PlayerKind.None;
            bool ownsLand = owned.Contains(slot);
            if (!isNone) active++;

            if (isNone && ownsLand)
            {
                problems.Add($"{name} owns territory but is set to None.");
            }
            else if (!isNone && !ownsLand)
            {
                problems.Add($"{name} is set to play but owns no territory.");
            }
            else if (!isNone && ownsLand && !hasCapital.Contains(slot))
            {
                problems.Add($"{name} is set to play but has no capital.");
            }
        }

        if (active < 2)
        {
            problems.Add($"A map needs at least 2 players; {active} selected.");
        }

        return problems;
    }

    /// <summary>
    /// Filter <paramref name="candidates"/> down to the players whose slot
    /// (<see cref="PlayerId.Index"/>) owns at least one of
    /// <paramref name="territories"/>, preserving candidate order. Used to
    /// trim a roster (e.g. the tutorial's all-human 6) to only the colors
    /// that actually hold land, so landless slots don't show as players
    /// (issue #83). Slots are preserved, not compacted — a result may be
    /// e.g. {0,2,4} if those are the owners.
    /// </summary>
    public static List<Player> ActivePlayersForTerritories(
        IReadOnlyList<Player> candidates,
        IReadOnlyCollection<Territory> territories)
    {
        var owned = new HashSet<int>();
        foreach (Territory t in territories)
        {
            if (!t.Owner.IsNone) owned.Add(t.Owner.Index);
        }

        var active = new List<Player>(candidates.Count);
        foreach (Player p in candidates)
        {
            if (owned.Contains(p.Id.Index)) active.Add(p);
        }
        return active;
    }
}
