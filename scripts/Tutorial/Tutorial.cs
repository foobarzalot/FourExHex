using System;
using System.Collections.Generic;

/// <summary>
/// Top-level POCO for an authored tutorial. Phase 3a held only the
/// header fields (Title / StartTurn / StartPlayer); Phase 3b adds
/// <see cref="Beats"/> now that the <see cref="Beat"/> type exists.
///
/// Serialized as an optional <c>"Tutorial"</c> block under
/// <see cref="SaveData"/>; absent on regular saves and starting maps.
/// No namespace — production scripts in this codebase are all
/// top-level (only tests use <c>namespace FourExHex.Tests</c>).
/// </summary>
public sealed class Tutorial
{
    public string Title { get; init; } = "";
    public int StartTurn { get; init; } = 1;
    public int StartPlayer { get; init; } = 0;
    public IReadOnlyList<Beat> Beats { get; init; } = Array.Empty<Beat>();
}
