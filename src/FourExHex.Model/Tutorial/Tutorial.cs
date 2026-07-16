// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
/// <summary>
/// Top-level POCO for an authored tutorial: a display title plus a
/// <see cref="Replay"/> payload that carries the full recorded
/// playthrough (initial snapshot + every state-mutating beat). The
/// Replay is captured by TutorialBuilder's Record mode (a real game
/// played as all six humans), with the controller's replay-recording
/// machinery recording the script automatically.
///
/// Serialized as an optional <c>"Tutorial"</c> block alongside the
/// <c>"Replay"</c> block under <see cref="SaveData"/>; absent on
/// regular saves and starting maps. No namespace — production
/// scripts in this codebase are all top-level (only tests use
/// <c>namespace FourExHex.Tests</c>).
/// </summary>
public sealed class Tutorial
{
    public string Title { get; init; } = "";
    public Replay Replay { get; init; } = null!;
}
