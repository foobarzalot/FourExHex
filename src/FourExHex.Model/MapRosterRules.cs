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
    /// non-<c>None</c> color that owns no land (it would start eliminated);
    /// and fewer than two active (non-<c>None</c>) colors.
    /// </summary>
    public static IReadOnlyList<string> ValidateForSave(
        IReadOnlyCollection<Territory> territories, PlayerKind[] kinds)
    {
        var problems = new List<string>();

        var owned = new HashSet<int>();
        foreach (Territory t in territories)
        {
            if (!t.Owner.IsNone) owned.Add(t.Owner.Index);
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
        }

        if (active < 2)
        {
            problems.Add($"A map needs at least 2 players; {active} selected.");
        }

        return problems;
    }
}
