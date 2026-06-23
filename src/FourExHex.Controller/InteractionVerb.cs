/// <summary>
/// Single source of truth for the platform-aware interaction verb used in
/// user-facing instructional / tooltip / prompt text: "Click" on a desktop
/// build (macOS/Windows), "Tap" on a mobile build (iOS/Android).
///
/// Godot-free so it is reachable from both this Controller layer (e.g.
/// <see cref="TutorialInstructionText"/>) and the Godot-side view scripts
/// (HudView, MapEditorHudView). The platform signal can't be read here —
/// <c>OS.HasFeature("mobile")</c> lives in GodotSharp — so it is pushed in
/// once at startup via <see cref="Configure"/>, mirroring how the Godot-free
/// <see cref="Log"/> is configured from <c>LogBootstrap</c>. Defaults to the
/// desktop verb until configured, so non-Godot test runs read "Click" unless
/// they opt into mobile.
/// </summary>
public static class InteractionVerb
{
    private static bool _isMobile;

    /// <summary>Set the form factor once at startup. <c>true</c> = mobile
    /// (iOS/Android) ⇒ "Tap"; <c>false</c> = desktop ⇒ "Click".</summary>
    public static void Configure(bool isMobile) => _isMobile = isMobile;

    /// <summary>Sentence-start verb: "Tap" on mobile, "Click" on desktop.</summary>
    public static string Capitalized => _isMobile ? "Tap" : "Click";

    /// <summary>Mid-sentence verb: "tap" on mobile, "click" on desktop.</summary>
    public static string Lowercase => _isMobile ? "tap" : "click";
}
