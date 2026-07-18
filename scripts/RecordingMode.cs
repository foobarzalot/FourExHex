// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System;

/// <summary>
/// Session-scoped "video recording" flag: while active, HudView and
/// HexMapView hide promo-noisy chrome (action hint, gold chip, toasts,
/// banners, turn/swatch bar, capital warning badges) so screen captures
/// show a clean board while the game stays fully playable. Toggled only
/// from the DEBUG-only <see cref="CheatMenu"/>, so Release builds ship
/// with the flag permanently false; not persisted across sessions.
/// </summary>
public static class RecordingMode
{
    public static bool Active { get; private set; }

    /// <summary>Fired after <see cref="Active"/> flips, so views can
    /// hide/restore their chrome immediately instead of waiting for the
    /// next state-change refresh.</summary>
    public static event Action? Changed;

    public static void Toggle()
    {
        Active = !Active;
        Log.Debug(Log.LogCategory.Cheat, $"[recording] mode {(Active ? "ON" : "OFF")}");
        Changed?.Invoke();
    }
}
