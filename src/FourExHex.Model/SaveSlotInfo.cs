// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Lightweight metadata describing a save slot for the load-game UI.
/// Read by <see cref="SaveStore.ListSlots"/> from the file headers
/// without fully deserializing the save body.
/// </summary>
public sealed class SaveSlotInfo
{
    /// <summary>The slot's saved name (also the basename of its file).</summary>
    public string SlotName { get; }

    /// <summary>UTC unix-seconds timestamp when the slot was saved.</summary>
    public long SavedAtUnix { get; }

    /// <summary>Turn number recorded in the save.</summary>
    public int TurnNumber { get; }

    /// <summary>True if this is the autosave slot (special name).</summary>
    public bool IsAutosave { get; }

    public SaveSlotInfo(string slotName, long savedAtUnix, int turnNumber, bool isAutosave)
    {
        SlotName = slotName;
        SavedAtUnix = savedAtUnix;
        TurnNumber = turnNumber;
        IsAutosave = isAutosave;
    }
}
