// SPDX-License-Identifier: MIT
// Copyright (c) 2026 FooBarzalot
using Godot;

/// <summary>
/// C# face of the vendored godot-share plugin (addons/SharePlugin +
/// ios/plugins), which exposes an <c>Engine</c> singleton named
/// <c>SharePlugin</c> on iOS and Android only. Mirrors <see cref="IosLog"/>'s
/// harmless-on-other-platforms pattern: <see cref="Available"/> is simply
/// false on desktop, so callers can branch without platform checks.
///
/// Only the outgoing <c>share(Dictionary)</c> call is wrapped — share-target
/// (receiving) stays out of scope with the deferred file-association work.
/// </summary>
public static class ShareBridge
{
    private const string SingletonName = "SharePlugin";
    private static bool _signalsConnected;

    public static bool Available => Engine.HasSingleton(SingletonName);

    /// <summary>Open the OS share sheet for the file at
    /// <paramref name="absolutePath"/>. Fire-and-forget: the outcome
    /// arrives via the plugin's signals and is logged under the Share
    /// category.</summary>
    public static void ShareFile(string absolutePath, string mimeType, string title)
    {
        if (!Available)
        {
            Log.Warn(Log.LogCategory.Share,
                "[share] ShareFile called without the SharePlugin singleton");
            return;
        }
        GodotObject plugin = Engine.GetSingleton(SingletonName);
        ConnectSignalsOnce(plugin);
        var data = new Godot.Collections.Dictionary
        {
            { "title", title },
            { "subject", title },
            { "content", "" },
            { "file_path", absolutePath },
            { "mime_type", mimeType },
        };
        Log.Info(Log.LogCategory.Share,
            $"[share] share sheet -> '{absolutePath}' ({mimeType})");
        plugin.Call("share", data);
    }

    private static void ConnectSignalsOnce(GodotObject plugin)
    {
        if (_signalsConnected) return;
        _signalsConnected = true;
        plugin.Connect("share_completed", Callable.From((string activity) =>
            Log.Info(Log.LogCategory.Share, $"[share] share completed via '{activity}'")));
        plugin.Connect("share_canceled", Callable.From(() =>
            Log.Info(Log.LogCategory.Share, "[share] share canceled")));
        plugin.Connect("share_failed", Callable.From((string error) =>
            Log.Warn(Log.LogCategory.Share, $"[share] share failed: {error}")));
    }
}
