// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Handoff statics for the Tutorial Builder entry flow. The cheat menu
/// sets <see cref="ConfigRequested"/> and routes to the main menu, which
/// shows the shared play-config panel (player setup + game mode) with the
/// TutorialBuilder purpose; its forward action bakes the selections into
/// <see cref="Pending"/> and launches the builder scene, which reads and
/// clears it in <c>_Ready</c>. Absent (direct scene launch), the builder
/// falls back to 6-player Freeform.
/// Mirrors the static-state idiom of <see cref="MapEditorRequest"/>.
/// </summary>
public static class TutorialBuilderRequest
{
    public sealed class Request
    {
        /// <summary>Per-slot kinds (length = PlayerConfig): None = color
        /// out of play; anything else = active. The builder forces active
        /// slots Human for hot-seat recording.</summary>
        public PlayerKind[]? Kinds { get; init; }

        /// <summary>Mode the recording runs and saves under.</summary>
        public GameMode Mode { get; init; }
    }

    /// <summary>Cheat menu → main menu: open the tutorial-builder config
    /// panel on arrival.</summary>
    public static bool ConfigRequested { get; set; }

    /// <summary>Main menu → builder scene: the chosen roster + mode.</summary>
    public static Request? Pending { get; set; }
}
