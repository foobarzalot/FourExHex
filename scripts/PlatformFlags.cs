// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// Platform bits for the <b>layout-relevant</b> branches, with a harness
/// override. A desktop runner can never reach the mobile layout paths on its
/// own — no notch, no home indicator, and a different landing-panel button
/// count — so the view-matrix sweep forces them via
/// <c>FOUREXHEX_FAKE_MOBILE</c>.
///
/// Deliberately <b>not</b> a blanket replacement for <c>OS.HasFeature</c>. The
/// non-layout mobile branches — LogBootstrap's iOS log sink, ShareInbox's
/// share polling, MailBridge's Android compose rung, MapEditorPanel's
/// touch-hover suppression — keep reading <c>OS.HasFeature</c> directly, so a
/// faked desktop run can't reroute the log into IosLog or arm a platform
/// plugin that isn't there.
///
/// Read once at static init: the string table (<c>Strings.Configure</c>) and
/// the landing button count are both consumed at build time, so this must not
/// flip mid-process. The harness therefore runs a separate pass per platform
/// shape rather than toggling between cells.
///
/// <see cref="IsIos"/> is not overridable — faking iOS would take the raw-DPI
/// scale path and the IosLog sink, neither of which is layout-shaped, and both
/// of which are covered by DisplayScaleMathTests.
/// </summary>
public static class PlatformFlags
{
    /// <summary>Set to any non-empty value to make <see cref="IsMobile"/> true
    /// on a desktop run.</summary>
    public const string FakeMobileEnvVar = "FOUREXHEX_FAKE_MOBILE";

    static PlatformFlags()
    {
        FakeMobileActive = OS.GetEnvironment(FakeMobileEnvVar).Length > 0;
        IsMobile = OS.HasFeature("mobile") || FakeMobileActive;
        IsIos = OS.HasFeature("ios");
    }

    /// <summary>True on a real phone/tablet, or when the harness forces it.</summary>
    public static bool IsMobile { get; }

    /// <summary>True only on a genuine iOS build.</summary>
    public static bool IsIos { get; }

    /// <summary>True when <see cref="IsMobile"/> is forced rather than real —
    /// logged at boot so a run's provenance is visible.</summary>
    public static bool FakeMobileActive { get; }
}
