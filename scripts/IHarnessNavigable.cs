// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using System.Collections.Generic;

/// <summary>
/// A scene root the view-matrix harness can drive. Implemented by the scene
/// roots so the list of screens lives next to the code that can satisfy it and
/// cannot drift, and so the harness never learns any panel's internals.
///
/// Everything under scripts/ compiles into one assembly, so the members are
/// <c>internal</c> — no reflection, no public API surface, and the private
/// fields the screens live in stay private.
/// </summary>
internal interface IHarnessNavigable
{
    /// <summary>Screens this scene can show, in visit order.</summary>
    IReadOnlyList<string> HarnessScreenIds { get; }

    /// <summary>Show the named screen synchronously. Returns false for an id
    /// this scene cannot currently satisfy — a load-slot picker with no saves,
    /// say — which the harness records as SKIPPED rather than PASSED, so an
    /// unreachable screen never reads as covered.</summary>
    bool ShowHarnessScreen(string id);

    /// <summary>Dismiss whatever <see cref="ShowHarnessScreen"/> opened and
    /// return to the scene's resting state, so the next screen starts clean.</summary>
    void ResetHarnessScreen();
}
